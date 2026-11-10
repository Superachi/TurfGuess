using TurfGuess.Core.Grid;

namespace TurfGuess.Core.MapGen;

/// <summary>A validated, symmetric arena.</summary>
public sealed class GameMap
{
    public required Grid<CellType> Cells { get; init; }
    public required SymmetryMode Symmetry { get; init; }
    public required RectI SpawnLeft { get; init; }
    public required RectI SpawnRight { get; init; }

    /// <summary>Walkable cells (floor + spawn pads).</summary>
    public required int WalkableCells { get; init; }

    /// <summary>Rows where floor crosses the vertical center line.</summary>
    public required int CenterCrossingRows { get; init; }

    /// <summary>Which sub-seed attempt produced this map (0-based).</summary>
    public required int Attempt { get; init; }

    public int Width => Cells.Width;
    public int Height => Cells.Height;

    public RectI Spawn(Side side) => side == Side.Left ? SpawnLeft : SpawnRight;
}
