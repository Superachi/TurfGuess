using System.Runtime.InteropServices;
using SkiaSharp;
using TurfGuess.Core.Grid;
using TurfGuess.Core.Noise;
using TurfGuess.Core.Paint;
using TurfGuess.Core.Util;

namespace TurfGuess.Core.Render;

/// <summary>
/// Draws the final paint mask (DESIGN.md §5). The renderer never counts anything:
/// shading, shadows and outlines only change how the mask looks.
/// </summary>
public static class MapRenderer
{
    private static readonly Rgb VoidColor = new(22, 24, 32);
    private static readonly Rgb FloorColor = new(148, 151, 160);
    private static readonly Rgb WallTop = new(196, 200, 210);
    private static readonly Rgb WallLight = new(232, 235, 242);
    private static readonly Rgb WallSide = new(220, 224, 232);
    private static readonly Rgb WallDark = new(140, 144, 156);
    private static readonly Rgb WallDarkSide = new(160, 164, 175);
    private static readonly SKColor BannerColor = new(18, 20, 28);

    /// <summary>The image posted with a new round.</summary>
    public static SKBitmap RenderRound(GeneratedRound round, RenderStyle style, string? caption = null)
    {
        var bitmap = RenderMap(round, style, extraHeight: 0);
        using var canvas = new SKCanvas(bitmap);
        if (caption is not null) DrawCaption(canvas, caption, round.Paint.PixelsPerCell);
        return bitmap;
    }

    /// <summary>The reveal image: the same map with a result banner underneath.</summary>
    public static SKBitmap RenderReveal(GeneratedRound round, RenderStyle style, string? caption = null)
    {
        int p = round.Paint.PixelsPerCell;
        int bannerHeight = (int)(round.Paint.Height * 0.18);
        var bitmap = RenderMap(round, style, bannerHeight);
        using var canvas = new SKCanvas(bitmap);
        if (caption is not null) DrawCaption(canvas, caption, p);
        DrawBanner(canvas, round, style, new SKRect(0, round.Paint.Height, round.Paint.Width, round.Paint.Height + bannerHeight));
        return bitmap;
    }

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // ---------------------------------------------------------------------------------------

    private static SKBitmap RenderMap(GeneratedRound round, RenderStyle style, int extraHeight)
    {
        var paint = round.Paint;
        var cells = round.Map.Cells;
        int w = paint.Width, h = paint.Height, p = paint.PixelsPerCell;
        var mask = paint.Mask;

        var left = Rgb.Parse(style.Left.Color);
        var right = Rgb.Parse(style.Right.Color);
        var texture = new GradientNoise(Rng.Derive(round.Seed, 77));

        int shadowDx = Math.Max(2, p / 4), shadowDy = Math.Max(2, p / 3);
        int bevel = Math.Max(1, p / 8);

        var pixels = new int[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var kind = mask[i];
                bool gridLine = x % p == 0 || y % p == 0;
                Rgb c;

                switch (kind)
                {
                    case PixelKind.Void:
                        c = VoidColor.Add(5 * texture.Sample(x * 0.05, y * 0.05));
                        break;

                    case PixelKind.Unpainted:
                        c = gridLine ? FloorColor.Add(-14) : FloorColor;
                        break;

                    case PixelKind.Left:
                    case PixelKind.Right:
                    {
                        var team = kind == PixelKind.Left ? left : right;
                        c = team.Scale(1 + 0.07 * texture.Fbm(x / (p * 3.0), y / (p * 3.0), 2));
                        if (IsPaintEdge(mask, w, h, x, y, kind)) c = c.Scale(0.80);
                        else if (gridLine) c = c.Scale(0.95);
                        break;
                    }

                    case PixelKind.SpawnLeft:
                    case PixelKind.SpawnRight:
                    {
                        var team = kind == PixelKind.SpawnLeft ? left : right;
                        bool stripe = ((x + y) / Math.Max(2, p / 2)) % 2 == 0;
                        c = team.Scale(stripe ? 0.55 : 0.42);
                        break;
                    }

                    default: // Wall
                    {
                        int cx = x / p, cy = y / p, lx = x % p, ly = y % p;
                        c = WallTop;
                        if (ly < bevel && !IsWall(cells, cx, cy - 1)) c = WallLight;
                        else if (lx < bevel && !IsWall(cells, cx - 1, cy)) c = WallSide;
                        else if (ly >= p - bevel && !IsWall(cells, cx, cy + 1)) c = WallDark;
                        else if (lx >= p - bevel && !IsWall(cells, cx + 1, cy)) c = WallDarkSide;
                        break;
                    }
                }

                // Drop shadow cast by walls (light from top-left).
                if (kind != PixelKind.Wall)
                {
                    int sx = x - shadowDx, sy = y - shadowDy;
                    if (sx >= 0 && sy >= 0 && mask[sy * w + sx] == PixelKind.Wall) c = c.Scale(0.62);
                }

                pixels[i] = c.ToRgba();
            }
        }

        var bitmap = new SKBitmap(new SKImageInfo(w, h + extraHeight, SKColorType.Rgba8888, SKAlphaType.Opaque));
        IntPtr dst = bitmap.GetPixels();
        int rowBytes = bitmap.RowBytes;
        for (int y = 0; y < h; y++)
            Marshal.Copy(pixels, y * w, dst + y * rowBytes, w);

