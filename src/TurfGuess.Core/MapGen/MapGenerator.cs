using TurfGuess.Core.Grid;
using TurfGuess.Core.Util;

namespace TurfGuess.Core.MapGen;

public sealed class MapGenerationException(string message) : Exception(message);

/// <summary>
/// Builds a blocky, symmetric arena on a cell grid (DESIGN.md §3):
/// rectangles + corridors for the left half, pits, grid-aligned obstacles,
/// then symmetry and validation.
/// </summary>
public static class MapGenerator
{
    private const int MaxRooms = 40;
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
        var rooms = new List<RectI>();

        // --- Spawn pad + platform -------------------------------------------------------
        const int padW = 3, padH = 5;
        int padY = rng.Next(h / 2 - padH - 4, h / 2 + 4);
        var pad = new RectI(2, padY, padW, padH);
        var platform = Clip(new RectI(1, padY - 3, 9, padH + 6), bounds);
        Carve(g, platform);
        rooms.Add(platform);

        // --- Center plaza: symmetric around the middle row, so it always crosses the center line
        //     in both symmetry modes.
        int a = rng.Next(3, 9);
        int plazaW = rng.Next(6, 15);
        var plaza = new RectI(half - plazaW, h / 2 - a, plazaW, 2 * a + h % 2);
        AddRoom(g, rng, rooms, plaza, bounds);

        // --- Paired seam lanes: lanes that end on the center line at row y AND at the mirrored row,
        //     so in rotate mode they connect to their rotated counterpart.
        int pairs = rng.Next(0, 3);
        for (int i = 0; i < pairs; i++)
        {
            int lw = rng.Next(2, 4);
            int maxY = plaza.Y - lw - 1;
            if (maxY <= 3) break;
            int y = rng.Next(3, maxY);
            int len = rng.Next(4, 11);
            AddRoom(g, rng, rooms, new RectI(half - len, y, len, lw), bounds);
            AddRoom(g, rng, rooms, new RectI(half - len, h - y - lw, len, lw), bounds);
        }

        // --- Random rooms until the half is full enough. Walls and pits will take some floor
        //     away later, so aim above the final target.
        double target = rng.Range(o.MinFloor + 0.03, Math.Max(o.MinFloor + 0.04, o.MaxFloor - 0.08)) + o.ObstacleDensity * 0.6;
        while (rooms.Count < MaxRooms && WalkableFraction(g, half) < target)
        {
            int rw = rng.Next(4, 15);
            int rh = rng.Next(4, 13);
            int rx = rng.Next(1, half - rw + 1);
            int ry = rng.Next(1, h - rh);
            AddRoom(g, rng, rooms, new RectI(rx, ry, rw, rh), bounds);
        }

        // --- Pits: rectangular holes that create chokepoints.
        int pits = rng.Next(0, 4);
        for (int i = 0; i < pits; i++)
        {
            var pit = new RectI(rng.Next(bounds.X, bounds.Right - 2), rng.Next(bounds.Y, bounds.Bottom - 2), rng.Next(2, 6), rng.Next(2, 6));
            pit = Clip(pit, bounds);
            if (pit.Intersects(platform.Inflate(2))) continue;
            g.Fill(pit, CellType.Void);
        }

        // --- Obstacles: grid-aligned pillars, segments, L- and T-shapes.
        PlaceObstacles(g, rng, o.ObstacleDensity, half, platform.Inflate(1));

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

    private static void AddRoom(Grid<CellType> g, Rng rng, List<RectI> rooms, RectI room, RectI bounds)
    {
        room = Clip(room, bounds);
        if (room.W <= 0 || room.H <= 0) return;
        Carve(g, room);
        if (rooms.Count > 0)
            Connect(g, rng, rooms[rng.Next(0, rooms.Count)], room, bounds);
        rooms.Add(room);
    }

    /// <summary>Carves an L-shaped corridor between the centers of two rooms.</summary>
    private static void Connect(Grid<CellType> g, Rng rng, RectI from, RectI to, RectI bounds)
    {
        int cw = rng.Next(2, 4);
        int ax = from.CenterX, ay = from.CenterY, bx = to.CenterX, by = to.CenterY;
        bool horizontalFirst = rng.Chance(0.5);
        int cornerX = horizontalFirst ? bx : ax;
        int cornerY = horizontalFirst ? ay : by;

        // Segment 1: (ax, ay) -> corner; segment 2: corner -> (bx, by).
        CarveSegment(g, ax, ay, cornerX, cornerY, cw, bounds);
        CarveSegment(g, cornerX, cornerY, bx, by, cw, bounds);
    }

