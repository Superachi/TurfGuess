using TurfGuess.Core.Grid;
using TurfGuess.Core.MapGen;
using TurfGuess.Core.Noise;
using TurfGuess.Core.Util;

namespace TurfGuess.Core.Paint;

/// <summary>
/// Paints a map (DESIGN.md §4.2): two distance fields, an unpainted mask chosen by quantile,
/// a per-pixel ownership score with jitter and splats, and a quantile threshold that hits
/// the target winner share to the pixel.
/// </summary>
public static class PaintSimulator
{
    // Cost field: exp(contrast * fbm). Higher contrast = longer, more finger-like borders.
    private const double CostFrequency = 0.09;   // per cell
    private const double CostContrast = 1.1;

    // Unpainted mask: distance-from-both-spawns plus a "holes" noise.
    private const double HolesFrequency = 0.22;  // per cell
    private const double HolesWeight = 0.45;

    // Border roughness in the ownership score.
    private const double JitterFrequency = 1.1;  // per cell
    private const double JitterAmplitude = 0.10;
    private const double GrainFrequency = 4.0;   // per cell
    private const double GrainAmplitude = 0.035;

    // Splat stamps.
    private const double SplatStrength = 8.0;
    private const double SplatCandidateBand = 0.3;

    public static PaintResult? Paint(GameMap map, PaintOptions options, Side winner, double winnerShare, double unpaintedShare, ulong seed)
    {
        if (winnerShare is <= 0.5 or >= 1) throw new ArgumentOutOfRangeException(nameof(winnerShare));
        if (unpaintedShare is < 0 or >= 0.5) throw new ArgumentOutOfRangeException(nameof(unpaintedShare));

        var cells = map.Cells;
        int cw = cells.Width, ch = cells.Height;
        int p = options.PixelsPerCell;
        int pw = cw * p, ph = ch * p;

        // 1. Cost field.
        var costNoise = new GradientNoise(Rng.Derive(seed, 1));
        var cost = new float[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                cost[y * cw + x] = cells[x, y].IsWalkable()
                    ? (float)Math.Exp(CostContrast * costNoise.Fbm(x * CostFrequency, y * CostFrequency, 3))
                    : float.PositiveInfinity;

        // 2. Distance fields, computed once.
        var distL = Dijkstra(cells, cost, map.SpawnLeft);
        var distR = Dijkstra(cells, cost, map.SpawnRight);

        // 3. Base mask from cell types + upsampled distances for floor pixels.
        var mask = new PixelKind[pw * ph];
        var pixelToFloor = new int[pw * ph];
        var floorPixels = new List<int>(pw * ph / 2);
        var dl = new List<float>(pw * ph / 2);
        var dr = new List<float>(pw * ph / 2);

        for (int py = 0; py < ph; py++)
        {
            for (int px = 0; px < pw; px++)
            {
                int pi = py * pw + px;
                pixelToFloor[pi] = -1;
                var cell = cells[px / p, py / p];
                switch (cell)
                {
                    case CellType.Void: mask[pi] = PixelKind.Void; break;
                    case CellType.Wall: mask[pi] = PixelKind.Wall; break;
                    case CellType.SpawnLeft: mask[pi] = PixelKind.SpawnLeft; break;
                    case CellType.SpawnRight: mask[pi] = PixelKind.SpawnRight; break;
                    default:
                        double gx = (px + 0.5) / p - 0.5, gy = (py + 0.5) / p - 0.5;
                        pixelToFloor[pi] = floorPixels.Count;
                        floorPixels.Add(pi);
                        dl.Add(Interpolate(distL, cells, gx, gy));
                        dr.Add(Interpolate(distR, cells, gx, gy));
                        mask[pi] = PixelKind.Unpainted;
                        break;
                }
            }
        }

        int nFloor = floorPixels.Count;
        if (nFloor == 0) return null;

        // 4. Unpainted mask, fixed before the margin is set.
        var holes = new GradientNoise(Rng.Derive(seed, 2));
        float maxMin = 0;
        for (int i = 0; i < nFloor; i++) maxMin = Math.Max(maxMin, Math.Min(dl[i], dr[i]));
        if (maxMin <= 0) maxMin = 1;

        var unpaintedKey = new float[nFloor];
        var order = new int[nFloor];
        for (int i = 0; i < nFloor; i++)
        {
            int pi = floorPixels[i];
            double gx = (pi % pw + 0.5) / p, gy = (pi / pw + 0.5) / p;
            double u = Math.Min(dl[i], dr[i]) / maxMin + HolesWeight * holes.Fbm(gx * HolesFrequency, gy * HolesFrequency, 2);
            unpaintedKey[i] = (float)-u; // ascending sort => largest u first
            order[i] = i;
        }
        Array.Sort(unpaintedKey, order);

        int nUnpainted = (int)Math.Round(unpaintedShare * nFloor);
        var isPainted = new bool[nFloor];
        for (int k = nUnpainted; k < nFloor; k++) isPainted[order[k]] = true;

        // 5. Ownership score s = ln dR − ln dL + jitter (+ splats). Positive = closer to left.
        var jitter = new GradientNoise(Rng.Derive(seed, 3));
        var grain = new GradientNoise(Rng.Derive(seed, 4));
        var score = new float[nFloor];
        for (int i = 0; i < nFloor; i++)
        {
            if (!isPainted[i]) continue;
            int pi = floorPixels[i];
            double gx = (pi % pw + 0.5) / p, gy = (pi / pw + 0.5) / p;
            double s = Math.Log(dr[i] + 1.0) - Math.Log(dl[i] + 1.0)
                + JitterAmplitude * jitter.Fbm(gx * JitterFrequency, gy * JitterFrequency, 3)
                + GrainAmplitude * grain.Sample(gx * GrainFrequency, gy * GrainFrequency);
            score[i] = (float)s;
        }

        if (options.Splatter)
            ApplySplats(new Rng(Rng.Derive(seed, 5)), score, isPainted, floorPixels, pixelToFloor, pw, ph, p);

        // 6. Quantile threshold: exactly nLeft painted pixels go to the left team.
        var paintedKeys = new List<float>(nFloor);
        var paintedIdx = new List<int>(nFloor);
        for (int i = 0; i < nFloor; i++)
        {
            if (!isPainted[i]) continue;
            paintedKeys.Add(-score[i]); // ascending sort => most "left" first
            paintedIdx.Add(i);
        }

        int nPainted = paintedIdx.Count;
        if (nPainted == 0) return null;

        var keys = paintedKeys.ToArray();
        var idx = paintedIdx.ToArray();
        Array.Sort(keys, idx);

        double leftShare = winner == Side.Left ? winnerShare : 1 - winnerShare;
        int nLeft = (int)Math.Round(leftShare * nPainted);

        // 7. Final mask.
        for (int k = 0; k < nPainted; k++)
            mask[floorPixels[idx[k]]] = k < nLeft ? PixelKind.Left : PixelKind.Right;

        var result = new PaintResult { Width = pw, Height = ph, PixelsPerCell = p, Mask = mask };

        // Sanity: the losing team must still hold ground next to its own spawn.
        var loser = winner.Opposite();
        if (!TouchesSpawn(result, map.Spawn(loser), loser == Side.Left ? PixelKind.Left : PixelKind.Right))
            return null;

        return result;
    }

    private static float[] Dijkstra(Grid<CellType> g, float[] cost, RectI source)
    {
        var dist = new float[g.Cells.Length];
        Array.Fill(dist, float.PositiveInfinity);
        var queue = new PriorityQueue<int, float>();

        for (int y = source.Y; y < source.Bottom; y++)
        {
            for (int x = source.X; x < source.Right; x++)
            {
                int i = g.Index(x, y);
                dist[i] = 0;
                queue.Enqueue(i, 0);
            }
        }

        while (queue.TryDequeue(out int i, out float d))
        {
            if (d > dist[i]) continue;
            int x = i % g.Width, y = i / g.Width;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (!g.InBounds(nx, ny) || !g[nx, ny].IsWalkable()) continue;
                    bool diagonal = dx != 0 && dy != 0;
                    // No cutting corners past walls.
                    if (diagonal && (!g[x + dx, y].IsWalkable() || !g[x, y + dy].IsWalkable())) continue;

                    int j = g.Index(nx, ny);
                    float step = (cost[i] + cost[j]) * 0.5f * (diagonal ? 1.41421356f : 1f);
                    float nd = d + step;
                    if (nd < dist[j])
                    {
                        dist[j] = nd;
                        queue.Enqueue(j, nd);
                    }
                }
            }
        }

