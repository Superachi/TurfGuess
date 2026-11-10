namespace TurfGuess.Core.Grid;

public enum SymmetryMode
{
    /// <summary>180° rotation around the map center. No peek-side advantage (default).</summary>
    Rotate,

    /// <summary>Reflection across the vertical center line.</summary>
    Mirror,
}

public static class Symmetry
{
    /// <summary>Maps a cell to its symmetric counterpart.</summary>
    public static (int X, int Y) Map(int x, int y, int width, int height, SymmetryMode mode) =>
        mode == SymmetryMode.Rotate ? (width - 1 - x, height - 1 - y) : (width - 1 - x, y);

    public static CellType Swap(CellType t) => t switch
    {
        CellType.SpawnLeft => CellType.SpawnRight,
        CellType.SpawnRight => CellType.SpawnLeft,
        _ => t,
    };

    /// <summary>Overwrites the right half of the grid with the transformed left half.</summary>
    public static void Apply(Grid<CellType> grid, SymmetryMode mode)
    {
        if (grid.Width % 2 != 0) throw new ArgumentException("Grid width must be even.", nameof(grid));
        int half = grid.Width / 2;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < half; x++)
            {
                var (mx, my) = Map(x, y, grid.Width, grid.Height, mode);
                grid[mx, my] = Swap(grid[x, y]);
            }
        }
    }
}
