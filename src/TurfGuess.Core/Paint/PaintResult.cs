namespace TurfGuess.Core.Paint;

/// <summary>What a single pixel of the final mask is.</summary>
public enum PixelKind : byte
{
    Void = 0,
    Wall = 1,
    Unpainted = 2,
    Left = 3,
    Right = 4,
    SpawnLeft = 5,
    SpawnRight = 6,
}

/// <summary>
/// The final paint mask at render resolution. This is the single source of truth for both
/// the score and the renderer (DESIGN.md §4.2 step 7).
/// </summary>
public sealed class PaintResult
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int PixelsPerCell { get; init; }
    public required PixelKind[] Mask { get; init; }

    public PixelKind this[int x, int y] => Mask[y * Width + x];
}
