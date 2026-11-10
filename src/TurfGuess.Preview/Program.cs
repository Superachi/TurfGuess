using System.Diagnostics;
using System.Globalization;
using SkiaSharp;
using TurfGuess.Core;
using TurfGuess.Core.Grid;
using TurfGuess.Core.Render;

// Usage: dotnet run --project src/TurfGuess.Preview -- [--count 12] [--seed 42] [--out preview]
//        [--ppc 16] [--symmetry Rotate|Mirror] [--no-reveal] [--columns 4]

var args_ = ParseArgs(args);
int count = args_.Int("count", 12);
ulong baseSeed = (ulong)args_.Long("seed", 42);
string outDir = Path.GetFullPath(args_.Str("out", "preview"));
int columns = args_.Int("columns", 4);
bool reveal = !args_.Flag("no-reveal");

var options = new GeneratorOptions();
options.Paint.PixelsPerCell = args_.Int("ppc", options.Paint.PixelsPerCell);
if (args_.Has("symmetry")) options.Map.Symmetry = Enum.Parse<SymmetryMode>(args_.Str("symmetry", "Rotate"), ignoreCase: true);
var style = new RenderStyle();

Directory.CreateDirectory(outDir);
Console.WriteLine($"Writing {count} rounds to {outDir}");
Console.WriteLine($"{"seed",6} {"try",3} {"winner",-7} {"target",7} {"actual",7} {"L cov",6} {"R cov",6} {"unp",6} {"ms",6}");

var thumbs = new List<(SKBitmap Image, string Label)>();
for (int i = 0; i < count; i++)
{
    ulong seed = baseSeed + (ulong)i;
    var sw = Stopwatch.StartNew();
    var round = RoundGenerator.Generate(seed, options);
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
        $"{seed,6} {round.Attempts,3} {winnerName,-7} {round.TargetWinnerShare,7:P2} {s.WinnerShare,7:P2} {s.CoverageLeft,6:P1} {s.CoverageRight,6:P1} {s.UnpaintedShare,6:P1} {genMs,6}"));

    thumbs.Add((MapRenderer.RenderRound(round, style), $"#{seed}  {winnerName} {s.WinnerShare:P1}"));
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
}
