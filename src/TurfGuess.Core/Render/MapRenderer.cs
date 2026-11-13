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
    private static readonly Rgb VoidColor = new(40, 42, 48);
    private static readonly Rgb FloorColor = new(148, 151, 160);
    private static readonly Rgb WallTop = new(196, 200, 210);
    private static readonly Rgb WallLight = new(232, 235, 242);
    private static readonly Rgb WallSide = new(220, 224, 232);
    private static readonly Rgb WallDark = new(140, 144, 156);
    private static readonly Rgb WallDarkSide = new(160, 164, 175);
    private const double FloorCheckerShade = -9;    // darker checker squares on unpainted floor (negative = darker)
    private const double WallHatchShade = 0.86;     // darkness of the alternate hatch stripe on wall tops
    private static readonly SKColor BannerColor = new(28, 29, 34);

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
        bool marbled = style.Ink == InkStyle.Marbled;
        var leftInk = marbled ? Ink.Marble(left) : new Ink(left, ShadeHueShift, ShadeLightness);
        var rightInk = marbled ? Ink.Marble(right) : new Ink(right, ShadeHueShift, ShadeLightness);

        int shadowDx = Math.Max(2, p / 4), shadowDy = Math.Max(2, p / 3);
        int bevel = Math.Max(1, p / 8);

        var (tone, gloss) = style.Ink switch
        {
            InkStyle.Shaded => InkLighting(mask, w, h, p, round.Seed),
            InkStyle.Marbled => InkMarble(mask, w, h, p, round.Seed),
            _ => (new float[w * h], new float[w * h]),
        };
        double edgeShadow = style.Ink switch
        {
            InkStyle.Shaded => ShadeEdgeShadow,
            InkStyle.Marbled => MarbleEdgeShadow,
            _ => 0,
        };

        var pixels = new int[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var kind = mask[i];
                Rgb c;

                switch (kind)
                {
                    case PixelKind.Void:
                        c = VoidColor;
                        break;

                    case PixelKind.Unpainted:
                        // Checkerboard on the bare floor only (squares are half a cell); painted areas stay smooth.
                        int half = Math.Max(1, p / 2);
                        c = ((x / half) + (y / half)) % 2 == 0 ? FloorColor : FloorColor.Add(FloorCheckerShade);
                        break;

                    case PixelKind.Left:
                    case PixelKind.Right:
                    {
                        var ink = kind == PixelKind.Left ? leftInk : rightInk;
                        double t = tone[i];
                        // Borders sit in cool shadow, which outlines each color.
                        if (edgeShadow > 0 && IsPaintEdge(mask, w, h, x, y, kind)) t = Math.Min(t, 0) - edgeShadow;
                        c = ink.Shade(t, gloss[i]);
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
                        else
                        {
                            // Diagonal hatching on the top face, like the spawn zones.
                            bool wallStripe = ((x + y) / Math.Max(2, p / 2)) % 2 == 0;
                            if (!wallStripe) c = WallTop.Scale(WallHatchShade);
                        }
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

        if (style.Outline != ArenaOutline.None)
            ApplyOutline(pixels, mask, w, h, Math.Max(2, p / 4), style.Outline);

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

    /// <summary>
    /// Outlines the arena in the unpainted floor color.
    /// Outside: void pixels within <paramref name="width"/> of the arena are filled.
    /// Inside: arena pixels within <paramref name="width"/> of the void are overwritten.
    /// </summary>
    private static void ApplyOutline(int[] pixels, PixelKind[] mask, int w, int h, int width, ArenaOutline mode)
    {
        int r2 = width * width;
        int outline = FloorColor.ToRgba();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool isVoid = mask[i] == PixelKind.Void;
                if (mode == ArenaOutline.Outside && !isVoid) continue;
                if (mode == ArenaOutline.Inside && isVoid) continue;

                // Outside looks for an arena pixel nearby, inside for a void pixel (or the image edge).
                bool near = false;
                for (int dy = -width; dy <= width && !near; dy++)
                {
                    for (int dx = -width; dx <= width; dx++)
                    {
                        if (dx * dx + dy * dy > r2) continue;
                        int nx = x + dx, ny = y + dy;
                        bool nVoid = nx < 0 || ny < 0 || nx >= w || ny >= h || mask[ny * w + nx] == PixelKind.Void;
                        if (mode == ArenaOutline.Outside ? !nVoid : nVoid) { near = true; break; }
                    }
                }
                if (near) pixels[i] = outline;
            }
        }
    }

    private static bool IsWall(Grid<CellType> cells, int x, int y) => cells.InBounds(x, y) && cells[x, y] == CellType.Wall;

    // ---------------------------------------------------------------------------------------
    // Ink shading
    // ---------------------------------------------------------------------------------------

    // Shaded style (InkStyle.Shaded).
    private const double BumpFrequency = 1 / 4.0;   // per cell
    private const double BumpStrength = 3.0;
    private const double ShadeGain = 1.6;
    private const double SwellFrequency = 1 / 7.0;  // per cell: broad light/dark swells
    private const double SwellWeight = 0.6;
    private const double GlossPower = 30;
    private const double GlossGain = 1.4;
    private const double ShadeEdgeShadow = 0.8;
    private const double ShadeHueShift = 20;        // degrees at full light/shadow
    private const double ShadeLightness = 0.075;

    // Marbled style (InkStyle.Marbled): domain-warped noise folded into bands.
    private const double MarbleFrequency = 1 / 16.0; // per cell
    private const double MarbleWarp = 2.2;          // how far the coordinates get pushed around
    private const double MarbleBands = 1.4;         // bands per unit of warped noise
    private const double MarbleSharpness = 1.0;     // < 1 flattens band centres into distinct stripes
    private const double MarbleStreakGain = 0;      // thin glossy streaks along band crests
    private const double MarbleStreakPower = 40;
    private const double MarbleEdgeShadow = 0;
    private const double MarbleHueShift = 15;
    private const double MarbleLightness = 0.015;

    // Yellow and green base colors turn muddy (olive) when their hue shifts, so the marble's hue
    // shift fades out for base hues in this OKLCh range: full damping inside the core, a smooth
    // fade over the margin on either side. Yellow ≈ 105°, green ≈ 140°.
    private const double DampCenterHue = 120;
    private const double DampCoreHalfWidth = 30;   // 90°–150°: fully damped
    private const double DampFadeWidth = 25;       // fades back to full shift by 65° and 175°
    private const double DampAmount = 0.65;        // 1 = no hue shift at all inside the core

    /// <summary>Multiplier for the marble hue shift, from the base color's OKLCh hue.</summary>
    internal static double MarbleHueShiftScale(double baseHue)
    {
        double d = Math.Abs(((baseHue - DampCenterHue + 540) % 360) - 180);
        double weight = d <= DampCoreHalfWidth ? 1
            : d >= DampCoreHalfWidth + DampFadeWidth ? 0
            : 0.5 + 0.5 * Math.Cos(Math.PI * (d - DampCoreHalfWidth) / DampFadeWidth);
        return 1 - DampAmount * weight;
    }

    /// <summary>
    /// Oil-slick marbling: noise sampled at coordinates warped by noise, warped by noise again
    /// (Inigo Quilez' domain warping), then folded through a sine into flowing bands.
    /// The tone swings between the warm and cool hue-shifted versions of the ink color.
    /// </summary>
    private static (float[] Tone, float[] Gloss) InkMarble(PixelKind[] mask, int w, int h, int p, ulong seed)
    {
        var n = new GradientNoise(Rng.Derive(seed, 79));
        var tone = new float[w * h];
        var gloss = new float[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (mask[i] is not (PixelKind.Left or PixelKind.Right)) continue;

                double x0 = x * MarbleFrequency / p, y0 = y * MarbleFrequency / p;
                double qx = n.Fbm(x0, y0, 2);
                double qy = n.Fbm(x0 + 5.2, y0 + 1.3, 2);
                double rx = n.Fbm(x0 + MarbleWarp * qx + 1.7, y0 + MarbleWarp * qy + 9.2, 2);
                double ry = n.Fbm(x0 + MarbleWarp * qx + 8.3, y0 + MarbleWarp * qy + 2.8, 2);
                double v = n.Fbm(x0 + MarbleWarp * rx, y0 + MarbleWarp * ry, 3);

                double phase = v * MarbleBands * 2 * Math.PI;
                double s = Math.Sin(phase);
                tone[i] = (float)(Math.Sign(s) * Math.Pow(Math.Abs(s), MarbleSharpness));

                double crest = 0.5 + 0.5 * Math.Sin(phase * 2 + 1.3);
                gloss[i] = (float)(Math.Pow(crest, MarbleStreakPower) * MarbleStreakGain);
            }
        }

        return (tone, gloss);
    }

    /// <summary>
    /// Treats the ink as a thick, slightly bumpy layer lit from the top-left.
    /// Returns per pixel a tone in roughly [-1, 1] (shadow to light) and a gloss amount in [0, 1].
    /// Only paint pixels get values.
    /// </summary>
    private static (float[] Tone, float[] Gloss) InkLighting(PixelKind[] mask, int w, int h, int p, ulong seed)
    {
        var bumps = new GradientNoise(Rng.Derive(seed, 77));
        var swells = new GradientNoise(Rng.Derive(seed, 78));

        var height = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (mask[i] is PixelKind.Left or PixelKind.Right)
                    height[i] = (float)bumps.Fbm(x * BumpFrequency / p, y * BumpFrequency / p, 2);
            }

        // Light from the top-left, viewer straight above.
        var (lx, ly, lz) = Normalize(-0.55, -0.75, 0.6);
        var (hx, hy, hz) = Normalize(lx, ly, lz + 1);
        double glossBase = Math.Pow(hz, GlossPower);

        var tone = new float[w * h];
        var gloss = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (mask[i] is not (PixelKind.Left or PixelKind.Right)) continue;

                double dx = H(x + 1, y, i) - H(x - 1, y, i);
                double dy = H(x, y + 1, i) - H(x, y - 1, i);
                var (nx, ny, nz) = Normalize(-dx * BumpStrength, -dy * BumpStrength, 1);

                double lambert = nx * lx + ny * ly + nz * lz;
                double shade = (lambert - lz) * ShadeGain;
                double swell = swells.Fbm(x * SwellFrequency / p, y * SwellFrequency / p, 2);
                tone[i] = (float)Math.Clamp((1 - SwellWeight) * shade + SwellWeight * swell, -1.5, 1.5);

                double spec = Math.Pow(Math.Max(0, nx * hx + ny * hy + nz * hz), GlossPower) - glossBase;
                gloss[i] = (float)Math.Clamp(spec * GlossGain, 0, 1);
            }
        }

        return (tone, gloss);

        // Height of a neighbour; non-paint neighbours count as level with this pixel.
        double H(int nx, int ny, int self)
        {
            if (nx < 0 || ny < 0 || nx >= w || ny >= h) return height[self];
            int j = ny * w + nx;
            return mask[j] is PixelKind.Left or PixelKind.Right ? height[j] : height[self];
        }
    }

    private static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        double len = Math.Sqrt(x * x + y * y + z * z);
        return (x / len, y / len, z / len);
    }

    /// <summary>
    /// An ink color that shades with hue shifting in OKLCh: lights drift warm (toward yellow),
    /// shadows drift cool (toward blue) and get a little richer, instead of only changing value.
    /// </summary>
    private sealed class Ink(Rgb baseColor, double HueShift, double LightnessRange)
    {
        private const double WarmHue = 100;   // OKLCh hue of yellow
        private const double CoolHue = 265;   // OKLCh hue of blue
        private const double ChromaRange = 0.06;
        private static readonly Rgb GlossColor = new(255, 251, 238);

        private readonly Oklch _base = ColorMath.ToOklch(baseColor.R, baseColor.G, baseColor.B);

        /// <summary>Marble ink: hue shift scaled down for yellow/green base colors.</summary>
        public static Ink Marble(Rgb baseColor)
        {
            var hue = ColorMath.ToOklch(baseColor.R, baseColor.G, baseColor.B).H;
            return new Ink(baseColor, MarbleHueShift * MarbleHueShiftScale(hue), MarbleLightness);
        }

        public Rgb Shade(double tone, double gloss)
        {
            double hue = tone >= 0
                ? ColorMath.HueToward(_base.H, WarmHue, HueShift * tone)
                : ColorMath.HueToward(_base.H, CoolHue, HueShift * -tone);
            var shaded = new Oklch(
                _base.L + LightnessRange * tone,
                Math.Max(0, _base.C * (1 - ChromaRange * tone)),
                hue);

            var (r, g, b) = ColorMath.ToSrgb(shaded);
            var c = new Rgb(r, g, b);
            return gloss > 0 ? c.Mix(GlossColor, gloss) : c;
        }
    }

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
        canvas.DrawText($"{style.Left.Name} {Percent.Share(score.CoverageLeft)}", bar.Left, labelY, SKTextAlign.Left, labelFont, lp);
        canvas.DrawText($"{Percent.Share(score.UnpaintedShare)} unpainted", bar.MidX, labelY, SKTextAlign.Center, labelFont, up);
        canvas.DrawText($"{style.Right.Name} {Percent.Share(score.CoverageRight)}", bar.Right, labelY, SKTextAlign.Right, labelFont, rp);
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
        public Rgb Mix(Rgb other, double t) => new(R + (other.R - R) * t, G + (other.G - G) * t, B + (other.B - B) * t);

        public int ToRgba()
        {
            int r = Clamp(R), g = Clamp(G), b = Clamp(B);
            return r | (g << 8) | (b << 16) | unchecked((int)0xFF000000);
        }

        public SKColor ToSk() => new((byte)Clamp(R), (byte)Clamp(G), (byte)Clamp(B));

        private static int Clamp(double v) => (int)Math.Clamp(Math.Round(v), 0, 255);
    }
}
