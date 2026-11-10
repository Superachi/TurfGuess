using TurfGuess.Core.Paint;

namespace TurfGuess.Core.Tests;

public class CornerRoundingTests
{
    private static PixelKind[] Mask(int w, int h, Func<int, int, bool> isFloor)
    {
        var m = new PixelKind[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                m[y * w + x] = isFloor(x, y) ? PixelKind.Unpainted : PixelKind.Void;
        return m;
    }

    [Fact]
    public void Distance_transform_matches_brute_force()
    {
        const int w = 23, h = 17;
        var rng = new Random(5);
        var set = Enumerable.Range(0, w * h).Select(_ => rng.NextDouble() < 0.1).ToArray();
        var fast = CornerRounding.SquaredDistanceTo(set, w, h, feature: true);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double best = double.PositiveInfinity;
                for (int j = 0; j < set.Length; j++)
                {
                    if (!set[j]) continue;
                    int dx = j % w - x, dy = j / w - y;
                    best = Math.Min(best, dx * dx + dy * dy);
                }
                if (double.IsPositiveInfinity(best)) continue;
                Assert.Equal(best, fast[y * w + x], 3);
            }
        }
    }

    [Fact]
    public void Outer_corners_of_a_square_are_cut_and_the_inside_kept()
    {
        const int w = 60, h = 60;
        var m = Mask(w, h, (x, y) => x is >= 10 and < 50 && y is >= 10 and < 50);
        CornerRounding.Apply(m, w, h, outerRadiusPx: 8, innerRadiusPx: 0);

        Assert.Equal(PixelKind.Void, m[10 * w + 10]);          // corner pixel cut
        Assert.Equal(PixelKind.Void, m[49 * w + 49]);
        Assert.Equal(PixelKind.Unpainted, m[30 * w + 30]);     // centre kept
        Assert.Equal(PixelKind.Unpainted, m[10 * w + 30]);     // straight edge kept
        Assert.Equal(PixelKind.Unpainted, m[14 * w + 14]);     // inside the arc kept
    }

    [Fact]
    public void Inner_corners_are_filled_with_a_curve()
    {
        // An L-shape: the concave corner is at (30, 30).
        const int w = 60, h = 60;
        var m = Mask(w, h, (x, y) => (x is >= 10 and < 30 && y is >= 10 and < 50) || (x is >= 10 and < 50 && y is >= 30 and < 50));
        CornerRounding.Apply(m, w, h, outerRadiusPx: 0, innerRadiusPx: 8);

        Assert.Equal(PixelKind.Unpainted, m[29 * w + 30]);     // right at the concave corner: filled
        Assert.Equal(PixelKind.Void, m[20 * w + 40]);          // far from the corner: still void
    }

    [Fact]
    public void Zero_radius_changes_nothing()
    {
        const int w = 30, h = 30;
        var m = Mask(w, h, (x, y) => x is >= 5 and < 25 && y is >= 5 and < 25);
        var before = (PixelKind[])m.Clone();
        CornerRounding.Apply(m, w, h, 0, 0);
        Assert.Equal(before, m);
    }

    [Fact]
    public void Two_cell_wide_corridor_survives_the_maximum_radius()
    {
        const int p = 16, w = 200, h = 80;
        var m = Mask(w, h, (x, y) => y is >= 24 and < 24 + 2 * p);
        CornerRounding.Apply(m, w, h, 0.95 * p, 0.95 * p);
        // The ends touch the image edge (which counts as void) and get rounded; the middle must survive.
        for (int x = 2 * p; x < w - 2 * p; x++)
            Assert.Equal(PixelKind.Unpainted, m[(24 + p) * w + x]);
    }

    [Fact]
    public void Walls_and_spawns_are_never_cut()
    {
        const int w = 40, h = 40;
        var m = Mask(w, h, (x, y) => x is >= 5 and < 35 && y is >= 5 and < 35);
        m[5 * w + 5] = PixelKind.SpawnLeft;   // a spawn pixel sitting exactly on a corner
        CornerRounding.Apply(m, w, h, 8, 0);
        Assert.Equal(PixelKind.SpawnLeft, m[5 * w + 5]);
    }

    [Fact]
    public void Rounded_rounds_still_hit_their_target_share()
    {
        var o = new GeneratorOptions { Paint = { PixelsPerCell = 8, CornerRadius = 0.9, InnerCornerRadius = 0.9 } };
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var r = RoundGenerator.Generate(seed, o);
            Assert.InRange(Math.Abs(r.Score.WinnerShare - r.TargetWinnerShare), 0, 1.0 / r.Score.PaintedPixels);
        }
    }
}
