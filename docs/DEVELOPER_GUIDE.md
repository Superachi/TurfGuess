# Turf Guess: Developer Guide

How Turf Guess works and how to change it. For what the game does and how to use the Preview tool, see the [User Guide](USER_GUIDE.md). For design decisions and the planned bot behaviour, see the spec, [DESIGN.md](../DESIGN.md).

---

## Overview

Turf Guess turns a **seed** (a 64-bit number) into a **round**:

1. a symmetric arena,
2. a paint mask in which one team covers an exact, chosen share of the floor,
3. a score counted from that mask,
4. images: the round image to post, and a reveal image.

All of this lives in **`TurfGuess.Core`**, a pure, deterministic library with no Discord or database code. The same seed and options always give the same round. Two apps sit on top of it:

```mermaid
flowchart LR
    seed([seed + options]) --> RG[RoundGenerator]
    subgraph Core["TurfGuess.Core (pure, deterministic)"]
        RG --> MG[MapGenerator<br/>cell grid]
        MG --> PS[PaintSimulator<br/>pixel mask]
        PS --> SC[Score<br/>counted from mask]
        PS --> MR[MapRenderer<br/>SkiaSharp]
        CP[ColorPairs<br/>pick per seed] --> MR
    end
    MR --> PNG([round / reveal PNG])
    Preview[TurfGuess.Preview<br/>console app] --> RG
    Bot[TurfGuess.Bot<br/>planned] -.-> RG
```

| Project | Status | Role |
|---|---|---|
| `src/TurfGuess.Core` | ✅ | Map generation, paint, scoring, rendering, color pairs |
| `src/TurfGuess.Preview` | ✅ | Console app that writes sample rounds and contact sheets |
| `tests/TurfGuess.Core.Tests` | ✅ | xUnit tests for Core (77 tests) |
| `src/TurfGuess.Bot` | 🚧 Planned (milestone 3) | Discord bot: scheduler, voting buttons, SQLite, reveals. See DESIGN.md §2, §6–8 |

**The one rule to remember:** the **paint mask** is the single source of truth. The score is counted from it and the renderer only draws it. Nothing visual may ever change what's counted.

---

## Getting started

**Requirements:** the .NET 10 SDK. SkiaSharp brings its own native binaries on Windows. A Linux host additionally needs `SkiaSharp.NativeAssets.Linux`, which gets added with the bot.

```bash
dotnet build                                   # whole solution
dotnet test                                    # 77 tests, ~6 s
dotnet run --project src/TurfGuess.Preview -c Release -- --count 12 --seed 42
```

Use `-c Release` for the Preview app. Generation is noticeably slower in Debug.

### Repository layout

```
TurfGuess/
  DESIGN.md                      # the spec: decisions, behaviour, planned bot
  TurfGuess.slnx                 # .NET 10 XML solution
  docs/                          # this guide + the user guide
  src/
    TurfGuess.Core/
      GeneratorOptions.cs        # all generation options + validation
      RoundGenerator.cs          # seed + options -> GeneratedRound (entry point)
      Grid/                      # Grid<T>, CellType, Side, RectI, Symmetry
      MapGen/                    # MapGenerator, GameMap
      Paint/                     # PaintSimulator, CornerRounding, PaintResult
      Scoring/                   # Score, ScoreResult
      Render/                    # MapRenderer, RenderStyle, ColorPairs, ColorMath
      Noise/GradientNoise.cs     # seeded Perlin-style noise + fBm
      Util/Rng.cs                # SplitMix64 PRNG
    TurfGuess.Preview/Program.cs
  tests/TurfGuess.Core.Tests/
  preview/                       # Preview output (git-ignored)
```

---

## The pipeline in detail

### Entry point: `RoundGenerator.Generate(seed, options)`

```csharp
var round = RoundGenerator.Generate(seed, new GeneratorOptions());
var style = ColorPairs.Pick(seed);                    // colors for this round
using var image = MapRenderer.RenderRound(round, style, caption: "Round #12");
byte[] png = MapRenderer.EncodePng(image);
```