    private static void CarveSegment(Grid<CellType> g, int x0, int y0, int x1, int y1, int width, RectI bounds)
    {
        int minX = Math.Min(x0, x1) - width / 2, maxX = Math.Max(x0, x1) - width / 2 + width;
        int minY = Math.Min(y0, y1) - width / 2, maxY = Math.Max(y0, y1) - width / 2 + width;
        Carve(g, Clip(new RectI(minX, minY, maxX - minX, maxY - minY), bounds));
    }

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

    private static double WalkableFraction(Grid<CellType> g, int half)
    {
        int n = 0;
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < half; x++)
                if (g[x, y].IsWalkable()) n++;
        return n / (double)(half * g.Height);
    }

    // ---------------------------------------------------------------------------------------
    // Obstacles
    // ---------------------------------------------------------------------------------------

    private static void PlaceObstacles(Grid<CellType> g, Rng rng, double density, int half, RectI keepClear)
    {
        int floor = 0;
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < half; x++)
                if (g[x, y] == CellType.Floor) floor++;

        int target = (int)(density * floor);
        int placed = 0;
        for (int tries = 0; placed < target && tries < 2000; tries++)
        {
            var shape = RandomShape(rng);
            int ox = rng.Next(1, half);
            int oy = rng.Next(1, g.Height - 1);

            bool ok = true;
            foreach (var (dx, dy) in shape)
            {
                int x = ox + dx, y = oy + dy;
                if (x < 1 || x >= half || y < 1 || y >= g.Height - 1 || g[x, y] != CellType.Floor || keepClear.Contains(x, y))
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;

            foreach (var (dx, dy) in shape) g[ox + dx, oy + dy] = CellType.Wall;
            placed += shape.Count;
        }
    }

    private static List<(int Dx, int Dy)> RandomShape(Rng rng)
    {
        var cells = new List<(int, int)>();
        switch (rng.Next(0, 5))
        {
            case 0: // pillar
                cells.Add((0, 0));
                break;
            case 1: // 2x2 block
                cells.AddRange([(0, 0), (1, 0), (0, 1), (1, 1)]);
                break;
            case 2: // straight segment
                for (int i = 0, n = rng.Next(3, 7); i < n; i++) cells.Add((i, 0));
                break;
            case 3: // L-shape
                for (int i = 0, n = rng.Next(3, 6); i < n; i++) cells.Add((i, 0));
                for (int i = 1, n = rng.Next(3, 6); i < n; i++) cells.Add((0, i));
                break;
            default: // T-shape
                int bar = rng.Chance(0.5) ? 3 : 5;
                for (int i = 0; i < bar; i++) cells.Add((i - bar / 2, 0));
                for (int i = 1, n = rng.Next(2, 4); i <= n; i++) cells.Add((0, i));
                break;
        }

        // Random rotation by k * 90°.
        int k = rng.Next(0, 4);
        for (int i = 0; i < cells.Count; i++)
        {
            var (x, y) = cells[i];
            for (int r = 0; r < k; r++) (x, y) = (-y, x);
            cells[i] = (x, y);
        }
        return cells;
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

    /// <summary>Turns unreachable floor into void, and drops walls that no longer border reachable floor.</summary>
    private static void RemoveUnreached(Grid<CellType> g, bool[] reached)
    {
        for (int i = 0; i < g.Cells.Length; i++)
            if (g.Cells[i].IsWalkable() && !reached[i]) g.Cells[i] = CellType.Void;

        var toVoid = new List<int>();
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                if (g[x, y] != CellType.Wall) continue;
                bool touchesFloor = false;
                for (int dy = -1; dy <= 1 && !touchesFloor; dy++)
                    for (int dx = -1; dx <= 1 && !touchesFloor; dx++)
                        if (g.InBounds(x + dx, y + dy) && g[x + dx, y + dy].IsWalkable()) touchesFloor = true;
                if (!touchesFloor) toVoid.Add(g.Index(x, y));
            }
        }
        foreach (int i in toVoid) g.Cells[i] = CellType.Void;
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
