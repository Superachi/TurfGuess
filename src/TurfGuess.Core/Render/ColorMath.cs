namespace TurfGuess.Core.Render;

/// <summary>A color in OKLCh: perceptual lightness (0–1), chroma and hue in degrees.</summary>
internal readonly record struct Oklch(double L, double C, double H);

/// <summary>sRGB ⇄ OKLab/OKLCh conversions (Björn Ottosson's OKLab).</summary>
internal static class ColorMath
{
    public static Oklch ToOklch(double r8, double g8, double b8)
    {
        double r = ToLinear(r8 / 255.0), g = ToLinear(g8 / 255.0), b = ToLinear(b8 / 255.0);

        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        double L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        double A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;

        double h = Math.Atan2(B, A) * 180 / Math.PI;
        if (h < 0) h += 360;
        return new Oklch(L, Math.Sqrt(A * A + B * B), h);
    }

    /// <summary>Converts back to 8-bit sRGB, clamping colors outside the gamut.</summary>
    public static (double R, double G, double B) ToSrgb(Oklch c)
    {
        double hr = c.H * Math.PI / 180;
        double A = c.C * Math.Cos(hr), B = c.C * Math.Sin(hr);

        double l = c.L + 0.3963377774 * A + 0.2158037573 * B;
        double m = c.L - 0.1055613458 * A - 0.0638541728 * B;
        double s = c.L - 0.0894841775 * A - 1.2914855480 * B;
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;

        double r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

        return (255 * ToGamma(r), 255 * ToGamma(g), 255 * ToGamma(b));
    }

    /// <summary>Moves hue <paramref name="h"/> toward <paramref name="target"/> along the shorter arc, by at most <paramref name="degrees"/>.</summary>
    public static double HueToward(double h, double target, double degrees)
    {
        double diff = ((target - h + 540) % 360) - 180; // signed, in (-180, 180]
        double step = Math.Sign(diff) * Math.Min(Math.Abs(diff), degrees);
        return (h + step + 360) % 360;
    }

    private static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double ToGamma(double c)
    {
        c = Math.Clamp(c, 0, 1);
        return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
    }
}
