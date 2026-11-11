using System.Diagnostics;
using System.Globalization;
using SkiaSharp;
using TurfGuess.Core;
using TurfGuess.Core.Grid;
using TurfGuess.Core.Render;
using TurfGuess.Core.Scoring;

// Usage: dotnet run --project src/TurfGuess.Preview -- [--count 12] [--seed 42] [--out preview] [--ink Flat|Shaded|Marbled]
//        [--ppc 16] [--symmetry Rotate|Mirror] [--no-reveal] [--columns 4] [--radius 0.75] [--inner-radius 0.5]
//        dotnet run --project src/TurfGuess.Preview -- --pairs [--seed 42]
//          [--pairs-file pairs.json] [--pairs-out name.png] [--pairs-full]
//          renders one map in every color pair -> preview/color_pairs.png

var args_ = ParseArgs(args);
int count = args_.Int("count", 12);
ulong baseSeed = (ulong)args_.Long("seed", 42);
string outDir = Path.GetFullPath(args_.Str("out", "preview"));
int columns = args_.Int("columns", 4);
bool reveal = !args_.Flag("no-reveal");

var options = new GeneratorOptions();
options.Paint.PixelsPerCell = args_.Int("ppc", options.Paint.PixelsPerCell);
options.Paint.CornerRadius = args_.Double("radius", options.Paint.CornerRadius);
options.Paint.InnerCornerRadius = args_.Double("inner-radius", options.Paint.InnerCornerRadius);
var inkStyle = Enum.Parse<InkStyle>(args_.Str("ink", new RenderStyle().Ink.ToString()), ignoreCase: true);
if (args_.Has("symmetry")) options.Map.Symmetry = Enum.Parse<SymmetryMode>(args_.Str("symmetry", "Rotate"), ignoreCase: true);

if (args_.Flag("pairs"))
{
    Directory.CreateDirectory(outDir);
    var pairRound = RoundGenerator.Generate(baseSeed, options);
    var pairs = args_.Has("pairs-file")
        ? System.Text.Json.JsonSerializer.Deserialize<List<ColorPair>>(File.ReadAllText(args_.Str("pairs-file", "")))!
        : ColorPairs.Default.ToList();
    var pairThumbs = new List<(SKBitmap Image, string Label)>();
    for (int i = 0; i < pairs.Count; i++)
    {
        var pair = pairs[i];
        var pairStyle = new RenderStyle { Left = pair.A, Right = pair.B, Ink = inkStyle };
        pairThumbs.Add((MapRenderer.RenderRound(pairRound, pairStyle),
            $"{i + 1}. {pair.A.Name} {pair.A.Color}  vs  {pair.B.Name} {pair.B.Color}"));
        if (args_.Flag("pairs-full"))
        {
            using var full = MapRenderer.RenderRound(pairRound, pairStyle, $"{pair.A.Name} vs {pair.B.Name}");
            string slug = $"{i + 1:00}_{pair.A.Name}_{pair.B.Name}".ToLowerInvariant().Replace(' ', '-');
            File.WriteAllBytes(Path.Combine(outDir, $"pair_{slug}.png"), MapRenderer.EncodePng(full));
        }
    }
    string pairsPath = Path.Combine(outDir, args_.Str("pairs-out", "color_pairs.png"));
    WriteContactSheet(pairThumbs, args_.Int("columns", 3), pairsPath);
    foreach (var t in pairThumbs) t.Image.Dispose();
    Console.WriteLine($"Color pairs sheet ({pairs.Count} pairs, seed {baseSeed}): {pairsPath}");
    return;
}

Directory.CreateDirectory(outDir);
Console.WriteLine($"Writing {count} rounds to {outDir}");
Console.WriteLine($"{"seed",6} {"try",3} {"winner",-10} {"target",7} {"actual",7} {"L cov",6} {"R cov",6} {"unp",6} {"dil",3} {"rank",4} {"ms",6}");

