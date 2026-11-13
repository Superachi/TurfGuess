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

    // Bands scale with the configured range: 50.05–51.0 gives five bands of 0.19 points.
    [Theory]
    [InlineData(0.50051, DifficultyRank.X)]
    [InlineData(0.50239, DifficultyRank.X)]
    [InlineData(0.50241, DifficultyRank.S)]
    [InlineData(0.50431, DifficultyRank.A)]
    [InlineData(0.50621, DifficultyRank.B)]
    [InlineData(0.50811, DifficultyRank.C)]
    [InlineData(0.50999, DifficultyRank.C)]
    public void Bands_scale_with_the_configured_range(double winnerShare, DifficultyRank expected) =>
        Assert.Equal(expected, Difficulty.RankFor(winnerShare, new DifficultyOptions { MinWinnerShare = 50.05, MaxWinnerShare = 51.0 }));

    [Fact]
    public void A_single_fixed_margin_ranks_in_the_middle() =>
        Assert.Equal(DifficultyRank.A, Difficulty.RankFor(0.505, new DifficultyOptions { MinWinnerShare = 50.5, MaxWinnerShare = 50.5 }));
}