`Generate`:
1. **Validates** the options. Invalid options throw `ArgumentException` with every problem listed.
2. Draws the **round-level choices** from their own random stream (`Rng.Derive(seed, 0xD1FF)`): which side wins (50/50), the target winner share (uniform in `[MinWinnerShare, MaxWinnerShare]`) and the target unpainted share (uniform in `[UnpaintedMin, UnpaintedMax]`). Because these come from their own stream, they stay the same however many attempts the map needs.
3. Tries **attempts** until one succeeds. Attempt `a` uses map seed `Derive(seed, 2a+1)` and paint seed `Derive(seed, 2a+2)`. The map generator retries internally too, and the paint step can reject a result (see its sanity checks).
4. Returns a `GeneratedRound`: seed, `GeneratorVersion`, winner, targets, attempt count, map, paint mask and score.

### Determinism

- **`Rng`** is SplitMix64. It's used instead of `System.Random` so that output never depends on the runtime's implementation. `Rng.Derive(seed, salt)` makes independent sub-seeds without consuming state.
- **`GradientNoise`** is our own Perlin-style noise with fBm, seeded through `Rng`, instead of a third-party library.
- **`GeneratorVersion`** (in `RoundGenerator`) must be **bumped whenever a change alters what a given seed produces**. The bot will store it with each round, so old rounds can be told apart. Current version: **4**.
- Floating-point math (`Math.Log`, `Math.Exp`, …) is deterministic on a given platform. It may differ in the last bits across CPU architectures, which is why the bot stores each round's result and image instead of regenerating them.

### Stage 1: map generation (`MapGen/MapGenerator.cs`)

Works on a **cell grid**, by default 96×54. Cell types: `Void`, `Floor`, `Wall`, `SpawnLeft`, `SpawnRight`. Only the **left half** is generated. Symmetry then produces the right half.

