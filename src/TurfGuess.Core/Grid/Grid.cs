namespace TurfGuess.Core.Grid;

/// <summary>A rectangular grid stored as a flat array, indexed <c>y * Width + x</c>.</summary>
public sealed class Grid<T>
{
    public Grid(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Cells = new T[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public T[] Cells { get; }

    public T this[int x, int y]
    {
        get => Cells[y * Width + x];
        set => Cells[y * Width + x] = value;
    }

    public int Index(int x, int y) => y * Width + x;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public void Fill(RectI rect, T value)
    {
        int x0 = Math.Max(0, rect.X), y0 = Math.Max(0, rect.Y);
        int x1 = Math.Min(Width, rect.Right), y1 = Math.Min(Height, rect.Bottom);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                Cells[y * Width + x] = value;
    }
}

public enum CellType : byte
{
    Void = 0,
    Floor = 1,
    Wall = 2,
    SpawnLeft = 3,
    SpawnRight = 4,
}

public enum Side : byte
{
    Left = 0,
    Right = 1,
}

public static class CellTypeExtensions
{
    /// <summary>Paint (and the paint flood) can travel through this cell.</summary>
    public static bool IsWalkable(this CellType t) => t is CellType.Floor or CellType.SpawnLeft or CellType.SpawnRight;
}

public static class SideExtensions
{
    public static Side Opposite(this Side side) => side == Side.Left ? Side.Right : Side.Left;
}

/// <summary>Integer rectangle in grid cells. <see cref="Right"/> and <see cref="Bottom"/> are exclusive.</summary>
public readonly record struct RectI(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public int CenterX => X + W / 2;
    public int CenterY => Y + H / 2;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public RectI Inflate(int by) => new(X - by, Y - by, W + 2 * by, H + 2 * by);

    public bool Intersects(RectI o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
}