var thumbs = new List<(SKBitmap Image, string Label)>();
for (int i = 0; i < count; i++)
{
    ulong seed = baseSeed + (ulong)i;
    var sw = Stopwatch.StartNew();
    var round = RoundGenerator.Generate(seed, options);
    var style = ColorPairs.Pick(seed);
    style.Ink = inkStyle;
    long genMs = sw.ElapsedMilliseconds;

    using var image = MapRenderer.RenderRound(round, style, $"{style.GameName} · Seed {seed}");
    File.WriteAllBytes(Path.Combine(outDir, $"round_{seed}.png"), MapRenderer.EncodePng(image));

    if (reveal)
    {
        using var revealImage = MapRenderer.RenderReveal(round, style, $"{style.GameName} · Seed {seed}");
        File.WriteAllBytes(Path.Combine(outDir, $"reveal_{seed}.png"), MapRenderer.EncodePng(revealImage));
    }

    var s = round.Score;
    var winnerName = s.Winner == Side.Left ? style.Left.Name : style.Right.Name;
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{seed,6} {round.Attempts,3} {winnerName,-10} {round.TargetWinnerShare,7:P2} {s.WinnerShare,7:P2} {s.CoverageLeft,6:P1} {s.CoverageRight,6:P1} {s.UnpaintedShare,6:P1} {Diligent(round),3} {s.Rank(),4} {genMs,6}"));

    thumbs.Add((MapRenderer.RenderRound(round, style), $"#{seed}  {style.Left.Name} vs {style.Right.Name}: {winnerName} {s.WinnerShare:P1}, rank {s.Rank()}"));
}

WriteContactSheet(thumbs, columns, Path.Combine(outDir, "contact_sheet.png"));
foreach (var t in thumbs) t.Image.Dispose();
Console.WriteLine($"Contact sheet: {Path.Combine(outDir, "contact_sheet.png")}");

static void WriteContactSheet(List<(SKBitmap Image, string Label)> items, int columns, string path)
{
    if (items.Count == 0) return;
    const int thumbW = 480;
    int thumbH = (int)(items[0].Image.Height * (thumbW / (double)items[0].Image.Width));
    const int labelH = 30, gap = 10;
    int rows = (items.Count + columns - 1) / columns;
    int cols = Math.Min(columns, items.Count);

    var info = new SKImageInfo(cols * (thumbW + gap) + gap, rows * (thumbH + labelH + gap) + gap, SKColorType.Rgba8888, SKAlphaType.Opaque);
    using var sheet = new SKBitmap(info);
    using var canvas = new SKCanvas(sheet);
    canvas.Clear(new SKColor(12, 13, 18));

    using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default, 18);
    using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
    var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

    for (int i = 0; i < items.Count; i++)
    {
        int col = i % columns, row = i / columns;
        float x = gap + col * (thumbW + gap);
        float y = gap + row * (thumbH + labelH + gap);
        using var img = SKImage.FromBitmap(items[i].Image);
        canvas.DrawImage(img, new SKRect(x, y, x + thumbW, y + thumbH), sampling);
        canvas.DrawText(items[i].Label, x, y + thumbH + 22, SKTextAlign.Left, font, textPaint);
    }

    using var image = SKImage.FromBitmap(sheet);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
}

// Which teams painted their own spawn diligently this round: L, R, LR or -.
static string Diligent(GeneratedRound r)
{
    string s = (r.Paint.DiligentLeft ? "L" : "") + (r.Paint.DiligentRight ? "R" : "");
    return s.Length > 0 ? s : "-";
}

static Args ParseArgs(string[] argv)
{
    var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < argv.Length; i++)
    {
        if (!argv[i].StartsWith("--")) continue;
        string key = argv[i][2..];
        string? value = i + 1 < argv.Length && !argv[i + 1].StartsWith("--") ? argv[++i] : null;
        dict[key] = value;
    }
    return new Args(dict);
}

internal sealed record Args(Dictionary<string, string?> Values)
{
    public bool Has(string key) => Values.ContainsKey(key);
    public bool Flag(string key) => Values.ContainsKey(key);
    public string Str(string key, string fallback) => Values.TryGetValue(key, out var v) && v is not null ? v : fallback;
    public int Int(string key, int fallback) => Values.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;
    public long Long(string key, long fallback) => Values.TryGetValue(key, out var v) && long.TryParse(v, out var n) ? n : fallback;
    public double Double(string key, double fallback) => Values.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : fallback;
}
