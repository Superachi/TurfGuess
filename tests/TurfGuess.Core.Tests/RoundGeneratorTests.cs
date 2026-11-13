using TurfGuess.Core.Grid;
using TurfGuess.Core.Paint;
using TurfGuess.Core.Render;
using TurfGuess.Core.Scoring;

namespace TurfGuess.Core.Tests;

public class RoundGeneratorTests
{
    // Lower resolution keeps the suite fast; the maths is resolution-independent.
    private static GeneratorOptions Fast() => new() { Paint = { PixelsPerCell = 4 } };

    public static TheoryData<ulong> Seeds => new() { 1, 2, 3, 5, 8, 13, 21, 34, 55, 89 };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Winner_share_counted_from_mask_matches_target(ulong seed)
    {
        var round = RoundGenerator.Generate(seed, Fast());
        var s = round.Score;

        Assert.Equal(round.Winner, s.Winner);
        // Exact to one pixel (rounding of nLeft).
        Assert.InRange(Math.Abs(s.WinnerShare - round.TargetWinnerShare), 0, 1.0 / s.PaintedPixels);
        Assert.InRange(s.WinnerShare * 100, Fast().Difficulty.MinWinnerShare - 0.01, Fast().Difficulty.MaxWinnerShare + 0.01);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Unpainted_share_matches_target_and_range(ulong seed)
    {
        var o = Fast();
        var round = RoundGenerator.Generate(seed, o);
        var s = round.Score;

        Assert.InRange(Math.Abs(s.UnpaintedShare - round.TargetUnpaintedShare), 0, 1.0 / s.FloorPixels);
        Assert.InRange(s.UnpaintedShare, o.Paint.UnpaintedMin - 0.001, o.Paint.UnpaintedMax + 0.001);
    }

    [Fact]
    public void Same_seed_gives_identical_mask()
    {
        var a = RoundGenerator.Generate(77, Fast());
        var b = RoundGenerator.Generate(77, Fast());
        Assert.Equal(a.Paint.Mask, b.Paint.Mask);
        Assert.Equal(a.Score, b.Score);
    }

    [Fact]
    public void Both_winners_occur_across_seeds()
    {
        var winners = Enumerable.Range(1, 20).Select(i => RoundGenerator.Generate((ulong)i, Fast()).Winner).ToHashSet();
        Assert.Contains(Side.Left, winners);
        Assert.Contains(Side.Right, winners);
    }

    [Fact]
    public void Coverage_adds_up_and_spawns_and_walls_do_not_count()
    {
        var round = RoundGenerator.Generate(3, Fast());
        var s = round.Score;
        Assert.Equal(1.0, s.CoverageLeft + s.CoverageRight + s.UnpaintedShare, 9);

        long nonFloor = round.Paint.Mask.Count(k => k is PixelKind.Wall or PixelKind.Void or PixelKind.SpawnLeft or PixelKind.SpawnRight);
        Assert.Equal(round.Paint.Mask.Length, s.FloorPixels + nonFloor);
    }

    [Fact]
    public void Score_is_recounted_identically_from_mask()
    {
        var round = RoundGenerator.Generate(9, Fast());
        Assert.Equal(round.Score, Score.Count(round.Paint));
    }

    [Fact]
    public void Reroll_never_makes_a_round_easier_and_is_deterministic()
    {
        var off = Fast();
        off.Difficulty.RerollsC = off.Difficulty.RerollsB = off.Difficulty.RerollsA = off.Difficulty.RerollsS = 0;

        for (ulong seed = 1; seed <= 12; seed++)
        {
            var plain = RoundGenerator.Generate(seed, off);
            var rerolled = RoundGenerator.Generate(seed, Fast());

            Assert.Equal(0, plain.Rerolls);
            Assert.True(rerolled.Score.Rank(Fast().Difficulty) >= plain.Score.Rank(off.Difficulty));
            Assert.Equal(rerolled.Score, RoundGenerator.Generate(seed, Fast()).Score);
        }
    }

    [Fact]
    public void Reroll_count_respects_the_limit_of_the_best_rank()
    {
        var o = Fast();
        o.Difficulty.RerollsC = 10; o.Difficulty.RerollsB = 8; o.Difficulty.RerollsA = 5; o.Difficulty.RerollsS = 3;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var round = RoundGenerator.Generate(seed, o);
            Assert.InRange(round.Rerolls, 0, 10);
        }
        Assert.Equal(10, o.Difficulty.RerollLimit(DifficultyRank.C));
        Assert.Equal(0, o.Difficulty.RerollLimit(DifficultyRank.X));
    }

    [Theory]
    [InlineData(50.0, 54.0)]
    [InlineData(57.0, 52.0)]
    public void Invalid_difficulty_is_rejected(double min, double max)
    {
        var o = Fast();
        o.Difficulty.MinWinnerShare = min;
        o.Difficulty.MaxWinnerShare = max;
        Assert.Throws<ArgumentException>(() => RoundGenerator.Generate(1, o));
    }

    [Fact]
    public void Renderer_produces_png_of_expected_size()
    {
        var round = RoundGenerator.Generate(4, Fast());
        using var roundImage = MapRenderer.RenderRound(round, new RenderStyle(), "Test");
        using var revealImage = MapRenderer.RenderReveal(round, new RenderStyle());

        Assert.Equal(round.Paint.Width, roundImage.Width);
        Assert.Equal(round.Paint.Height, roundImage.Height);
        Assert.True(revealImage.Height > roundImage.Height);

        var png = MapRenderer.EncodePng(revealImage);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }
}
