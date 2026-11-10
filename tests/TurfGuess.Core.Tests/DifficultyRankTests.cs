using TurfGuess.Core.Scoring;

namespace TurfGuess.Core.Tests;

public class DifficultyRankTests
{
    [Theory]
    [InlineData(0.5001, DifficultyRank.X)]
    [InlineData(0.5069, DifficultyRank.X)]
    [InlineData(0.5070, DifficultyRank.S)]
    [InlineData(0.5109, DifficultyRank.S)]
    [InlineData(0.5110, DifficultyRank.A)]
    [InlineData(0.5149, DifficultyRank.A)]
    [InlineData(0.5150, DifficultyRank.B)]
    [InlineData(0.5189, DifficultyRank.B)]
    [InlineData(0.5190, DifficultyRank.C)]
    [InlineData(0.6000, DifficultyRank.C)]
    public void Closer_splits_rank_higher(double winnerShare, DifficultyRank expected) =>
        Assert.Equal(expected, Difficulty.RankFor(winnerShare));
}