        return dist;
    }

    /// <summary>Bilinear interpolation over cell centers, ignoring non-walkable neighbours.</summary>
    private static float Interpolate(float[] dist, Grid<CellType> g, double gx, double gy)
    {
        int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
        double fx = gx - x0, fy = gy - y0;
        double sum = 0, weight = 0;

        Accumulate(x0, y0, (1 - fx) * (1 - fy));
        Accumulate(x0 + 1, y0, fx * (1 - fy));
        Accumulate(x0, y0 + 1, (1 - fx) * fy);
        Accumulate(x0 + 1, y0 + 1, fx * fy);

        if (weight <= 0)
        {
            // Pixel sits at the far edge of its cell with only walls around: use its own cell.
            int cx = Math.Clamp((int)Math.Round(gx), 0, g.Width - 1), cy = Math.Clamp((int)Math.Round(gy), 0, g.Height - 1);
            return dist[g.Index(cx, cy)];
        }
        return (float)(sum / weight);

        void Accumulate(int x, int y, double w)
        {
            if (w <= 0 || !g.InBounds(x, y)) return;
            float d = dist[g.Index(x, y)];
            if (float.IsInfinity(d)) return;
            sum += d * w;
            weight += w;
        }
    }

    private static void ApplySplats(Rng rng, float[] score, bool[] isPainted, List<int> floorPixels, int[] pixelToFloor, int pw, int ph, int p)
    {
        int count = rng.Next(18, 33);
        for (int n = 0; n < count; n++)
        {
            // Prefer spots near the border, where a stray splat is visible.
            int centre = -1;
            for (int t = 0; t < 60; t++)
            {
                int i = rng.Next(0, floorPixels.Count);
                if (isPainted[i] && Math.Abs(score[i]) < SplatCandidateBand)
                {
                    centre = floorPixels[i];
                    break;
                }
            }
            if (centre < 0) continue;

            double sign = rng.Chance(0.5) ? 1 : -1;
            double cx = centre % pw, cy = centre / pw;
            double r = rng.Range(0.6, 1.5) * p;
            int lobes = rng.Next(3, 7);
            double phase = rng.Range(0, 2 * Math.PI);

            Stamp(cx, cy, r, lobes, phase, 0.18);

            int droplets = rng.Next(2, 6);
            for (int d = 0; d < droplets; d++)
            {
                double ang = rng.Range(0, 2 * Math.PI);
                double dist = r * rng.Range(1.3, 2.1);
                Stamp(cx + Math.Cos(ang) * dist, cy + Math.Sin(ang) * dist, r * rng.Range(0.15, 0.32), 0, 0, 0);
            }

            void Stamp(double sx, double sy, double radius, int lobeCount, double lobePhase, double wobble)
            {
                double maxR = radius * (1 + wobble);
                int x0 = Math.Max(0, (int)(sx - maxR)), x1 = Math.Min(pw - 1, (int)(sx + maxR));
                int y0 = Math.Max(0, (int)(sy - maxR)), y1 = Math.Min(ph - 1, (int)(sy + maxR));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        double dx = x - sx, dy = y - sy;
                        double dd = Math.Sqrt(dx * dx + dy * dy);
                        double limit = radius * (1 + wobble * Math.Sin(lobeCount * Math.Atan2(dy, dx) + lobePhase));
                        if (dd > limit) continue;
                        int fi = pixelToFloor[y * pw + x];
                        if (fi < 0 || !isPainted[fi]) continue;
                        score[fi] = (float)(sign * SplatStrength);
                    }
                }
            }
        }
    }

    private static bool TouchesSpawn(PaintResult r, RectI spawnCells, PixelKind kind)
    {
        int p = r.PixelsPerCell;
        var ring = spawnCells.Inflate(1);
        int x0 = Math.Max(0, ring.X * p), x1 = Math.Min(r.Width, ring.Right * p);
        int y0 = Math.Max(0, ring.Y * p), y1 = Math.Min(r.Height, ring.Bottom * p);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (r[x, y] == kind) return true;
        return false;
    }
}
