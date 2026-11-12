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

    // Unpainted mask: players rush forward, so floor near the spawns is what gets neglected.
    // Key = closeness to the nearest spawn + a "holes" noise that breaks it into patches.
    private const double HolesFrequency = 0.22;  // per cell
    private const double HolesWeight = 0.7;
    private const double SpawnNeglect = 1.0;
    private const float SpawnTrail = 3.5f;       // cost-distance around a pad that is always painted
    private const double SpawnTrailRoughness = 0.8; // how ragged the spawn trail's edge is
    private const double DiligentNeglect = 0.25; // a diligent team neglects its spawn area this much (vs 1.0)

    // Rough, sputtery edge where paint meets unpainted floor (same idea as the front-line jitter).
    private const double BorderJitterFrequency = 1.1;  // per cell
    private const double BorderJitterAmplitude = 0.06;
    private const double BorderGrainFrequency = 4.0;   // per cell
    private const double BorderGrainAmplitude = 0.02;

    // Paint splats landing in the unpainted floor near spawns.
    private const double SpawnSplatBand = 0.25;        // how far into the unpainted area (in units of the key)
    private const double SpawnSplatMinCloseness = 0.4; // only near a spawn, not in far corners

    // Extra score (in the fight band) for the team that neglected its spawn more: it painted the middle.
    private const double MiddleEdge = 0.15;

    // Fight zone: around the front line both colors interleave in blotches.
    private const double FightFrequency = 0.35;  // per cell
    private const double FightAmplitude = 0.9;
    private const double FightWidth = 0.35;      // in units of the ownership score

    // Border roughness in the ownership score.
    private const double JitterFrequency = 1.1;  // per cell
    private const double JitterAmplitude = 0.10;
    private const double GrainFrequency = 4.0;   // per cell
    private const double GrainAmplitude = 0.035;

    // Splat stamps.
    private const double SplatStrength = 8.0;
    private const double SplatCandidateBand = 0.5;
    private const double StrayBreakupFrequency = 0.6; // per cell, blotch size of the holes in stray ink
    private const double StrayBreakupCut = 0.15;      // noise above this leaves a hole (higher = more solid)
    private const double StrayMinDepth = 0.6;       // stray ink starts at least this far past the front, in the other team's half

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

        // 3. Base mask from cell types, rounded corners on the outline, then upsampled distances
        //    for every floor pixel.
        var mask = new PixelKind[pw * ph];
        for (int py = 0; py < ph; py++)
        {
            for (int px = 0; px < pw; px++)
            {
                mask[py * pw + px] = cells[px / p, py / p] switch
                {
                    CellType.Void => PixelKind.Void,
                    CellType.Wall => PixelKind.Wall,
                    CellType.SpawnLeft => PixelKind.SpawnLeft,
                    CellType.SpawnRight => PixelKind.SpawnRight,
                    _ => PixelKind.Unpainted,
                };
            }
        }

        CornerRounding.Apply(mask, pw, ph, options.CornerRadius * p, options.InnerCornerRadius * p);

        var pixelToFloor = new int[pw * ph];
        var floorPixels = new List<int>(pw * ph / 2);
        var dl = new List<float>(pw * ph / 2);
        var dr = new List<float>(pw * ph / 2);
        for (int pi = 0; pi < mask.Length; pi++)
        {
            pixelToFloor[pi] = -1;
            if (mask[pi] != PixelKind.Unpainted) continue;
            int px = pi % pw, py = pi / pw;
            double gx = (px + 0.5) / p - 0.5, gy = (py + 0.5) / p - 0.5;
            pixelToFloor[pi] = floorPixels.Count;
            floorPixels.Add(pi);
            dl.Add(Interpolate(distL, cells, gx, gy));
            dr.Add(Interpolate(distR, cells, gx, gy));
        }

        int nFloor = floorPixels.Count;
        if (nFloor == 0) return null;

        // 4. Unpainted mask, fixed before the margin is set.
        //    Key u: closeness to a spawn (scaled per team by how much it neglected its spawn)
        //    + holes noise + a rough border. The highest u become unpainted.
        var diligence = new Rng(Rng.Derive(seed, 7));
        // One draw for both teams: either both neglect their spawn or both paint it diligently,
        // so the winner can't be read off which side has the barer spawn.
        bool diligentLeft = diligence.Chance(options.DiligentSpawnChance);
        bool diligentRight = diligentLeft;
        // On top of that, each team's neglect is scaled by its own random factor, independent of who
        // wins, so the two spawns can be very differently bare and players have to judge each one.
        double variance = options.SpawnNeglectVariance;
        double neglectLeft = (diligentLeft ? DiligentNeglect : SpawnNeglect) * diligence.Range(1 - variance, 1 + variance);
        double neglectRight = (diligentRight ? DiligentNeglect : SpawnNeglect) * diligence.Range(1 - variance, 1 + variance);

        var holes = new GradientNoise(Rng.Derive(seed, 2));
        var borderJitter = new GradientNoise(Rng.Derive(seed, 8));
        var borderGrain = new GradientNoise(Rng.Derive(seed, 9));
        float maxMin = 0;
        for (int i = 0; i < nFloor; i++) maxMin = Math.Max(maxMin, Math.Min(dl[i], dr[i]));
        if (maxMin <= 0) maxMin = 1;

        var u = new float[nFloor];
        var closeness = new float[nFloor];
        for (int i = 0; i < nFloor; i++)
        {
            int pi = floorPixels[i];
            double gx = (pi % pw + 0.5) / p, gy = (pi / pw + 0.5) / p;
            double rough = borderJitter.Fbm(gx * BorderJitterFrequency, gy * BorderJitterFrequency, 3);

            // The spawn trail's edge is roughened by the same noise, so it isn't a smooth ring.
            if (Math.Min(dl[i], dr[i]) < SpawnTrail * (1 + SpawnTrailRoughness * rough))
            {
                u[i] = float.NegativeInfinity;
                closeness[i] = 1;
                continue;
            }

            double closeLeft = 1 - dl[i] / maxMin, closeRight = 1 - dr[i] / maxMin;
            closeness[i] = (float)Math.Max(closeLeft, closeRight);
            u[i] = (float)(Math.Max(neglectLeft * closeLeft, neglectRight * closeRight)
                + HolesWeight * holes.Fbm(gx * HolesFrequency, gy * HolesFrequency, 2)
                + BorderJitterAmplitude * rough
                + BorderGrainAmplitude * borderGrain.Sample(gx * BorderGrainFrequency, gy * BorderGrainFrequency));
        }

        int nUnpainted = (int)Math.Round(unpaintedShare * nFloor);

        // 4b. Sputters near spawns: estimate the unpainted border, then stamp paint splats just
        //     inside the unpainted side of it. Stamped pixels are forced painted (u = −∞), and the
        //     quantile below still lands exactly on the target share.
        if (options.Splatter && nUnpainted > 0)
        {
            float border = KthLargest(u, nUnpainted);
            ApplySpawnSplats(new Rng(Rng.Derive(seed, 10)), u, closeness, border, floorPixels, pixelToFloor, pw, ph, p);
        }

        var unpaintedKey = new float[nFloor];
        var order = new int[nFloor];
        for (int i = 0; i < nFloor; i++)
        {
            unpaintedKey[i] = -u[i]; // ascending sort => largest u first
            order[i] = i;
        }
        Array.Sort(unpaintedKey, order);

        var isPainted = new bool[nFloor];
        for (int k = nUnpainted; k < nFloor; k++) isPainted[order[k]] = true;

        // 5. Ownership score s = ln dR − ln dL + jitter (+ splats). Positive = closer to left.
        var jitter = new GradientNoise(Rng.Derive(seed, 3));
        var grain = new GradientNoise(Rng.Derive(seed, 4));
        var score = new float[nFloor];

        // Tilt: a gentle slope along y turns the front from vertical to a random angle. Near the
        // middle the log-ratio changes by about 2/(D+1) per cell (D = distance to the middle), so
        // a slope of tan(angle) times that moves the front sideways by tan(angle) per cell of y.
        double tilt = 0, tiltCenterY = ph / (2.0 * p);
        if (options.FrontTiltMaxDegrees > 0)
        {
            double angle = new Rng(Rng.Derive(seed, 13)).Range(-options.FrontTiltMaxDegrees, options.FrontTiltMaxDegrees) * Math.PI / 180;
            double sum = 0; int n = 0;
            for (int i = 0; i < nFloor; i++)
            {
                if (Math.Abs(Math.Log(dr[i] + 1.0) - Math.Log(dl[i] + 1.0)) < 0.05) { sum += dl[i]; n++; }
            }
            double mid = n > 0 ? sum / n : maxMin;
            tilt = Math.Tan(angle) * 2.0 / (mid + 1.0);
        }

        for (int i = 0; i < nFloor; i++)
        {
            if (!isPainted[i]) continue;
            int pi = floorPixels[i];
            double gx = (pi % pw + 0.5) / p, gy = (pi / pw + 0.5) / p;
            double s = Math.Log(dr[i] + 1.0) - Math.Log(dl[i] + 1.0) + tilt * (gy - tiltCenterY)
                + JitterAmplitude * jitter.Fbm(gx * JitterFrequency, gy * JitterFrequency, 3)
                + GrainAmplitude * grain.Sample(gx * GrainFrequency, gy * GrainFrequency);
            score[i] = (float)s;
        }

        int nPainted = nFloor - nUnpainted;
        if (nPainted <= 0) return null;
        double leftShare = winner == Side.Left ? winnerShare : 1 - winnerShare;
        int nLeft = (int)Math.Round(leftShare * nPainted);

        // 5b. Fight zone. Find where the front line will roughly be for this margin, then add
        //     blotchy noise that fades out away from it, so both colors interleave there.
        float front = Threshold(score, isPainted, nLeft);
        //     The team that neglected its spawn more rushed forward, so it gets a slight edge in this band
        //     (positive score = left). The quantile below keeps the shares exact; the edge only moves
        //     paint around.
        double middleEdge = MiddleEdge * Math.Clamp(neglectLeft - neglectRight, -1, 1);
        var fight = new GradientNoise(Rng.Derive(seed, 6));
        for (int i = 0; i < nFloor; i++)
        {
            if (!isPainted[i]) continue;
            double z = (score[i] - front) / FightWidth;
            if (z is > 4 or < -4) continue;
            int pi = floorPixels[i];
            double gx = (pi % pw + 0.5) / p, gy = (pi / pw + 0.5) / p;
            score[i] += (float)(Math.Exp(-z * z) * (FightAmplitude * fight.Fbm(gx * FightFrequency, gy * FightFrequency, 3) + middleEdge));
        }

        if (options.Splatter)
            ApplySplats(new Rng(Rng.Derive(seed, 5)), score, isPainted, floorPixels, pixelToFloor, pw, ph, p, front);

        if (options.StrayInkChance > 0)
            ApplyStrayInk(new Rng(Rng.Derive(seed, 11)), new GradientNoise(Rng.Derive(seed, 12)), options.StrayInkChance, score, isPainted, floorPixels, pixelToFloor, pw, ph, p, front);

        // 6. Quantile threshold: exactly nLeft painted pixels go to the left team.
        var keys = new float[nPainted];
        var idx = new int[nPainted];
        for (int i = 0, k = 0; i < nFloor; i++)
        {
            if (!isPainted[i]) continue;
            keys[k] = -score[i]; // ascending sort => most "left" first
            idx[k++] = i;
        }
        Array.Sort(keys, idx);

        // 7. Final mask.
        for (int k = 0; k < nPainted; k++)
            mask[floorPixels[idx[k]]] = k < nLeft ? PixelKind.Left : PixelKind.Right;

        var result = new PaintResult
        {
            Width = pw,
            Height = ph,
            PixelsPerCell = p,
            Mask = mask,
            DiligentLeft = diligentLeft,
            DiligentRight = diligentRight,
        };

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
            // No walkable cell among the four nearest centres (an edge pixel next to walls, or a
            // pixel filled in by corner rounding): use the nearest finite distance around it.
            int cx = Math.Clamp((int)Math.Round(gx), 0, g.Width - 1), cy = Math.Clamp((int)Math.Round(gy), 0, g.Height - 1);
            float best = float.PositiveInfinity;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (g.InBounds(cx + dx, cy + dy))
                        best = Math.Min(best, dist[g.Index(cx + dx, cy + dy)]);
            return float.IsInfinity(best) ? 0 : best;
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

    /// <summary>The score of the <paramref name="nLeft"/>-th most "left" painted pixel.</summary>
    private static float Threshold(float[] score, bool[] isPainted, int nLeft)
    {
        var values = new List<float>(score.Length);
        for (int i = 0; i < score.Length; i++)
            if (isPainted[i]) values.Add(-score[i]);
        if (values.Count == 0) return 0;
        values.Sort();
        return -values[Math.Clamp(nLeft, 0, values.Count - 1)];
    }

    private static void ApplySplats(Rng rng, float[] score, bool[] isPainted, List<int> floorPixels, int[] pixelToFloor, int pw, int ph, int p, float front)
    {
        int count = rng.Next(28, 46);
        for (int n = 0; n < count; n++)
        {
            // Prefer spots near the front line, where the fight happened.
            int centre = -1;
            for (int t = 0; t < 60; t++)
            {
                int i = rng.Next(0, floorPixels.Count);
                if (isPainted[i] && Math.Abs(score[i] - front) < SplatCandidateBand)
                {
                    centre = floorPixels[i];
                    break;
                }
            }
            if (centre < 0) continue;

            // Splat value relative to the front, so a stamp shows that team's color wherever it lands.
            double sign = rng.Chance(0.5) ? 1 : -1;
            float splatValue = (float)(front + sign * SplatStrength);
            StampSplat(rng, centre % pw, centre / pw, rng.Range(0.6, 1.5) * p, 2, 6, pw, ph, pixelToFloor, fi =>
            {
                if (isPainted[fi]) score[fi] = splatValue;
            });
        }
    }

    /// <summary>
    /// Stray ink deep in the other team's territory: a trail (a player running through) or splotches
    /// (bombs). Each team rolls independently, so either, both or neither may have some. Stamped
    /// pixels are forced to that team's side; the final quantile keeps the shares exact.
    /// </summary>
    private static void ApplyStrayInk(Rng rng, GradientNoise breakup, double chance, float[] score, bool[] isPainted, List<int> floorPixels,
        int[] pixelToFloor, int pw, int ph, int p, float front)
    {
        foreach (var side in new[] { Side.Left, Side.Right })
        {
            if (!rng.Chance(chance)) continue;

            // Positive score = left, so left ink goes where the score is far below the front.
            double sign = side == Side.Left ? 1 : -1;
            float value = (float)(front + sign * SplatStrength);
            // Blotchy noise punches holes in the ink, so it interleaves with the surrounding color
            // like the fight zone instead of being a solid stamp.
            Action<int> paint = fi =>
            {
                if (!isPainted[fi]) return;
                int pi = floorPixels[fi];
                double gx = (pi % pw + 0.5) / p, gy = (pi / pw + 0.5) / p;
                if (breakup.Fbm(gx * StrayBreakupFrequency, gy * StrayBreakupFrequency, 3) > StrayBreakupCut) return;
                score[fi] = value;
            };

            bool trail = rng.Chance(0.5);
            int count = trail ? rng.Next(1, 3) : rng.Next(1, 4);
            for (int n = 0; n < count; n++)
            {
                int start = -1;
                for (int t = 0; t < 120; t++)
                {
                    int i = rng.Next(0, floorPixels.Count);
                    if (isPainted[i] && (front - score[i]) * sign > StrayMinDepth)
                    {
                        start = floorPixels[i];
                        break;
                    }
                }
                if (start < 0) continue;
                double x = start % pw, y = start / pw;

                if (!trail)
                {
                    StampSplat(rng, x, y, rng.Range(0.9, 1.8) * p, 4, 9, pw, ph, pixelToFloor, paint);
                    continue;
                }

                // A wandering line of overlapping blobs; it turns away from walls and the void.
                double heading = rng.Range(0, 2 * Math.PI);
                double width = rng.Range(0.55, 0.85) * p;
                int steps = (int)(rng.Range(6, 14) * p / (0.3 * p));
                for (int s = 0; s < steps; s++)
                {
                    heading += rng.Range(-0.35, 0.35);
                    double nx = x + Math.Cos(heading) * 0.3 * p, ny = y + Math.Sin(heading) * 0.3 * p;
                    for (int tries = 0; tries < 8 && !IsFloor(nx, ny); tries++)
                    {
                        heading += rng.Range(0.6, 1.4) * (rng.Chance(0.5) ? 1 : -1);
                        nx = x + Math.Cos(heading) * 0.3 * p;
                        ny = y + Math.Sin(heading) * 0.3 * p;
                    }
                    if (!IsFloor(nx, ny)) break;
                    x = nx; y = ny;
                    StampSplat(rng, x, y, width, 0, 1, pw, ph, pixelToFloor, paint);
                }
            }
        }

        bool IsFloor(double fx, double fy) =>
            fx >= 0 && fy >= 0 && fx < pw && fy < ph && pixelToFloor[(int)fy * pw + (int)fx] >= 0;
    }

    /// <summary>
    /// Small paint splats and sputter droplets landing in the unpainted floor near spawns, just
    /// past the estimated unpainted border. Stamped pixels are forced painted.
    /// </summary>
    private static void ApplySpawnSplats(Rng rng, float[] u, float[] closeness, float border, List<int> floorPixels, int[] pixelToFloor, int pw, int ph, int p)
    {
        int count = rng.Next(14, 26);
        for (int n = 0; n < count; n++)
        {
            int centre = -1;
            for (int t = 0; t < 80; t++)
            {
                int i = rng.Next(0, floorPixels.Count);
                if (closeness[i] >= SpawnSplatMinCloseness && u[i] >= border && u[i] - border < SpawnSplatBand)
                {
                    centre = floorPixels[i];
                    break;
                }
            }
            if (centre < 0) continue;

            StampSplat(rng, centre % pw, centre / pw, rng.Range(0.35, 0.9) * p, 3, 8, pw, ph, pixelToFloor, fi =>
            {
                u[fi] = float.NegativeInfinity;
            });
        }
    }

    /// <summary>One splat: a lobed blob plus a few droplets around it. Calls <paramref name="apply"/> per floor pixel hit.</summary>
    private static void StampSplat(Rng rng, double cx, double cy, double radius, int minDroplets, int maxDroplets,
        int pw, int ph, int[] pixelToFloor, Action<int> apply)
    {
        int lobes = rng.Next(3, 7);
        double phase = rng.Range(0, 2 * Math.PI);
        Stamp(cx, cy, radius, lobes, phase, 0.18);

        int droplets = rng.Next(minDroplets, maxDroplets);
        for (int d = 0; d < droplets; d++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double dist = radius * rng.Range(1.3, 2.1);
            Stamp(cx + Math.Cos(ang) * dist, cy + Math.Sin(ang) * dist, radius * rng.Range(0.15, 0.32), 0, 0, 0);
        }

        void Stamp(double sx, double sy, double r, int lobeCount, double lobePhase, double wobble)
        {
            double maxR = r * (1 + wobble);
            int x0 = Math.Max(0, (int)(sx - maxR)), x1 = Math.Min(pw - 1, (int)(sx + maxR));
            int y0 = Math.Max(0, (int)(sy - maxR)), y1 = Math.Min(ph - 1, (int)(sy + maxR));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    double dx = x - sx, dy = y - sy;
                    double limit = r * (1 + wobble * Math.Sin(lobeCount * Math.Atan2(dy, dx) + lobePhase));
                    if (dx * dx + dy * dy > limit * limit) continue;
                    int fi = pixelToFloor[y * pw + x];
                    if (fi >= 0) apply(fi);
                }
            }
        }
    }

    /// <summary>The <paramref name="k"/>-th largest value (1-based).</summary>
    private static float KthLargest(float[] values, int k)
    {
        var copy = (float[])values.Clone();
        Array.Sort(copy);
        return copy[Math.Clamp(copy.Length - k, 0, copy.Length - 1)];
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
