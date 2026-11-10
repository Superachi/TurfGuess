# Turf Guess — Design Document

> Working title. The game name, team names and colors are all configurable (see [Configuration](#7-configuration)).

## 1. Concept

A Discord bot runs one round of a guessing game per day. Today it plays in a **single server**. The plan is to play **one shared daily puzzle across every server it's invited to**, Wordle style (section 12). Each round it posts an image of a procedurally generated, symmetric arena. Two colors of paint have flowed into it, one from the left spawn and one from the right. One color covers slightly more floor than the other. Players vote on which color they think won by clicking one of two buttons on the message. At the next daily round time (normally 24 hours later) the bot reveals the exact percentages and posts the next round.

The fun comes from:
- **Close calls.** Margins are small and tunable, so it really takes eyeballing.
- **Organic paint on hard-edged maps.** The map is blocky and obviously symmetric, while the paint flows organically around walls and obstacles, so you can't just compare the two halves.
- **The daily ritual.** One guess per day, plus streaks and a leaderboard.

Cheating (pixel counting) is explicitly **out of scope**. It's a casual game.

---

## 2. Daily cycle

There is always exactly one active round. At the configured **round time** (e.g. 09:00 local) every day:

1. **Reveal round N−1**
   - Close voting: set the round's status to `Revealed` in the database. **That is what closes voting** (see [Voting rules](#voting-rules)). Then edit the round message to disable both buttons and mark it **Closed**.
   - Post the reveal: the winning color, the exact percentages, how many people voted each way, and a list of the correct guessers (see [Reveal message](#reveal-message)).
2. **Post round N**
   - Generate a new map and paint it, then post the image with vote buttons.

```
Day 1 09:00   Post R1
Day 2 09:00   Reveal R1 → Post R2
Day 3 09:00   Reveal R2 → Post R3
...
```

### Schedule rules
- **The round time is wall-clock time in the configured `TimeZone`**, not a fixed 24-hour gap. A round's `reveal_at` is the **next time RoundTime comes around in TimeZone after the round is posted**. When the clocks change for daylight saving time, that round lasts 23 or 25 hours, and the posts stay at 09:00 local.
- **Catch-up rounds are shorter, never later.** If the bot was down and catches up at 15:00, it reveals the overdue round and posts a new one right away. That new round still reveals at the next 09:00, so the schedule never drifts.
- **If several days were missed,** only the overdue open round is revealed, then one new round is posted. Missed days are skipped, not posted after the fact.
- **First start (no rounds in the database):** post a round right away. It reveals at the next RoundTime. Starting at 08:55 would give a 5-minute round, so if less than `MinRoundLength` (default 6 hours) is left, the reveal moves to the RoundTime after that.
- The scheduler wakes for the earliest `reveal_at` of all open rounds, not on a fixed daily timer. Catch-up and normal operation then follow the same code path.

### Voting rules
- **Pure binary guess:** which team covered more floor? There are no margin brackets or bonus guesses.
- **Voting uses message buttons.** The round message has two buttons, one per team, each labeled with that round's color emoji and name (e.g. `🩷 Pink` / `💚 Green`, see [Colors](#colors)). One click is a vote, with no commands or extra steps.
- **Votes are hidden.** A click gets an **ephemeral** reply that only the voter sees ("You voted 🟧 Orange. You can change it until the reveal."). Nobody can see the running split, so players can't just follow the crowd.
- **One vote per player.** Clicking the other button changes the vote, and the newest click wins.
- **Live voter list (optional):** the round message can show who has voted ("N players have voted: @a, @b, …"), sorted by name, and be updated as votes come in. It shows who voted, never what they picked. Edits are debounced (e.g. at most once every 30 s) to stay within rate limits.

**Source of truth:** each click is written to the `votes` table straight away (an upsert on `(round_id, user_id)`).

**Closing is enforced by the database, not by the buttons.** Disabling the buttons is only a visual cue: a click can be in flight while the edit happens, and the edit itself can fail. So the vote upsert is **conditional on the round being open**, in a single statement:

```sql
INSERT INTO votes (round_id, user_id, choice, voted_at)
SELECT @RoundId, @UserId, @Choice, @Now
WHERE EXISTS (SELECT 1 FROM rounds WHERE id = @RoundId AND status = 'Open')
ON CONFLICT (round_id, user_id) DO UPDATE SET choice = excluded.choice, voted_at = excluded.voted_at;
```

- If 1 row is affected, the vote is recorded and the voter gets the ephemeral confirmation.
- If 0 rows are affected, the voter gets an ephemeral "Voting for this round is closed." The same happens for clicks on any old round message.
- SQLite runs writes one at a time, so a vote and the `Open → Revealed` update can never overlap. A vote either lands before the reveal counts votes, or it's rejected. Each button has a fixed custom id that includes the round id (`vote:{roundId}:{side}`). Clicks are routed by that id, so the buttons keep working after a restart (see [Tech stack](#8-tech-stack--structure)).

> While the bot is offline, Discord shows clicks as "This interaction failed", and they aren't recorded. That's accepted: the player can just click again once the bot is back.

### Reveal message
- Winning team, both coverage percentages (share of total floor) and the unpainted share. See [Definitions](#41-definitions).
- Vote counts per team.
- **The correct guessers by name**, sent as mentions with `AllowedMentions.None`. They render as clickable names but **don't ping anyone**.
- If the list would go over Discord's 2000-character message limit, it's cut off with "…and N more".
- The reveal image (the same map with a result banner) is attached.

### Round lifecycle

```
Pending ──(message posted, message_id saved)──▶ Open ──(voting closed)──▶ Revealed ──(reveal posted)──▶ Done
```

| Status | Meaning |
|---|---|
| `Pending` | The row is written: seed, results, `reveal_at` and image path. The message may or may not have been posted yet. |
| `Open` | The round message is posted and `message_id` is saved. Votes are accepted. |
| `Revealed` | Voting is closed and the votes are final. The reveal message may or may not have been posted yet. |
| `Done` | The reveal message is posted and `reveal_message_id` is saved. |

**Invariant:** at most one round is ever not `Done`. The next round is created only after the previous one reaches `Done`.

**Order of operations.** The database row is always written *before* the matching Discord call, and the Discord ID is saved *after* it:

- **Posting:**
  1. Generate the round and write it as `Pending`.
  2. Send the message.
  3. Save `message_id` and set the status to `Open`.
- **Revealing:**
  1. Set the status to `Revealed`. This closes voting.
  2. Disable the buttons.
  3. Send the reveal.
  4. Save `reveal_message_id` and set the status to `Done`.

**Crash recovery on startup.** The scheduler finishes any round that's left halfway:

| Found | Action |
|---|---|
| `Pending` | Check the bot's recent messages in the channel (the last 50) for one whose button id contains this round's id (`vote:{roundId}:…`). If found, adopt its `message_id` and move to `Open`. Otherwise post the message now. This **never posts a duplicate**. |
| `Open`, past `reveal_at` | Reveal it as normal. |
| `Revealed` | Check the recent messages for the reveal of this round (its embed footer carries `Round #{id}`). If found, adopt it and move to `Done`. Otherwise disable the buttons (harmless if already done) and post the reveal. |
| Everything `Done` | Create the next round. |

Every step can safely run twice, so recovery and normal operation use the same code path: "advance the current round one step until it's `Open` and waiting, or `Done`."

### Reliability
- All round state is kept in SQLite, so the bot survives restarts.
- On startup the bot checks for a round that is past its reveal time. If it finds one, it reveals it and posts a new round immediately (catch-up, see [Schedule rules](#schedule-rules)). The buttons on the open round keep working without any re-registration.
- The answer is stored only server-side. Each round stores its **seed**, so any image can be regenerated exactly for debugging.

---

## 3. Map generation

### 3.1 Requirements
- **Symmetric**, so neither team has a structural advantage.
- **Interesting shapes:** an irregular outline, obstacles, chokepoints and open areas.
- **Fully connected:** every floor cell can be reached from both spawns.
- **Spawns** on the far left and far right.
- **Deterministic** from a seed.

### 3.2 Symmetry mode
The default is **180° rotational (point) symmetry**: the right half is the left half rotated 180° around the map center. This is deliberate and follows how competitive arena maps are built. With a mirror, a corner that one team peeks around on its right would be a left-side peek for the other team. Rotation gives both teams identical sightlines, which removes the **right-peek advantage**.

The symmetry is meant to be **obvious**. It signals that the map is fair, and the guessing challenge comes from the paint, not from the layout.

| Mode | Description |
|---|---|
| `rotate` (default) | Rotate the left half 180° around the center. No peek-side advantage. |
| `mirror` | Reflect across the vertical center line. Available as an option, but it reintroduces the peek-side asymmetry. |

### 3.3 Pipeline
Work on a **cell grid** (e.g. 96×54 cells), then upscale for rendering. **The grid is the look.** Shapes stay rectilinear and built from blocks, like real arena geometry (platforms, corridors, ledges), with **no smoothing** into organic blobs. Paint is the only organic element on the map, and the contrast between hard-edged architecture and flowing ink is part of the style.

**Keep it simple.** Real arena maps are **one solid body** with a clean, stepped outline, a few gaps, and a handful of chunky obstacles. They're mostly open floor, not a maze of rooms and corridors. All layout shapes snap to a **2-cell step**, which keeps outlines clean.

1. **Generate the left half** from a few large rectangles:
   - **Spine:** one wide band (30–48% of the map height) from the left edge to the center line, shifted a little off the middle row. Its overlap with its own rotated copy is the main way across the center.
   - **Wings:** 2–4 big rectangles hanging off the top or bottom of the spine. Each overlaps the spine, so the body is always connected.
   - **Center plaza** (70% of maps): a rectangle at the center line, symmetric around the middle row, so it always crosses the center in both symmetry modes.
   - **Spawn pad** and a small platform around it, inside the spine near the left edge.
   - **Holes:** 0–2 sizeable rectangular gaps (4–8 cells), forming pits or notches in the outline.
2. **Place obstacles** (blocks and walls, which are impassable and can't be painted)
   - **At most 4 per half** by default (`MaxObstaclesPerHalf`), up to the configured density: chunky blocks (2–4 × 2–4 cells) and the odd straight wall (4–8 long, 1–2 thick).
   - Each obstacle needs a ring of floor around it, so it never leaves a thin sliver against the outline, and a 3-cell gap to other obstacles.
   - Keep a buffer zone around the spawn pad clear.
3. **Apply symmetry** to produce the full map.
4. **Validate connectivity**
   - Flood fill from the left spawn and remove any floor cells it doesn't reach (islands), plus any obstacles left floating in void. The inner cells of a big block count as part of that block.
   - Reject and regenerate the map (next sub-seed) if:
     - the right spawn isn't reachable,
     - the floor area is outside the `[min_floor, max_floor]` fraction,
     - the center line has too few crossing points (at least 4 crossing rows, at most 5 separate crossings).
5. **Output** a grid with cell types `VOID`, `FLOOR`, `WALL` and `SPAWN_L` / `SPAWN_R`.

### 3.4 Possible later variants
- **Map "styles"** with different presets (open field, corridors, islands joined by bridges).
- **Height/levels:** purely cosmetic, drawn with drop shadows.
- **Named maps:** generate a fun random name for each map ("Rusty Pier", "Neon Quarry").

---

## 4. Paint simulation

### 4.1 Definitions
| Term | Meaning |
|---|---|
| **Floor pixels** | Pixels of the final mask whose cell is `FLOOR`. Walls, void and spawn pads never count. |
| **Painted floor** | Floor pixels painted by either team: `L + R`. |
| **Unpainted share** | `U / (L + R + U)`, the fraction of floor nobody painted. |
| **Winner share** (difficulty) | `max(L, R) / (L + R)`, a share of the **painted** floor. This is what `MinWinnerShare` / `MaxWinnerShare` control. |
| **Coverage** (shown at reveal) | `L / floor` and `R / floor`, a share of **total** floor, as in the real thing, shown next to the unpainted share. |

Example: winner share 52%, unpainted 10% → the reveal shows **46.8% vs 43.2%, 10.0% unpainted**.

Using painted floor for difficulty means the unpainted share can vary from round to round without changing how hard the guess is.

### 4.2 Approach: two distance fields and one threshold
Paint should *flow* from each spawn and wrap around obstacles, not just split the screen in half.

1. **Cost field.** Build a noise-based cost per floor cell, so paint moves faster through some areas than others. This produces the organic, finger-like borders.
2. **Distance fields.** Run a Dijkstra from each spawn over the floor grid to get `dL` and `dR`, the raw cost-weighted distances. Paint can't cross `WALL` or `VOID`, so the distances wrap around obstacles. They're computed **once per round** and don't depend on the target margin.
3. **Upsample to mask resolution, with rounded corners.** Build the pixel mask from the cell types (the render resolution, e.g. 16 px/cell), then round the outline like CSS `border-radius`:
   - a morphological **opening** with a disk of radius `CornerRadius` rounds the **outer** (convex) corners;
   - a morphological **closing** with a disk of radius `InnerCornerRadius` fills the **inner** (concave) corners with a curve.

   Both use an exact Euclidean distance transform, so the radius doesn't affect speed. Only the floor/void outline changes: walls and spawn pads are never cut. Radii are in cells and must stay below 1, or 2-cell-wide parts of the map would vanish (outer) or 2-cell gaps would fill in (inner). This happens **before** scoring, so rounded-off corners never count. Then bilinearly interpolate `dL` and `dR` for every floor pixel.
4. **Unpainted mask** (fixed before the margin is set):
   - **The spawn areas get neglected, not the middle.** Real players rush forward, so the floor near their own spawn is what stays unpainted. Each pixel gets the key `u = closeness to the nearest spawn + holes noise`, and the pixels with the highest `u` become unpainted. The noise breaks this up into patches instead of a solid ring.
   - **Diligent teams.** Each round, each team independently has a `DiligentSpawnChance` (default 30%) of having painted its own spawn area diligently. A diligent team's closeness term is scaled down to 0.25, so far less floor near its spawn stays unpainted, and the unpainted share shifts elsewhere: to the other spawn, or toward the middle if both teams were diligent.
   - A short **spawn trail** right around each pad (cost-distance < 2.5, with a ragged edge) is always painted.
   - **Sputters at the unpainted border.** The key also gets fine jitter and grain, so the edge between paint and unpainted floor is ragged like the front line. A preliminary quantile estimates where that border will be, and 14–25 small splats with droplets (radius 0.35–0.9 cells) are stamped just inside the unpainted side, near spawns. Stamped pixels are forced painted, and the final quantile still hits the target share exactly. They're controlled by `Splatter`.
   - The cut-off is chosen as a **quantile**, so the unpainted share lands on a value picked at random in `[UnpaintedMin, UnpaintedMax]`.
   - Because this step doesn't involve the speed bias, changing the margin never changes the unpainted area.
5. **Ownership score per painted pixel:**
   ```
   s(p) = ln dR(p) − ln dL(p) + jitter(p) + fight(p) + splat(p)
   ```
   - `s > t` means left owns the pixel. Positive values mean "closer to left".
   - `jitter` is fine, high-frequency noise. It roughens the borders at pixel level, which gives the painted look.
   - **`fight`: the front line is where the battle happened.** A preliminary threshold `t₀` for the target margin estimates where the front will be. Blotchy noise (about 3-cell blobs) is added with a weight that falls off with distance from that front (`exp(−((s − t₀)/0.35)²)`). Both colors then interleave in islands across a wide band in the middle, rather than meeting along one clean line.
   - `splat` comes from 28–45 seeded splat stamps (circles plus droplets), each assigned a random team. They're placed mostly **near the front line**, where the fight was. Inside a stamp the score is set to `t₀ ± large`, so the stamp takes that team's color wherever it lands.
   - Jitter, fight noise and splats are part of the score field **from the start**, not cosmetic extras added later.
6. **Threshold for the exact margin.** The threshold `t` is the speed bias. It's equivalent to scaling the distances by `dL·b` vs `dR/b` with `t = 2 ln b`. Since everything else is fixed, `t` is simply a **quantile of `s` over the painted pixels**:
   - Pick the winning side (50/50) and the target winner share `w` within the difficulty range.
   - Left wins → `t` = the `(1 − w)` quantile of `s`. Right wins → `t` = the `w` quantile.
   - This hits the target **to the pixel**, with no iterative search and no tolerance band. At 1536×864 one pixel is under 0.0001% of the floor.
7. **Final mask.** The result is a per-pixel `byte[]` with `None / Left / Right / Unpainted` (plus wall/void). **This mask is the single source of truth** for both the score and the renderer.

### 4.3 Guarantees
- **Exact margin:** the stored share is the share counted from the final mask, and it equals the target up to one pixel.
- **No ties:** `MinWinnerShare > 50`, and the threshold is placed between distinct values of `s`. Ties in `s` are broken by pixel index, so the result is deterministic.
- **Sanity checks.** The seed is rejected (and the next sub-seed tried) if:
  - painted floor is under 50% of the image's floor,
  - the losing team ends up with no connected region touching its own spawn (it would look like a wipeout rather than a close match).

> Rule: **the score is always counted from the final paint mask**, never from an intermediate step. The renderer only *draws* that mask. Anti-aliasing, shading and shadows affect how it looks, never the count.

---

## 5. Rendering

- Upscale the grid (e.g. 16 px/cell → 1536×864) and render with SkiaSharp.
- Layers, bottom to top:
  1. Background (void): flat dark grey (`#282A30`), no texture.
  2. Floor: flat neutral gray. **No grid lines**: the cell grid shapes the map but isn't drawn.
  3. Paint: drawn in one of three **ink styles** (`RenderStyle.Ink`):
     - **Flat**: one solid color per team, for a minimalist look.
     - **Marbled** (default): an oil-slick look. Domain-warped noise (noise sampled at coordinates pushed around by more noise, twice) is folded through a sine into flowing, swirled bands. The bands swing between the warm and cool hue-shifted tones of the team color (up to 15° of hue) at nearly constant lightness. For yellow and green base colors (OKLCh hue 90°–150°, fading out by 65° and 175°), the hue shift is reduced to 35%, because those colors turn muddy olive when shifted. Glossy streaks along the band crests are supported but switched off.
     - **Shaded**: a thick ink layer with hue shifting:
     - Each pixel gets a *tone* from broad, smooth noise swells plus a faint bump map lit from the top-left.
     - Tone is applied in **OKLCh** (a perceptual color space). Lights get slightly lighter and drift **warm, toward yellow**; shadows get slightly darker, a little richer and drift **cool, toward blue**, by up to 20° of hue. Pink goes rose in the lights and magenta-purple in the shadows; green goes from yellow-green to bluish green.
     - Borders between colors sit in cool shadow, which outlines each region, plus soft gloss highlights on the bumps that face the light, which make the ink look wet.
  4. Walls and obstacles: raised look (a lighter top face plus a drop shadow).
  5. Spawn pads in team colors.
  6. Optional: round number and game name in a corner.
- The renderer receives the final paint mask from the paint step (section 4.2, step 7) at the same resolution and draws it. It never counts anything itself.

### Reveal image
- The same map with a result banner showing both percentages, and the winner highlighted.
- Later: an animated GIF of a counting bar filling up, for the suspense.

### Colors
- **Every round picks a color pair** from a list, deterministically from its seed. Which color goes to the left team is also picked from the seed. Each team's name is its color name ("Pink vs Green"), and each color has an emoji for its vote button (🩷 💚 💙 🩵 🧡 💜 💛 ❤️).
- **Built-in pairs:** three hand-picked regular-battle ink pairs from the Inkipedia ink gallery. The other 15 regular-battle pairs looked too garish and were dropped:

  | Pair | Colors |
  |---|---|
  | Pink vs Green | `#E0559A` / `#2E7D32` (adjusted: pink brighter, green darker than the wiki's `#C83D79` / `#409D3B`) |
  | Orange vs Blue | `#DE6624` / `#343BC4` |
  | Yellow vs Blue | `#D0BE08` / `#3A0CCD` |

  More pairs are being chosen from established palettes. The candidates are reviewed with `--pairs --pairs-file` in the Preview app.
- The list can be replaced in config (`ColorPairs`). Config validation checks that the two colors in each pair differ enough in lightness and hue, and that their names and emoji differ.
- The pair picked for a round is **stored with the round** (section 6), so a config change never changes the colors of a round that's already posted.

---

## 6. Data model (SQLite)

The schema is created on startup (`Data/Database.cs`, safe to run every time) and the database runs in WAL mode. Times are **Unix seconds (UTC)**. Discord ids are stored as `INTEGER`: snowflakes fit in a signed 64-bit integer, and the code casts to and from `ulong` at the edges.

```sql
rounds (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  channel_id        INTEGER NOT NULL,   -- single server, so no guild_id needed
  status            TEXT    NOT NULL,   -- 'Pending' | 'Open' | 'Revealed' | 'Done'
  message_id        INTEGER,            -- NULL until posted
  reveal_message_id INTEGER,            -- NULL until the reveal is posted
  seed              INTEGER NOT NULL,   -- < 2^63
  generator_version INTEGER NOT NULL,   -- RoundGenerator.GeneratorVersion at creation
  image_path        TEXT    NOT NULL,   -- round PNG
  reveal_image_path TEXT    NOT NULL,   -- reveal PNG, rendered at creation too
  created_at        INTEGER NOT NULL,
  posted_at         INTEGER,
  reveal_at         INTEGER NOT NULL,   -- next RoundTime in TimeZone after creation
  left_name         TEXT    NOT NULL,   -- color pair picked for this round, e.g. 'Pink'
  left_color        TEXT    NOT NULL,   -- '#E0559A'
  left_emoji        TEXT    NOT NULL,
  right_name        TEXT    NOT NULL,
  right_color       TEXT    NOT NULL,
  right_emoji       TEXT    NOT NULL,
  winner            TEXT    NOT NULL,   -- 'left' | 'right'
  winner_share      REAL    NOT NULL,   -- share of painted floor, 0–1 (difficulty)
  share_left        REAL    NOT NULL,   -- coverage, share of total floor, 0–1
  share_right       REAL    NOT NULL,
  unpainted         REAL    NOT NULL    -- share of total floor, 0–1
)

votes (
  round_id  INTEGER NOT NULL REFERENCES rounds (id),
  user_id   INTEGER NOT NULL,
  user_name TEXT    NOT NULL,           -- Discord username at the latest vote, for logs (added by an automatic upgrade)
  choice    TEXT    NOT NULL,           -- 'left' | 'right'
  voted_at  INTEGER NOT NULL,           -- time of the latest change
  PRIMARY KEY (round_id, user_id)
)
```

`votes` is upserted on every button click, only while the round is `Open` (see [Voting rules](#voting-rules)). The row as it stands when the round moves to `Revealed` is the final vote.

A partial unique index enforces the "one unfinished round" invariant:

```sql
-- Every row in this partial index has the same key value (1), so at most one row can exist.
CREATE UNIQUE INDEX one_active_round ON rounds (status <> 'Done') WHERE status <> 'Done';
```

**Images** are rendered once, when the round is created, and stored on disk (`Images:Directory`). If a file is missing, for example after a crash between creating the row and saving the images, it's regenerated from the stored seed. A warning is logged if the generator version has changed since.

Streaks, accuracy and the leaderboard can be computed from `votes` joined with `rounds`. Add a cached `user_stats` table later only if it's needed.

---

## 7. Configuration

`appsettings.json` (read from the app's own folder) is bound to `BotOptions`. User secrets and `TURFGUESS_*` environment variables override it, e.g. `TURFGUESS_Discord__ChannelId`. The options are validated at startup, and the bot refuses to start with a list of every problem. Validation covers:
- the token and channel,
- the round time and time zone,
- generator ranges,
- color pairs (valid hex, distinct names and emoji).

```jsonc
{
  "Game": {
    "Name": "Judd's Judgment",     // shown in message titles, image captions and the reveal
    "RoundTime": "09:00",
    "TimeZone": "Europe/Amsterdam",   // IANA id; .NET resolves it on Windows and Linux
    "ShowVoteCount": true,            // who has voted (names only) on the round message
    "MaxListedWinners": 50,           // extra names are cut off with "…and N more"
    "MinRoundLength": "06:00:00",     // first-start rounds shorter than this skip to the following RoundTime
    "Ink": "Marbled"                  // "Marbled" | "Flat" | "Shaded"
  },
  // Optional. Leave out to use the built-in pairs (section 5, Colors).
  // Each round picks one pair from its seed; emoji are shown on the vote buttons.
  "ColorPairs": [
    { "A": { "Name": "Pink",   "Color": "#E0559A", "Emoji": "🩷" },
      "B": { "Name": "Green",  "Color": "#2E7D32", "Emoji": "💚" } },
    { "A": { "Name": "Orange", "Color": "#DE6624", "Emoji": "🧡" },
      "B": { "Name": "Blue",   "Color": "#343BC4", "Emoji": "💙" } }
  ],
  "Generator": {
    "Difficulty": {
      // Fixed range. Each round picks a winner share uniformly in [min, max].
      // Measured as a share of PAINTED floor: max(L, R) / (L + R). Must be > 50.
      "MinWinnerShare": 50.3,   // %
      "MaxWinnerShare": 52.3
    },
    "Map": {
      "GridWidth": 96,
      "GridHeight": 54,
      "Symmetry": "Rotate",     // "Rotate" | "Mirror"
      "ObstacleDensity": 0.03,
      "MaxObstaclesPerHalf": 4,
      "MinFloor": 0.45,
      "MaxFloor": 0.70
    },
    "Paint": {
      "UnpaintedMin": 0.03,     // fraction of total floor, picked per round in [min, max]
      "UnpaintedMax": 0.10,
      "Splatter": true,         // splats in the fight zone + sputters at the unpainted border near spawns
      "DiligentSpawnChance": 0.3, // per team, per round: chance it painted its own spawn area diligently
      "PixelsPerCell": 16,      // mask and render resolution
      "CornerRadius": 0.75,     // outer corners of the outline, in cells, like CSS border-radius (0 = sharp, < 1)
      "InnerCornerRadius": 0.5  // curve filling inner corners, in cells (0 = sharp, < 1)
    }
  },
  "Discord": {
    "ChannelId": 0,
    "VoteCountInterval": "00:00:30"   // at most one voter-list edit per interval
    // The token is never stored in this file. It comes from the environment variable
    // TURFGUESS_Discord__Token, or from `dotnet user-secrets` during development.
  },
  "Database": {
    "Path": "turfguess.db"            // relative paths resolve against the working directory
  },
  "Images": {
    "Directory": "images"
  }
}
```

---

## 8. Tech stack & structure

**C# on .NET 10 (LTS)**, with nullable reference types enabled.

| Concern | Library | Notes |
|---|---|---|
| Discord | **Discord.Net** (`Discord.Net.WebSocket` + `Discord.Net.Interactions`) | Buttons, ephemeral replies, slash commands. NetCord is a possible alternative if Discord.Net turns out to be poorly maintained. |
| Hosting / DI / config / logging | `Microsoft.Extensions.Hosting` | Generic Host. The bot and the scheduler run as `BackgroundService`s. |
| Rendering | **SkiaSharp** (+ `SkiaSharp.NativeAssets.Linux` for Pi/VPS) | MIT license. Anti-aliased shapes, blur for drop shadows, PNG encoding. |
| Noise | Own `GradientNoise` (Perlin-style + fBm, ~100 lines) | Written in-house instead of vendoring FastNoiseLite: no dependency, and the output is fully under our control for determinism. Used for the paint cost field and textures. |
| Randomness | Own `Rng` (SplitMix64) | Seeded generation never depends on `System.Random`'s implementation. |
| Pathfinding | Built-in `PriorityQueue<TElement, TPriority>` | The two-source Dijkstra. Grids are flat arrays (`float[]`, `byte[]`, indexed `y * w + x`). |
| Database | **Microsoft.Data.Sqlite** + **Dapper** | Small hand-written SQL, no EF Core. The schema is created on startup. |
| Tests | xUnit | Determinism (same seed gives the same map), symmetry, connectivity, the winner share counted from the mask matching the target, the unpainted share staying within range. |

**Discord specifics:**
- Gateway intents: `GatewayIntents.Guilds` only. No privileged intents are needed.
- Channel permissions: *View Channel*, *Send Messages*, *Embed Links*, *Attach Files*, *Read Message History* (permission integer `117760`). Invite scopes: `bot` and `applications.commands`.
- Vote buttons have `customId = "vote:{roundId}:{left|right}"` and are handled by an `InteractionService` handler, `[ComponentInteraction("vote:*:*")]`. Discord.Net routes clicks by custom id, not by an in-memory view, so **the buttons keep working after a restart** with no re-registration needed.
- The vote confirmation is sent with `RespondAsync(..., ephemeral: true)`.
- The reveal message is sent with `allowedMentions: AllowedMentions.None`, so names render without pinging anyone.

**Solution layout:**

```
TurfGuess/
  TurfGuess.slnx                 # .NET 10's XML solution format
  src/
    TurfGuess.Core/              # class library: pure, deterministic, NO Discord or DB references
      Grid/                      #   Grid<T>, CellType, symmetry helpers
      MapGen/MapGenerator.cs     #   spine + wings + plaza, holes, obstacles, symmetry, validation
      Paint/PaintSimulator.cs    #   distance fields, unpainted mask, ownership score, quantile threshold
      Render/MapRenderer.cs      #   SkiaSharp: round image + reveal image
      Render/ColorPairs.cs       #   built-in color pairs + per-seed pick
      Scoring/Score.cs           #   coverage computed from the final paint mask
      Noise/GradientNoise.cs     #   seeded Perlin-style noise + fBm
      Util/Rng.cs                #   SplitMix64 PRNG
      RoundGenerator.cs          #   seed + options -> GeneratedRound (map, mask, score)
    TurfGuess.Bot/               # worker app (Generic Host)
      Program.cs                 #   config, validation, DI wiring
      appsettings.json
      Options/BotOptions.cs      #   strongly typed config + startup validation
      Data/Database.cs           #   connections + schema
      Data/RoundRepository.cs    #   SQLite + Dapper: rounds, conditional vote upsert
      Game/RoundSchedule.cs      #   next RoundTime in TimeZone, DST, first-round rule
      Game/RoundService.cs       #   round lifecycle: idempotent "advance one step" + recovery
      Game/RoundScheduler.cs     #   BackgroundService: wakes at reveal times / on admin signal
      Game/RoundImages.cs        #   render + store PNGs, regenerate if missing
      Game/MessageText.cs        #   all player-facing text (Discord-free, unit-tested)
      Game/MessagePreview.cs     #   --preview-messages: print all messages for a sample round
      Game/VoteService.cs        #   votes; closing enforced by the database
      Game/VoteCountService.cs   #   debounced voter list edits
      Game/IRoundChannel.cs      #   what the game needs from the chat platform
      Discord/DiscordBotService.cs   # client login, command registration, interaction routing
      Discord/DiscordRoundChannel.cs # IRoundChannel on Discord: post, close, find, reveal
      Discord/VoteModule.cs      #   button handler
      Discord/AdminModule.cs     #   /turf-admin slash commands
    TurfGuess.Preview/           # console app: writes N sample PNGs + a contact sheet, no Discord
  tests/
    TurfGuess.Core.Tests/
    TurfGuess.Bot.Tests/         # game logic with a fake channel, schedule, repository, text, config
```

The game logic only talks to Discord through `IRoundChannel`, so the round lifecycle, recovery, voting and reveal text are tested with an in-memory fake channel, without a token.

`TurfGuess.Core` must stay **pure and Discord-free**: seed + options in, map/paint/image out. That way it can be iterated on quickly with `dotnet run --project src/TurfGuess.Preview -- --count 12 --seed 42`.

### Slash commands
Admin commands live under their own top-level command, **`/turf-admin`**. Discord sets permissions per top-level command, so this keeps the admin commands limited to administrators (`DefaultMemberPermissions(Administrator)`) while the future player commands under `/turf` stay public. They're registered to the game's server, so they show up instantly.

| Command | Who | Status |
|---|---|---|
| `/turf-admin reveal-now` | Admins | ✅ Reveals the open round now and posts the next one. Handy for testing. |
| `/turf-admin status` | Admins | ✅ Shows the current round, its colors, reveal time and vote count. |
| `/turf stats` | Everyone | Later (milestone 4): your accuracy and streak |
| `/turf leaderboard` | Everyone | Later (milestone 4) |

All replies are ephemeral (only the person who used the command sees them).

---

## 9. Milestones

1. ✅ **Generator prototype:** `TurfGuess.Core` (map generation, paint, rendering) plus the `TurfGuess.Preview` console app, producing a contact sheet of 12 seeds. *This is where we find out whether it's fun.*
2. ✅ **Tune:** map styles, margin ranges, visual polish. The colorblind check is deferred.
3. 🟡 **Bot MVP:** daily post, button voting, the reveal at the next RoundTime with the list of correct guessers, SQLite, restart catch-up. The code and unit tests are done. A live run on a test server is still to do.
4. **Multiple servers:** one shared daily puzzle for every server the bot is in, Wordle style, with setup through `/turf-admin setup` instead of config (section 12).
5. **Engagement:** streaks, leaderboard, reveal GIF, map names.
6. **Hosting:** a small VPS or Raspberry Pi. Publish it as a self-contained app (`dotnet publish -r linux-arm64 --self-contained`) and run it as a systemd service through `Microsoft.Extensions.Hosting.Systemd`. On Windows it can run as a service instead (`Microsoft.Extensions.Hosting.WindowsServices`).

---

## 10. Decisions

| Question | Decision |
|---|---|
| Single server or multiple guilds? | **Now:** single server, one channel set in config. **Planned (milestone 4):** any number of servers, all playing **one shared daily puzzle** on one global schedule, with each server choosing its channel by command. See section 12. |
| Fixed team colors or varying? | **A different color pair each round**, picked from the seed. Team names are the color names. |
| Name the correct guessers at reveal? | **Yes**, listed by name with **no pings** (`AllowedMentions.None`). |
| Language? | **C# / .NET 10**, because it's the language the author knows best. Python offered nothing this project needs. |
| Fixed or adaptive margin range? | **Fixed**, taken from config. |
| What does "winner share" measure? | **Share of painted floor** (`max(L,R)/(L+R)`) for difficulty. The reveal shows **share of total floor** per team plus the unpainted share. |
| How long is a round? | Until the **next RoundTime in TimeZone**. Catch-up and daylight-saving rounds are shorter or longer, so the schedule never drifts. |
| Binary guess or margin brackets? | **Pure binary.** Players vote by clicking one of two buttons on the round message. |
| Reactions or buttons? | **Buttons.** Votes stay hidden (with an ephemeral confirmation), are easy to change, and need no *Manage Messages* permission. |

## 11. Open questions

- None at the moment. The welcome message and the failure threshold for multiple servers are decided in section 12.2.

---

## 12. Multiple servers: one shared daily puzzle (planned, milestone 4)

> **Status:** designed, not built. The bot currently runs in one server, with the channel set in config (sections 2, 6 and 7). Once this is built, the sections it changes get folded back into the main text.

### 12.1 Overview

The bot can be invited to any number of servers. **Every server plays the same puzzle on the same day**, Wordle style:

- Once a day, at one global release time, a new **puzzle** is generated: one map, one color pair, one answer. It's numbered: *Judd's Judgment #42*.
- Every server that has set the game up gets that puzzle posted in its channel. All servers reveal it at the same moment, then the next puzzle goes out.
- Each server sees its own votes and its own correct guessers. The reveal also shows how players did across all servers.

What a server admin does: invite the bot, run `/turf-admin setup` once, pick a channel. They never deal with tokens, config files or IDs.

### 12.2 Decisions

| Question | Decision | Why |
|---|---|---|
| Shared map or one per server? | **One shared puzzle per day.** | Same puzzle for everyone makes it a shared daily ritual, allows cross-server results, and the map is only generated and rendered once a day. |
| Can servers choose their own round time? | **No. One global schedule** (`Game:RoundTime` in `Game:TimeZone`, set by the operator). | With per-server times, a server revealing at 09:00 would spoil the answer for a server still voting until 18:00. One schedule makes spoilers impossible and keeps cross-server results meaningful. |
| What can a server configure? | **The channel**, and whether the game is enabled. | Everything else is part of the shared puzzle. |
| Votes per server or per player? | **Per server.** A vote belongs to (puzzle, server, player). | A server only sees its own players' votes. Telling which servers a player belongs to would need the privileged *Server Members* intent, which we avoid. Someone who plays in two servers votes in each. |
| A server sets up mid-puzzle? | **It gets the current puzzle straight away**, with the same global reveal time. | Like opening Wordle late in the day: you still play today's puzzle. |
| Welcome when the bot joins? | **Yes, a one-time rundown, sent as a DM to whoever invited the bot.** If that fails, it goes to the server owner, then to the system channel. | The person who added the bot is the one who wants to know how to start, without the bot posting in a public channel before it's set up. See 12.9. |
| When does a broken server stop getting posts? | **After 3 consecutive failed days** (`Discord:MaxFailedDays`). | Long enough to ride out a temporary permission mix-up, short enough to stop retrying a deleted channel. |
| Old single-server setup? | **Keeps working.** If `Discord:ChannelId` is set, that server is set up automatically on startup. | No breaking change for the existing install. |

### 12.3 Puzzle numbering and seeds

- Puzzles are numbered from 1 and stored in a `puzzles` table. The number shows in titles: *Judd's Judgment #42*. It replaces today's round number.
- Each puzzle's seed is **random** and stored when the puzzle is created, rather than derived from the date. That way nobody can work out tomorrow's answer from the code. Cheating is out of scope, but there's no reason to make it trivial.
- The puzzle's images are rendered once and reused for every server.

### 12.4 The two lifecycles

There are now two layers, each following the same idempotent "advance one step" pattern as today (section 2, Round lifecycle):

**Puzzle (global):** `Open → Closed`

| Step | When |
|---|---|
| Create (seed, generate, render, store) | Whenever there's no open puzzle |
| `Open → Closed` | At its `reveal_at`. **This closes voting in every server at once.** |

**Server post (one per server per puzzle):** `Pending → Open → Revealed → Done`. It's today's round lifecycle, keyed by (puzzle, server):

| Status | Meaning |
|---|---|
| `Pending` | Row written, message maybe not posted yet |
| `Open` | Puzzle message posted in that server's channel |
| `Revealed` | The puzzle is closed. That server's results are final, and its reveal may not be posted yet. |
| `Done` | That server's reveal is posted |

**At the round time:**
1. Close the current puzzle (one database update closes voting everywhere).
2. Move every server's post forward: disable the buttons and post that server's reveal.
3. Create the next puzzle.
4. Post it in every enabled server.

Steps 2 and 4 go through servers one at a time, within Discord's rate limits. The order between servers doesn't matter, because voting already closed for everyone in step 1.

Recovery works exactly as today, per server: before posting, the bot looks in that server's channel for a message it already posted (by button id or reveal footer).

**One broken server never blocks the others.** If posting to a server fails (channel deleted, permissions removed), the error is logged, that server's `failure_count` goes up, and the other servers carry on. After `Discord:MaxFailedDays` (default 3) consecutive failed days, the server is marked `needs_setup` and skipped until an admin runs `/turf-admin setup` again.

### 12.5 Voting

- Button ids become `vote:{puzzleId}:{side}`. The server comes from the interaction itself.
- The conditional upsert (section 2, Voting rules) checks that the **puzzle** is `Open` and that the server's post is `Open`. So one statement still closes voting everywhere at the reveal.

### 12.6 The reveal

Per server, as today: winner, coverage, unpainted share, that server's vote counts and correct guessers (named without pings). One new line:

> **Across all servers:** 63% guessed right (412 players in 14 servers).

This line counts every vote for the puzzle, so a player who voted in two servers counts twice. That's acceptable for a fun stat.

### 12.7 Data model changes

```sql
guilds (
  guild_id        INTEGER PRIMARY KEY,
  channel_id      INTEGER,            -- NULL until set up
  enabled         INTEGER NOT NULL,   -- 0/1; 0 after /turf-admin disable or when the bot is removed
  needs_setup     INTEGER NOT NULL,   -- 1 after repeated posting failures
  failure_count   INTEGER NOT NULL,
  joined_at       INTEGER NOT NULL,
  configured_at   INTEGER,
  welcomed_at     INTEGER             -- set once the one-time welcome went out (or was attempted)
)

puzzles (                             -- today's `rounds`, minus the Discord-specific columns
  id                INTEGER PRIMARY KEY AUTOINCREMENT,   -- the puzzle number
  status            TEXT NOT NULL,    -- 'Open' | 'Closed'
  seed, generator_version, image_path, reveal_image_path,
  created_at, reveal_at,
  left_name, left_color, left_emoji, right_name, right_color, right_emoji,
  winner, winner_share, share_left, share_right, unpainted
)

guild_rounds (                        -- one post of a puzzle in one server
  puzzle_id         INTEGER NOT NULL REFERENCES puzzles (id),
  guild_id          INTEGER NOT NULL REFERENCES guilds (guild_id),
  channel_id        INTEGER NOT NULL, -- copied at posting time, so later channel changes don't break recovery
  status            TEXT NOT NULL,    -- 'Pending' | 'Open' | 'Revealed' | 'Done'
  message_id        INTEGER,
  reveal_message_id INTEGER,
  posted_at         INTEGER,
  PRIMARY KEY (puzzle_id, guild_id)
)

votes (
  puzzle_id, guild_id, user_id, choice, voted_at,
  PRIMARY KEY (puzzle_id, guild_id, user_id)
)
```

- **Invariants:** at most one `Open` puzzle (a partial unique index, as today), and at most one unfinished `guild_rounds` row per server.
- **Schema versions:** a `schema_version` table. On startup, migration v1 → v2 turns existing `rounds` into `puzzles` plus `guild_rounds` rows for the configured server, and keeps their votes. The existing install upgrades in place.

### 12.8 Commands

| Command | Who | What |
|---|---|---|
| `/turf-admin setup channel:#…` | Server admins | Sets the game channel and enables the game. Checks that the bot can view, send, embed and attach there, and says what's missing if not. Posts the current puzzle straight away if one is open. Running it again changes the channel; the current puzzle moves to the new channel at the next post. |
| `/turf-admin disable` | Server admins | Stops posting in this server. Existing messages stay. |
| `/turf-admin status` | Server admins | This server's channel and status, the current puzzle number and reveal time, and this server's vote count. |
| `/turf-owner reveal-now`, `/turf-owner status` | Bot operator only | `reveal-now` now affects **every** server, so it moves to owner-only commands. They're registered only in the operator's home server (`Discord:HomeGuildId`) and also check `Discord:OwnerIds`. |

Admin commands are registered **globally**, so they work in every server. Owner commands stay guild-registered in the home server only.

### 12.9 Joining and leaving servers

- **Bot added** (`JoinedGuild`): insert a `guilds` row (not set up) and send a **one-time welcome** with a quick rundown (below). It's sent once per server: `welcomed_at` is stored, so re-invites and restarts don't repeat it.
- **Bot removed** (`LeftGuild`): mark the server `enabled = 0`. History stays, so re-inviting the bot picks up where it left off after setup.
- **Channel deleted or permissions revoked:** handled by the failure counting in 12.4.

#### Who gets the welcome

Discord doesn't tell a bot who invited it, so the bot works down this list and stops at the first one that succeeds:

1. **A DM to the person who invited the bot.** Found in the server's audit log: the newest *Bot Add* entry whose target is this bot names the user who added it. This needs the **View Audit Log** permission, which the invite link asks for. If an admin unticks it, this step is skipped.
2. **A DM to the server owner.** The owner is always known, so this works even without audit log access.
3. **A post in the server's system channel** (where Discord posts "X joined"), when both DMs fail, for example because the person doesn't accept DMs from server members.
4. **Nothing.** The failure is logged, and an admin can still discover the game through the `/turf-admin` commands.

#### The welcome message (draft)

> **Thanks for adding Judd's Judgment to {server}!** 🎨
>
> Every day, every server playing gets the same puzzle: an arena where two teams of paint fought over the floor. Players guess which color painted more by clicking a button. The next day the answer is revealed, along with who in your server got it right and how everyone did across all servers.
>
> **To start:** an admin runs `/turf-admin setup channel:#your-channel` in {server}. The first puzzle is posted there right away.
>
> - A new puzzle comes out daily at <t:{nextRoundTime}:t> (your local time).
> - The bot needs to view, send messages, embed links and attach files in that channel. `/turf-admin setup` checks this for you.
> - `/turf-admin status` shows the current puzzle, and `/turf-admin disable` stops the game.

- `{server}` is the server's name. `<t:…:t>` shows the daily release time in each reader's own time zone.
- The DM version and the system-channel version use the same text. In the channel version, "Thanks for adding" becomes "Thanks for having me in".

### 12.10 Configuration changes

| Setting | Change |
|---|---|
| `Discord:ChannelId` | Becomes optional: if set, that channel's server is set up automatically (the single-server shortcut) |
| `Discord:HomeGuildId` | New: where owner commands are registered |
| `Discord:OwnerIds` | New: user ids allowed to use owner commands |
| `Game:RoundTime`, `Game:TimeZone` | Unchanged, now explicitly global |
| `Discord:MaxFailedDays` | New, default `3`: consecutive failed days before a server is marked `needs_setup` |
| `Discord:SendWelcome` | New, default `true`: the one-time welcome when the bot joins a server |

**Invite link:** the permissions grow from `117760` to `117888`, adding *View Audit Log* (`128`) so the welcome can reach the person who invited the bot. The rest is unchanged: View Channel, Send Messages, Embed Links, Attach Files, Read Message History. Scopes are still `bot` and `applications.commands`.

### 12.11 Scale notes

- **Verification:** Discord requires a bot to be verified before it can join more than about 100 servers.
- **Sharding:** past about 2,500 servers, swap `DiscordSocketClient` for `DiscordShardedClient`. The game's load is tiny (two posts per server per day), so this is the only scaling step expected.
- **Posting time:** about two REST calls per server at the round time. That's seconds for dozens of servers and a few minutes for thousands. Spoilers aren't a risk, because voting closes for everyone before any reveal is posted.
- **Intents:** still only `Guilds`. No privileged intents. Sending DMs and reading the audit log need no extra intents, only the View Audit Log permission for the latter.

### 12.12 Tests to add

- Two servers share one puzzle and one seed. Each gets its own post, votes and correct guessers.
- Closing the puzzle rejects votes in every server.
- A server that fails to post doesn't block the others. Three failed days mark it `needs_setup`.
- A server that sets up mid-puzzle gets the current puzzle with the global reveal time.
- Crash recovery per server: no duplicate posts in any server.
- The cross-server stat counts all servers.
- Migration v1 → v2 keeps existing rounds and votes.
- The `Discord:ChannelId` shortcut still sets up that server.
- The welcome goes to the inviter from the audit log, falls back to the owner, then to the system channel, and is sent only once per server (with a fake that refuses DMs).
