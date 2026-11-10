using TurfGuess.Core.Grid;
using TurfGuess.Core.Util;

namespace TurfGuess.Core.MapGen;

public sealed class MapGenerationException(string message) : Exception(message);

/// <summary>
/// Builds a simple, blocky, symmetric arena on a cell grid (DESIGN.md §3):
/// one solid body made of a few large overlapping rectangles (spine + wings + center plaza),
/// a couple of holes, and a handful of chunky obstacles. Then symmetry and validation.
/// </summary>
public static class MapGenerator
{
    /// <summary>Layout shapes snap to multiples of this many cells, which keeps outlines clean.</summary>
    private const int Snap = 2;

    private const int MinCrossingRows = 4;
    private const int MaxCrossingRuns = 5;

    public static GameMap Generate(ulong seed, MapOptions options)
    {
        string? lastReason = null;
        for (int attempt = 0; attempt < options.MaxAttempts; attempt++)
        {
            var rng = new Rng(Rng.Derive(seed, (ulong)attempt));
            var map = TryGenerate(rng, options, attempt, out lastReason);
            if (map is not null) return map;
        }

        throw new MapGenerationException(
            $"No valid map after {options.MaxAttempts} attempts (last rejection: {lastReason}).");
    }

    internal static GameMap? TryGenerate(Rng rng, MapOptions o, int attempt, out string? rejectReason)
    {
        int w = o.GridWidth, h = o.GridHeight, half = w / 2;
        var g = new Grid<CellType>(w, h);

        // The left half, keeping a one-cell void border around the outside of the map.
        var bounds = new RectI(1, 1, half - 1, h - 2);

        // --- Spine: one wide band from the left edge to the center line. ----------------
        //     Offsetting it from the middle row makes the rotated halves interlock instead of
        //     forming one straight bar; the overlap with its rotated twin is the main crossing.
        int spineH = SnapUp(rng.Next(h * 30 / 100, h * 48 / 100));
        int offset = SnapDown(rng.Next(-h / 9, h / 9 + 1));
        int spineY = h / 2 - spineH / 2 + offset;
        var spine = Clip(new RectI(1, spineY, half - 1, spineH), bounds);
        Carve(g, spine);

        // --- Wings: 2–4 big rectangles hanging off the top or bottom of the spine. --------
        int wings = rng.Next(2, 5);
        for (int i = 0; i < wings; i++)
        {
            int ww = SnapUp(rng.Next(half * 20 / 100, half * 45 / 100));
            int wh = SnapUp(rng.Next(h * 15 / 100, h * 35 / 100));
            int wx = SnapDown(rng.Next(1, Math.Max(2, half - ww)));
            int overlap = SnapUp(rng.Next(4, Math.Max(5, wh / 2)));
            bool top = i % 2 == 0 ? rng.Chance(0.6) : rng.Chance(0.4);
            int wy = top ? spine.Y - wh + overlap : spine.Bottom - overlap;
            Carve(g, Clip(new RectI(wx, wy, ww, wh), bounds));
        }

        // --- Center plaza (most maps): symmetric around the middle row, so it always crosses
        //     the center line in both symmetry modes.
        if (rng.Chance(0.7))
        {
            int a = SnapUp(rng.Next(h * 12 / 100, h * 22 / 100));
            int plazaW = SnapUp(rng.Next(6, 17));
            Carve(g, Clip(new RectI(half - plazaW, h / 2 - a, plazaW, 2 * a + h % 2), bounds));
        }

        // --- Spawn pad + platform, inside the spine.
        const int padW = 3, padH = 5;
        int padY = Math.Clamp(spine.CenterY - padH / 2 + rng.Next(-2, 3), spine.Y + 1, spine.Bottom - padH - 1);
        var pad = new RectI(2, padY, padW, padH);
        var platform = Clip(new RectI(1, padY - 3, 9, padH + 6), bounds);
        Carve(g, platform);

        // --- Holes: 0–2 sizeable rectangular gaps (pits or notches in the outline).
        int holes = rng.Next(0, 3);
        for (int i = 0; i < holes; i++)
        {
            var hole = new RectI(
                SnapDown(rng.Next(bounds.X + 8, bounds.Right - 4)),
                SnapDown(rng.Next(bounds.Y, bounds.Bottom - 4)),
                SnapUp(rng.Next(4, 9)),
                SnapUp(rng.Next(4, 9)));
            hole = Clip(hole, bounds);
            if (hole.Intersects(platform.Inflate(2))) continue;
            g.Fill(hole, CellType.Void);
        }

        // --- Obstacles: a handful of chunky blocks and the odd straight wall.
        PlaceObstacles(g, rng, o.ObstacleDensity, o.MaxObstaclesPerHalf, half, platform.Inflate(1));

        // --- Spawn pad on top.
        g.Fill(pad, CellType.SpawnLeft);

        // --- Symmetry.
        Symmetry.Apply(g, o.Symmetry);
        var spawnRight = MapRect(pad, w, h, o.Symmetry);

        // --- Validation.
        var reached = FloodFrom(g, pad);
        if (!reached[g.Index(spawnRight.X, spawnRight.Y)])
        {
            rejectReason = "spawns not connected";
            return null;
        }

        RemoveUnreached(g, reached);

        int walkable = 0;
        foreach (var c in g.Cells) if (c.IsWalkable()) walkable++;
        double floorFraction = walkable / (double)(w * h);
        if (floorFraction < o.MinFloor || floorFraction > o.MaxFloor)
        {
            rejectReason = $"floor fraction {floorFraction:P1} outside [{o.MinFloor:P0}, {o.MaxFloor:P0}]";
            return null;
        }

        var (crossRows, crossRuns) = CenterCrossings(g);
        if (crossRows < MinCrossingRows || crossRuns > MaxCrossingRuns)
        {
            rejectReason = $"center crossings rows={crossRows} runs={crossRuns}";
            return null;
        }

        rejectReason = null;
        return new GameMap
        {
            Cells = g,
            Symmetry = o.Symmetry,
            SpawnLeft = pad,
            SpawnRight = spawnRight,
            WalkableCells = walkable,
            CenterCrossingRows = crossRows,
            Attempt = attempt,
        };
    }

