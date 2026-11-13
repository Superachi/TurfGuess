namespace TurfGuess.Core.Scoring;

/// <summary>How hard a round is to call, from easiest (C) to hardest (X).</summary>
public enum DifficultyRank
{
    C,
    B,
    A,
    S,
    X,
}

public static class Difficulty
{
    /// <summary>
    /// The rank for a winner share of the painted floor, as a fraction (0.5–1). The configured
    /// [MinWinnerShare, MaxWinnerShare] range is cut into five equal bands, X (closest) to C
    /// (widest), so each rank comes up about one round in five whatever the range is. With the
    /// default 50.3–52.3% range the bands are 0.4 points wide.
    /// </summary>
    public static DifficultyRank RankFor(double winnerShare, DifficultyOptions range)
    {
        double min = range.MinWinnerShare / 100.0, max = range.MaxWinnerShare / 100.0;
        if (max <= min) return DifficultyRank.A; // a single fixed margin: the middle rank

        // 0 = closest possible, 1 = widest; rounded so a share exactly on a band edge isn't pushed
        // to the wrong side by floating-point error.
        double t = Math.Round((winnerShare - min) / (max - min), 9);
        return t switch
        {
            < 0.2 => DifficultyRank.X,
            < 0.4 => DifficultyRank.S,
            < 0.6 => DifficultyRank.A,
            < 0.8 => DifficultyRank.B,
            _ => DifficultyRank.C,
        };
    }

    /// <summary>Points a round of this rank is worth: C = 1 … X = 5.</summary>
    public static int Points(this DifficultyRank rank) => (int)rank + 1;

    /// <summary>The rank for the default difficulty range.</summary>
    public static DifficultyRank RankFor(double winnerShare) => RankFor(winnerShare, new DifficultyOptions());

    /// <summary>The rank of a counted round.</summary>
    public static DifficultyRank Rank(this ScoreResult score, DifficultyOptions range) => RankFor(score.WinnerShare, range);
}
