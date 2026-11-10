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
    /// Upper bounds of the winner's share of the painted floor (exclusive) for X, S, A and B.
    /// Anything at or above the last bound is C. Equal 0.7-point bands over the default
    /// 50.5–54% range, so with the default settings each rank comes up about one round in five.
    /// Fixed rather than derived from the configured range, so a rank always means the same split.
    /// </summary>
    private static readonly (double MaxShare, DifficultyRank Rank)[] Bands =
    [
        (0.512, DifficultyRank.X),
        (0.519, DifficultyRank.S),
        (0.526, DifficultyRank.A),
        (0.533, DifficultyRank.B),
    ];

    /// <summary>The rank for a winner share of the painted floor, as a fraction (0.5–1).</summary>
    public static DifficultyRank RankFor(double winnerShare)
    {
        foreach (var (max, rank) in Bands)
            if (winnerShare < max) return rank;
        return DifficultyRank.C;
    }

    /// <summary>The rank of a counted round.</summary>
    public static DifficultyRank Rank(this ScoreResult score) => RankFor(score.WinnerShare);
}
