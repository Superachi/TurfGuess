using TurfGuess.Core.Util;

namespace TurfGuess.Core.Noise;

/// <summary>
/// Seeded 2D gradient (Perlin-style) noise with fractal Brownian motion.
/// Output of <see cref="Sample"/> and <see cref="Fbm"/> is roughly in [-1, 1].
/// </summary>
public sealed class GradientNoise
{
    private const int GradientCount = 16;
    private static readonly double[] GradX = new double[GradientCount];
    private static readonly double[] GradY = new double[GradientCount];

    private readonly int[] _perm = new int[512];

    static GradientNoise()
    {
        for (int i = 0; i < GradientCount; i++)
        {
            double a = i * (2 * Math.PI / GradientCount);
            GradX[i] = Math.Cos(a);
            GradY[i] = Math.Sin(a);
        }
    }

    public GradientNoise(ulong seed)
    {
        var rng = new Rng(seed);
        var p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    public double Sample(double x, double y)
    {
        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        double xf = x - xi;
        double yf = y - yi;
        int X = xi & 255;
        int Y = yi & 255;

        double n00 = Dot(_perm[_perm[X] + Y], xf, yf);
        double n10 = Dot(_perm[_perm[X + 1] + Y], xf - 1, yf);
        double n01 = Dot(_perm[_perm[X] + Y + 1], xf, yf - 1);
        double n11 = Dot(_perm[_perm[X + 1] + Y + 1], xf - 1, yf - 1);

        double u = Fade(xf);
        double v = Fade(yf);
        double nx0 = n00 + u * (n10 - n00);
        double nx1 = n01 + u * (n11 - n01);
        return (nx0 + v * (nx1 - nx0)) * 1.41421356;
    }

    public double Fbm(double x, double y, int octaves, double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, amp = 1, freq = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            // Offset each octave so the octaves don't line up at the origin.
            sum += amp * Sample(x * freq + o * 17.31, y * freq - o * 9.73);
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }
        return sum / norm;
    }

    private static double Dot(int hash, double x, double y)
    {
        int h = hash & (GradientCount - 1);
        return GradX[h] * x + GradY[h] * y;
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
}
