using TurfGuess.Core.Scoring;

namespace TurfGuess.Core.Tests;

public class DifficultyRankTests
{
    [Theory]
    [InlineData(0.5001, DifficultyRank.X)]
    [InlineData(0.5119, DifficultyRank.X)]
    [InlineData(0.5120, DifficultyRank.S)]
    [InlineData(0.5189, DifficultyRank.S)]
    [InlineData(0.5190, DifficultyRank.A)]
    [InlineData(0.5259, DifficultyRank.A)]
    [InlineData(0.5260, DifficultyRank.B)]
    [InlineData(0.5329, DifficultyRank.B)]
    [InlineData(0.5330, DifficultyRank.C)]
    [InlineData(0.6000, DifficultyRank.C)]
    public void Closer_splits_rank_higher(double winnerShare, DifficultyRank expected) =>
        Assert.Equal(expected, Difficulty.RankFor(winnerShare));
}