| Step | Details |
|---|---|
| Bounds | Left half, with a one-cell void border around the map |
| **Spine** | One band from the left edge to the center line. Height 30–48% of the grid, shifted off the middle row by up to ±1/9 of the height. Its overlap with its own rotated copy is the main crossing. |
| **Wings** | 2–4 rectangles (20–45% of the half's width, 15–35% of the height) hanging off the top or bottom of the spine, overlapping it by at least 4 cells, so the body is always connected |
| **Center plaza** | In 70% of maps: a rectangle at the center line, symmetric around the middle row, so it crosses the center in both symmetry modes |
| **Spawn** | A 3×5 pad at x=2 inside the spine, with a 9-cell-wide platform around it |
| **Holes** | 0–2 void rectangles (4–8 cells), kept away from the spawn platform |
| **Obstacles** | Up to `MaxObstaclesPerHalf` (4), until `ObstacleDensity` × floor is covered. 75% are blocks of 2–4 × 2–4 cells, 25% are straight walls 4–8 long and 1–2 thick. Each needs a one-cell ring of floor (no slivers against the outline) and a 3-cell gap to other obstacles. |
| Snap | Spine, wings, plaza and holes snap to a **2-cell step** |
| **Symmetry** | `Symmetry.Apply`: `Rotate` maps `(x, y) → (w−1−x, h−1−y)`, and `Mirror` maps `(x, y) → (w−1−x, y)`. Spawn types are swapped. |
| **Validation** | Flood fill (4-connected) from the left spawn. Unreached floor becomes void, and walls not connected to a wall that touches reached floor become void (so the inner cells of big blocks survive). The map is rejected if the right spawn isn't reached, the walkable fraction is outside `[MinFloor, MaxFloor]`, or the center line has fewer than 4 crossing rows or more than 5 separate crossings. |

A rejected map retries with the next sub-seed, up to `MaxAttempts` (50) times, before throwing `MapGenerationException`.

### Stage 2: paint (`Paint/PaintSimulator.cs`)

Produces a **pixel mask** at `PixelsPerCell` (16) resolution, by default 1536×864. Each pixel is a `PixelKind`: `Void`, `Wall`, `Unpainted`, `Left`, `Right`, `SpawnLeft` or `SpawnRight`.

```mermaid
flowchart TD
    A[1. cost field<br/>noise per floor cell] --> B[2. Dijkstra from each spawn<br/>dL, dR per cell]
    B --> C[3. pixel mask from cells<br/>+ corner rounding]
    C --> D[upsample dL, dR to pixels]
    D --> E[4. unpainted mask<br/>closeness to spawn × diligence + noise<br/>+ sputter splats, quantile]
    E --> F[5. ownership score s<br/>ln dR − ln dL + jitter + grain]
    F --> G[5b. estimate front t₀<br/>add fight noise + splats]
    G --> H[6. quantile threshold<br/>exactly nLeft pixels go Left]
    H --> I[7. final mask]
```

1. **Cost field.** For each walkable cell, `cost = exp(1.1 · fbm(x·0.09, y·0.09))`. Varying costs make paint flow faster through some areas, which gives finger-like borders.
2. **Distance fields.** An 8-connected Dijkstra from every cell of each spawn pad, with no corner cutting past walls, gives `dL` and `dR`. They're computed once and don't depend on the margin.
3. **Pixel mask and corner rounding.** Each pixel takes its cell's type, with floor as `Unpainted`. Then `CornerRounding.Apply` rounds the outline (see below), and `dL` and `dR` are bilinearly interpolated for every floor pixel, ignoring non-walkable neighbours.
4. **Unpainted mask.** The highest-`u` pixels become unpainted, so the floor **near spawns** gets neglected, broken into patches by the noise. The cut-off is a **quantile**, so the share lands exactly on target. This step doesn't depend on the margin.
   ```
   u = max(nL·(1 − dL/max), nR·(1 − dR/max)) + 0.7·holes + 0.06·jitter + 0.02·grain
   ```
   - **Diligence:** `n` is 1.0 for a normal team and 0.25 for a team that painted its spawn diligently. Each team is diligent with probability `DiligentSpawnChance` (30%), drawn from `Rng.Derive(paintSeed, 7)` and exposed as `PaintResult.DiligentLeft/Right`. The unpainted share then shifts to the other spawn, or toward the middle when both teams were diligent.
   - **Spawn trail:** pixels within a cost-distance of `2.5 · (1 + 0.8·jitter)` of a pad are never unpainted. The jitter makes the trail's edge ragged rather than a smooth ring.
   - **Sputters (4b):** `jitter` and `grain` make the paint/unpainted edge ragged. A preliminary quantile gives the estimated border `u₀`. Then 14–25 small splats (radius 0.35–0.9 cells, 3–7 droplets each) are stamped at pixels near a spawn (closeness ≥ 0.4) with `u₀ ≤ u < u₀ + 0.15`, setting `u = −∞` (forced painted). Only after that does the final quantile run.
5. **Ownership score.** For painted pixels, `s = ln(dR+1) − ln(dL+1) + 0.10·jitter + 0.035·grain`. Positive means closer to the left team.
   - **Fight zone (5b).** A preliminary threshold `t₀` for the target margin estimates where the front line will be. Then `0.9 · exp(−((s − t₀)/0.35)²) · fight-noise` is added, so both colors interleave in blotches across a band in the middle.
   - **Splats.** 28–45 stamps (wobbly circles with a radius of 0.6–1.5 cells, plus 2–5 droplets each), mostly placed where `|s − t₀| < 0.5`. Each one sets `s = t₀ ± 8`, so it shows its team's color wherever it lands.
6. **Exact threshold.** `nLeft = round(leftShare · nPainted)`. The painted pixels are sorted by `s`, and the top `nLeft` become `Left`, the rest `Right`. This is equivalent to biasing the teams' speed (`t = 2 ln b`), but it needs no search and is **exact to one pixel**.
7. **Sanity check.** The losing team must still have paint right next to its own spawn. Otherwise the result reads like a wipeout and the attempt is rejected.

#### Corner rounding (`Paint/CornerRounding.cs`)

This works like CSS `border-radius`, on the floor/void outline at pixel level:
- **Opening** (erode, then dilate) with a disk of radius `CornerRadius` rounds the **outer** corners.
- **Closing** (dilate, then erode) with a disk of radius `InnerCornerRadius` fills the **inner** corners.

"Solid" means anything that isn't void, so walls and spawn pads are never cut. Erosion and dilation use an **exact Euclidean distance transform** (Felzenszwalb & Huttenlocher, separable over columns then rows), so the cost doesn't depend on the radius. Pixels outside the image count as void.

Radii are in cells and must be **below 1**. An opening with radius *r* removes features narrower than 2*r*, and the generator can produce 2-cell-wide parts.

### Stage 3: scoring (`Scoring/Score.cs`)

`Score.Count(mask)` counts `Left`, `Right` and `Unpainted` pixels. Everything else isn't floor.

| Value | Formula | Used for |
|---|---|---|
| `WinnerShare` | `max(L, R) / (L + R)` | Difficulty. This is what `Min/MaxWinnerShare` control. |
| `CoverageLeft`, `CoverageRight` | `L / floor`, `R / floor` | The percentages shown at the reveal |
| `UnpaintedShare` | `U / floor` | Shown at the reveal |
| `Winner` | `L > R ? Left : Right` | Ties are impossible because `MinWinnerShare > 50` |

### Stage 4: rendering (`Render/MapRenderer.cs`)

`RenderRound` and `RenderReveal` return an `SKBitmap`, and `EncodePng` turns it into PNG bytes. Pixels are written into an `int[]` buffer (RGBA8888) and copied into the bitmap. SkiaSharp is used for text, the banner and encoding.

| Layer | Look |
|---|---|
| Void | Flat `#282A30` |
| Unpainted floor | Flat `#9497A0` (148, 151, 160) |
| Paint | Depends on the ink style (below) |
| Spawn pads | Team color at 42–55% brightness, diagonal stripes |
| Walls | Light grey top with a bevel: lighter top and left edges, darker bottom and right edges (`p/8` px) |
| Drop shadow | Pixels whose `(x − p/4, y − p/3)` neighbour is a wall are darkened to 62% |
| Caption | Optional rounded label at the top-left |
| Reveal banner | 18% of the image height: "X WINS", a coverage bar, labelled percentages |

#### Ink styles (`RenderStyle.Ink`)

| Style | How it works |
|---|---|
| **Marbled** (default) | **Domain-warped noise**: `q = fbm(p)`, `r = fbm(p + 2.2q)`, `v = fbm(p + 2.2r)`, at 1/16 cells. `sin(v · 1.4 · 2π)` gives a tone in [−1, 1], folded into swirled bands. |
| **Flat** | Tone 0 everywhere: one solid color per team |
| **Shaded** | Tone from broad swells (60%) plus a bump map lit from the top-left (40%), with gloss highlights, and cool shadow on borders |

Each tone is applied by `Ink.Shade` in **OKLCh** (`ColorMath`, Björn Ottosson's OKLab). Positive tones shift the hue toward yellow (100°) and lighten. Negative tones shift it toward blue (265°), darken and add a little chroma. Marbled uses a 15° hue shift and a ±0.015 lightness swing, so the bands differ mostly in hue.

**Yellow/green damping (Marbled only).** Yellow and green base colors turn olive when their hue shifts, so `MarbleHueShiftScale(baseHue)` reduces the shift to 35% for base hues between 90° and 150°, fading back to full strength by 65° and 175° with a cosine curve.

All tuning values are `private const` fields grouped by style in `MapRenderer.cs`: `Shade…`, `Marble…` and `Damp…`.

#### Color pairs (`Render/ColorPairs.cs`)

`ColorPairs.Default` holds three pairs: Pink/Green, Orange/Blue and Yellow/Blue. `ColorPairs.Pick(seed)` picks a pair, and which color goes left, from `Rng.Derive(seed, 0xC0102)`. Each `TeamStyle` has a `Name`, a hex `Color` and an `Emoji`, which goes on the vote button.

---

## Options reference

All options are mutable classes, so the bot can bind them from `appsettings.json` (DESIGN.md §7). `GeneratorOptions.Validate()` enforces the ranges below.

**`MapOptions`**

| Option | Default | Valid range | Meaning |
|---|---|---|---|
| `GridWidth` | 96 | even, ≥ 16 | Cells across |
| `GridHeight` | 54 | ≥ 16 | Cells down |
| `Symmetry` | `Rotate` | `Rotate`, `Mirror` | How the right half is made |
| `ObstacleDensity` | 0.03 | 0–0.3 | Target fraction of floor covered by obstacles |
| `MaxObstaclesPerHalf` | 4 | 0–20 | Hard cap on obstacles |
| `MinFloor` / `MaxFloor` | 0.45 / 0.70 | 0 < min < max < 1 | Allowed walkable fraction of the grid |
| `MaxAttempts` | 50 | ≥ 1 | Sub-seeds to try |

**`PaintOptions`**

| Option | Default | Valid range | Meaning |
|---|---|---|---|
| `UnpaintedMin` / `UnpaintedMax` | 0.05 / 0.15 | 0 ≤ min ≤ max < 0.5 | Unpainted share of the floor, picked per round |
| `Splatter` | true | | Splats in the fight zone and sputters at the unpainted border near spawns |
| `DiligentSpawnChance` | 0.3 | 0–1 | Per team, per round: chance it painted its own spawn area diligently |
| `PixelsPerCell` | 16 | 2–64 | Mask and image resolution |
| `CornerRadius` | 0.75 | 0–<1 | Outer corner radius, in cells |
| `InnerCornerRadius` | 0.5 | 0–<1 | Inner corner radius, in cells |

**`DifficultyOptions`**

| Option | Default | Valid range | Meaning |
|---|---|---|---|
| `MinWinnerShare` / `MaxWinnerShare` | 50.5 / 54.0 | 50 < min ≤ max < 100 | Winner's share of the painted floor, in % |

**`RenderStyle`** (not validated, set per round)

| Option | Default | Meaning |
|---|---|---|
| `GameName` | "Turf Guess" | Used in captions |
| `Ink` | `Marbled` | `Flat`, `Shaded`, `Marbled` |
| `Left` / `Right` | Orange / Blue | Normally set by `ColorPairs.Pick` |

---

## Testing

```bash
dotnet test
```

| File | Covers |
|---|---|
| `MapGeneratorTests` | Same seed gives the same map; different seeds differ; both symmetry modes exact; every walkable cell reachable from both spawns; floor fraction and spawn placement |
| `RoundGeneratorTests` | Counted winner share = target (±1 pixel); unpainted share = target; identical masks per seed; both sides win across seeds; coverage adds up; walls, void and spawns don't count; invalid difficulty rejected; renderer outputs PNGs of the right size |
| `CornerRoundingTests` | Distance transform vs brute force; square corners cut; L-shaped corners filled; radius 0 is a no-op; 2-cell corridor survives the max radius; spawns never cut; rounded rounds still hit the target |
| `SpawnPaintTests` | Diligence chance 0/1 controls the flags; mix at the default; diligent teams leave less unpainted floor near spawn; targets unaffected; invalid chance rejected |
| `ColorPairsTests` | Pairs valid and distinct; pick is deterministic; all pairs and both sides used; empty list rejected |
| `MarbleDampingTests` | Yellow/green damped; other hues full; smooth fade |

Round tests run at `PixelsPerCell = 4` to stay fast. The math doesn't depend on resolution. Internals are visible to the test project via `InternalsVisibleTo`.

**Visual checks** aren't automated. Use the Preview app, and compare `contact_sheet.png` before and after a change.

---

## Common changes

**Tweak how paint or the map looks**
Change the `private const` values at the top of `PaintSimulator.cs`, `MapGenerator.cs` or the relevant section of `MapRenderer.cs`, then check with the Preview app. If the change affects what a seed produces (map or mask, not just colors), **bump `GeneratorVersion`**.

**Add or change a color pair**
Edit `ColorPairs.Default`. Keep names and emoji distinct within a pair, and update the count in `ColorPairsTests`. Preview candidates first with `--pairs --pairs-file`.

**Add an ink style**
1. Add a value to `InkStyle`.
2. Add a branch to the `style.Ink switch` in `MapRenderer.RenderMap` that returns `(tone, gloss)` arrays.
3. Optionally give it its own `Ink` hue and lightness profile.

**Add a generation option**
1. Add a property with a default to the right options class.
2. Add a check to `Validate()`.
3. Use it in the generator.
4. Bump `GeneratorVersion` if the default changes the output.
5. Document it here and in DESIGN.md §7.

**Performance**
A round takes about 250–450 ms at 1536×864 in Release: the two Dijkstra runs, two sorts over the ~700k floor pixels, four distance transforms and the marble noise (5 fBm calls per paint pixel). The bot only needs one round per day.