        if (extraHeight > 0)
        {
            using var canvas = new SKCanvas(bitmap);
            using var bg = new SKPaint { Color = BannerColor };
            canvas.DrawRect(new SKRect(0, h, w, h + extraHeight), bg);
        }

        return bitmap;
    }

    private static bool IsWall(Grid<CellType> cells, int x, int y) => cells.InBounds(x, y) && cells[x, y] == CellType.Wall;

    /// <summary>True when a 4-neighbour is floor of a different kind (other team or unpainted).</summary>
    private static bool IsPaintEdge(PixelKind[] mask, int w, int h, int x, int y, PixelKind kind)
    {
        return Differs(x + 1, y) || Differs(x - 1, y) || Differs(x, y + 1) || Differs(x, y - 1);

        bool Differs(int nx, int ny)
        {
            if (nx < 0 || ny < 0 || nx >= w || ny >= h) return false;
            var k = mask[ny * w + nx];
            return k != kind && k is PixelKind.Left or PixelKind.Right or PixelKind.Unpainted;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Text and banner
    // ---------------------------------------------------------------------------------------

    private static SKTypeface Bold() => SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;

    private static void DrawCaption(SKCanvas canvas, string caption, int p)
    {
        using var font = new SKFont(Bold(), p * 1.4f);
        float textWidth = font.MeasureText(caption);
        float pad = p * 0.6f;
        var box = new SKRect(p * 0.8f, p * 0.8f, p * 0.8f + textWidth + 2 * pad, p * 0.8f + font.Size + 2 * pad);
        using var bg = new SKPaint { Color = new SKColor(0, 0, 0, 150), IsAntialias = true };
        canvas.DrawRoundRect(box, pad, pad, bg);
        using var fg = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(caption, box.Left + pad, box.Bottom - pad - font.Size * 0.12f, SKTextAlign.Left, font, fg);
    }

    private static void DrawBanner(SKCanvas canvas, GeneratedRound round, RenderStyle style, SKRect area)
    {
        var score = round.Score;
        var leftColor = Rgb.Parse(style.Left.Color).ToSk();
        var rightColor = Rgb.Parse(style.Right.Color).ToSk();
        var winnerStyle = score.Winner == Side.Left ? style.Left : style.Right;
        var winnerColor = score.Winner == Side.Left ? leftColor : rightColor;

        float margin = area.Width * 0.03f;
        float unit = area.Height / 6f;

        using var titleFont = new SKFont(Bold(), unit * 1.25f);
        using var labelFont = new SKFont(Bold(), unit * 0.95f);
        using var titlePaint = new SKPaint { Color = winnerColor, IsAntialias = true };
        canvas.DrawText($"{winnerStyle.Name.ToUpperInvariant()} WINS", area.MidX, area.Top + unit * 1.55f, SKTextAlign.Center, titleFont, titlePaint);

        // Coverage bar: left | unpainted | right, proportional to total floor.
        var bar = new SKRect(area.Left + margin, area.Top + unit * 2.2f, area.Right - margin, area.Top + unit * 3.4f);
        float lw = bar.Width * (float)score.CoverageLeft;
        float rw = bar.Width * (float)score.CoverageRight;
        using (var pl = new SKPaint { Color = leftColor })
            canvas.DrawRect(new SKRect(bar.Left, bar.Top, bar.Left + lw, bar.Bottom), pl);
        using (var pu = new SKPaint { Color = FloorColor.ToSk() })
            canvas.DrawRect(new SKRect(bar.Left + lw, bar.Top, bar.Right - rw, bar.Bottom), pu);
        using (var pr = new SKPaint { Color = rightColor })
            canvas.DrawRect(new SKRect(bar.Right - rw, bar.Top, bar.Right, bar.Bottom), pr);

        float labelY = area.Top + unit * 4.8f;
        using var lp = new SKPaint { Color = leftColor, IsAntialias = true };
        using var rp = new SKPaint { Color = rightColor, IsAntialias = true };
        using var up = new SKPaint { Color = new SKColor(170, 174, 184), IsAntialias = true };
        canvas.DrawText($"{style.Left.Name} {score.CoverageLeft:P1}", bar.Left, labelY, SKTextAlign.Left, labelFont, lp);
        canvas.DrawText($"{score.UnpaintedShare:P1} unpainted", bar.MidX, labelY, SKTextAlign.Center, labelFont, up);
        canvas.DrawText($"{style.Right.Name} {score.CoverageRight:P1}", bar.Right, labelY, SKTextAlign.Right, labelFont, rp);
    }

    // ---------------------------------------------------------------------------------------

    private readonly record struct Rgb(double R, double G, double B)
    {
        public static Rgb Parse(string hex)
        {
            var c = SKColor.Parse(hex);
            return new Rgb(c.Red, c.Green, c.Blue);
        }

        public Rgb Scale(double f) => new(R * f, G * f, B * f);
        public Rgb Add(double d) => new(R + d, G + d, B + d);

        public int ToRgba()
        {
            int r = Clamp(R), g = Clamp(G), b = Clamp(B);
            return r | (g << 8) | (b << 16) | unchecked((int)0xFF000000);
        }

        public SKColor ToSk() => new((byte)Clamp(R), (byte)Clamp(G), (byte)Clamp(B));

        private static int Clamp(double v) => (int)Math.Clamp(Math.Round(v), 0, 255);
    }
}
