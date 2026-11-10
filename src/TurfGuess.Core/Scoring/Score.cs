using TurfGuess.Core.Grid;
using TurfGuess.Core.Paint;

namespace TurfGuess.Core.Scoring;

/// <summary>Coverage counted from the final paint mask (DESIGN.md §4.1).</summary>
public sealed record ScoreResult(long LeftPixels, long RightPixels, long UnpaintedPixels)
{
    public long FloorPixels => LeftPixels + RightPixels + UnpaintedPixels;
    public long PaintedPixels => LeftPixels + RightPixels;

    public Side Winner => LeftPixels > RightPixels ? Side.Left : Side.Right;

    /// <summary>max(L, R) / (L + R): share of the PAINTED floor. This is the difficulty value.</summary>
    public double WinnerShare => Math.Max(LeftPixels, RightPixels) / (double)PaintedPixels;

    /// <summary>Share of TOTAL floor, as shown at reveal.</summary>
    public double CoverageLeft => LeftPixels / (double)FloorPixels;
    public double CoverageRight => RightPixels / (double)FloorPixels;
    public double UnpaintedShare => UnpaintedPixels / (double)FloorPixels;

    public double Coverage(Side side) => side == Side.Left ? CoverageLeft : CoverageRight;
}

public static class Score
{
    public static ScoreResult Count(PaintResult paint)
    {
        long l = 0, r = 0, u = 0;
        foreach (var kind in paint.Mask)
        {
            switch (kind)
            {
                case PixelKind.Left: l++; break;
                case PixelKind.Right: r++; break;
                case PixelKind.Unpainted: u++; break;
            }
        }
        return new ScoreResult(l, r, u);
    }
}