    private static int SnapDown(int v) => (int)Math.Floor(v / (double)Snap) * Snap;
    private static int SnapUp(int v) => (int)Math.Ceiling(v / (double)Snap) * Snap;

    private static void Carve(Grid<CellType> g, RectI r)
    {
        for (int y = Math.Max(0, r.Y); y < Math.Min(g.Height, r.Bottom); y++)
            for (int x = Math.Max(0, r.X); x < Math.Min(g.Width, r.Right); x++)
                if (g[x, y] == CellType.Void) g[x, y] = CellType.Floor;
    }

    private static RectI Clip(RectI r, RectI bounds)
    {
        int x0 = Math.Max(r.X, bounds.X), y0 = Math.Max(r.Y, bounds.Y);
        int x1 = Math.Min(r.Right, bounds.Right), y1 = Math.Min(r.Bottom, bounds.Bottom);
        return new RectI(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    // ---------------------------------------------------------------------------------------
    // Obstacles
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Places a few solid rectangles fully inside open floor: each needs a one-cell ring of floor
    /// around it (no slivers against the outline) and a clear gap to other obstacles.
    /// </summary>
    private static void PlaceObstacles(Grid<CellType> g, Rng rng, double density, int maxObstacles, int half, RectI keepClear)
    {
        int floor = 0;
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < half; x++)
                if (g[x, y] == CellType.Floor) floor++;

        int targetCells = (int)(density * floor);
        int placedCells = 0, placed = 0;
        for (int tries = 0; placedCells < targetCells && placed < maxObstacles && tries < 500; tries++)
        {
            RectI block;
            if (rng.Chance(0.25))
            {
                // Straight wall, 1–2 thick.
                int len = rng.Next(4, 9), thick = rng.Next(1, 3);
                block = rng.Chance(0.5) ? new RectI(0, 0, len, thick) : new RectI(0, 0, thick, len);
            }
            else
            {
                // Chunky block.
                block = new RectI(0, 0, rng.Next(2, 5), rng.Next(2, 5));
            }
            block = block with { X = rng.Next(2, half - block.W), Y = rng.Next(2, g.Height - 2 - block.H) };

            if (block.Intersects(keepClear)) continue;
            if (!AllFloor(g, block.Inflate(1), half)) continue;          // interior only
            if (AnyWall(g, block.Inflate(3))) continue;                   // spacing between obstacles

            g.Fill(block, CellType.Wall);
            placedCells += block.W * block.H;
            placed++;
        }
    }

    private static bool AllFloor(Grid<CellType> g, RectI r, int half)
    {
        if (r.X < 0 || r.Y < 0 || r.Right > half || r.Bottom > g.Height) return false;
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
                if (g[x, y] != CellType.Floor) return false;
        return true;
    }

    private static bool AnyWall(Grid<CellType> g, RectI r)
    {
        for (int y = Math.Max(0, r.Y); y < Math.Min(g.Height, r.Bottom); y++)
            for (int x = Math.Max(0, r.X); x < Math.Min(g.Width, r.Right); x++)
                if (g[x, y] == CellType.Wall) return true;
        return false;
    }

    // ---------------------------------------------------------------------------------------
    // Validation helpers
    // ---------------------------------------------------------------------------------------

    private static RectI MapRect(RectI r, int w, int h, SymmetryMode mode)
    {
        var (x0, y0) = Symmetry.Map(r.X, r.Y, w, h, mode);
        var (x1, y1) = Symmetry.Map(r.Right - 1, r.Bottom - 1, w, h, mode);
        int minX = Math.Min(x0, x1), minY = Math.Min(y0, y1);
        return new RectI(minX, minY, Math.Abs(x1 - x0) + 1, Math.Abs(y1 - y0) + 1);
    }

    /// <summary>4-connected flood fill over walkable cells, starting from every cell of <paramref name="start"/>.</summary>
    internal static bool[] FloodFrom(Grid<CellType> g, RectI start)
    {
        var reached = new bool[g.Cells.Length];
        var queue = new Queue<int>();
        for (int y = start.Y; y < start.Bottom; y++)
        {
            for (int x = start.X; x < start.Right; x++)
            {
                int i = g.Index(x, y);
                if (g.Cells[i].IsWalkable() && !reached[i])
                {
                    reached[i] = true;
                    queue.Enqueue(i);
                }
            }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % g.Width, y = i / g.Width;
            TryVisit(x + 1, y);
            TryVisit(x - 1, y);
            TryVisit(x, y + 1);
            TryVisit(x, y - 1);
        }

        return reached;

        void TryVisit(int x, int y)
        {
            if (!g.InBounds(x, y)) return;
            int j = g.Index(x, y);
            if (reached[j] || !g.Cells[j].IsWalkable()) return;
            reached[j] = true;
            queue.Enqueue(j);
        }
    }

    /// <summary>
    /// Turns unreachable floor into void, and drops obstacles that no longer touch reachable floor.
    /// A wall is kept when it is connected (through other walls) to a wall bordering reachable floor,
    /// so the inner cells of a big block survive.
    /// </summary>
    private static void RemoveUnreached(Grid<CellType> g, bool[] reached)
    {
        for (int i = 0; i < g.Cells.Length; i++)
            if (g.Cells[i].IsWalkable() && !reached[i]) g.Cells[i] = CellType.Void;

        var keep = new bool[g.Cells.Length];
        var queue = new Queue<int>();
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                if (g[x, y] != CellType.Wall) continue;
                bool touchesFloor = false;
                for (int dy = -1; dy <= 1 && !touchesFloor; dy++)
                    for (int dx = -1; dx <= 1 && !touchesFloor; dx++)
                        if (g.InBounds(x + dx, y + dy) && g[x + dx, y + dy].IsWalkable()) touchesFloor = true;
                if (!touchesFloor) continue;
                int i = g.Index(x, y);
                keep[i] = true;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % g.Width, y = i / g.Width;
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (!g.InBounds(nx, ny)) continue;
                int j = g.Index(nx, ny);
                if (keep[j] || g.Cells[j] != CellType.Wall) continue;
                keep[j] = true;
                queue.Enqueue(j);
            }
        }

        for (int i = 0; i < g.Cells.Length; i++)
            if (g.Cells[i] == CellType.Wall && !keep[i]) g.Cells[i] = CellType.Void;
    }

    private static (int Rows, int Runs) CenterCrossings(Grid<CellType> g)
    {
        int half = g.Width / 2;
        int rows = 0, runs = 0;
        bool inRun = false;
        for (int y = 0; y < g.Height; y++)
        {
            bool crossing = g[half - 1, y].IsWalkable() && g[half, y].IsWalkable();
            if (crossing)
            {
                rows++;
                if (!inRun) runs++;
            }
            inRun = crossing;
        }
        return (rows, runs);
    }
}
