namespace TurfGuess.Core.Paint;

/// <summary>
/// Rounds the corners of the map outline at pixel resolution (DESIGN.md §4.2, step 3),
/// like CSS border-radius:
/// <list type="bullet">
/// <item>a morphological <b>opening</b> with a disk rounds the outer (convex) corners;</item>
/// <item>a morphological <b>closing</b> with a disk fills the inner (concave) corners with a curve.</item>
/// </list>
/// Both use an exact Euclidean distance transform, so any radius costs the same.
/// "Solid" is everything that isn't void (floor, walls, spawn pads), so only the outline changes.
/// </summary>
internal static class CornerRounding
{
    /// <summary>
    /// Applies the rounding to a base mask whose floor pixels are <see cref="PixelKind.Unpainted"/>.
    /// Pixels cut off become <see cref="PixelKind.Void"/>; pixels filled in become floor.
    /// </summary>
    public static void Apply(PixelKind[] mask, int w, int h, double outerRadiusPx, double innerRadiusPx)
    {
        if (outerRadiusPx <= 0 && innerRadiusPx <= 0) return;

        var solid = new bool[mask.Length];
        for (int i = 0; i < mask.Length; i++) solid[i] = mask[i] != PixelKind.Void;

        var rounded = solid;
        if (outerRadiusPx > 0) rounded = Dilate(Erode(rounded, w, h, outerRadiusPx), w, h, outerRadiusPx);
        if (innerRadiusPx > 0) rounded = Erode(Dilate(rounded, w, h, innerRadiusPx), w, h, innerRadiusPx);

        for (int i = 0; i < mask.Length; i++)
        {
            if (solid[i] && !rounded[i] && mask[i] == PixelKind.Unpainted) mask[i] = PixelKind.Void;
            else if (!solid[i] && rounded[i]) mask[i] = PixelKind.Unpainted;
        }
    }

    /// <summary>Keeps pixels further than <paramref name="r"/> from any non-set pixel.</summary>
    private static bool[] Erode(bool[] set, int w, int h, double r)
    {
        var dist = SquaredDistanceTo(set, w, h, feature: false);
        var result = new bool[set.Length];
        double r2 = r * r;
        for (int i = 0; i < set.Length; i++) result[i] = set[i] && dist[i] > r2;
        return result;
    }

    /// <summary>Adds pixels within <paramref name="r"/> of any set pixel.</summary>
    private static bool[] Dilate(bool[] set, int w, int h, double r)
    {
        var dist = SquaredDistanceTo(set, w, h, feature: true);
        var result = new bool[set.Length];
        double r2 = r * r;
        for (int i = 0; i < set.Length; i++) result[i] = dist[i] <= r2;
        return result;
    }

    /// <summary>
    /// Exact squared Euclidean distance from every pixel to the nearest pixel whose value equals
    /// <paramref name="feature"/> (Felzenszwalb &amp; Huttenlocher), separable over columns then rows.
    /// Pixels outside the image count as non-solid, so the map edge erodes like void.
    /// </summary>
    internal static float[] SquaredDistanceTo(bool[] set, int w, int h, bool feature)
    {
        const float Inf = 1e20f;
        var grid = new float[w * h];
        for (int i = 0; i < grid.Length; i++) grid[i] = set[i] == feature ? 0 : Inf;

        int n = Math.Max(w, h);
        var f = new float[n + 2];
        var d = new float[n + 2];
        var v = new int[n + 2];
        var z = new float[n + 3];

        // Columns. Padding one pixel beyond each end as void (feature when looking for non-solid).
        bool padIsFeature = !feature;
        for (int x = 0; x < w; x++)
        {
            f[0] = padIsFeature ? 0 : Inf;
            for (int y = 0; y < h; y++) f[y + 1] = grid[y * w + x];
            f[h + 1] = padIsFeature ? 0 : Inf;
            Transform(f, h + 2, d, v, z);
            for (int y = 0; y < h; y++) grid[y * w + x] = d[y + 1];
        }

        // Rows.
        for (int y = 0; y < h; y++)
        {
            f[0] = padIsFeature ? 0 : Inf;
            for (int x = 0; x < w; x++) f[x + 1] = grid[y * w + x];
            f[w + 1] = padIsFeature ? 0 : Inf;
            Transform(f, w + 2, d, v, z);
            for (int x = 0; x < w; x++) grid[y * w + x] = d[x + 1];
        }

        return grid;
    }

    /// <summary>1D squared distance transform of sampled function <paramref name="f"/> (lower envelope of parabolas).</summary>
    private static void Transform(float[] f, int n, float[] d, int[] v, float[] z)
    {
        int k = 0;
        v[0] = 0;
        z[0] = float.NegativeInfinity;
        z[1] = float.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            float s = Intersection(f, q, v[k]);
            while (s <= z[k])
            {
                k--;
                s = Intersection(f, q, v[k]);
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = float.PositiveInfinity;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            int vk = v[k];
            d[q] = (q - vk) * (float)(q - vk) + f[vk];
        }
    }

    /// <summary>Where the parabolas rooted at q and p intersect.</summary>
    private static float Intersection(float[] f, int q, int p) =>
        ((f[q] + q * (float)q) - (f[p] + p * (float)p)) / (2f * q - 2f * p);
}
