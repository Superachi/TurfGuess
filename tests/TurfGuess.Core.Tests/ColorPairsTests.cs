using SkiaSharp;
using TurfGuess.Core.Render;

namespace TurfGuess.Core.Tests;

public class ColorPairsTests
{
    [Fact]
    public void Default_pairs_are_valid_and_distinct_within_each_pair()
    {
        Assert.Equal(10, ColorPairs.Default.Count);
        foreach (var pair in ColorPairs.Default)
        {
            Assert.True(SKColor.TryParse(pair.A.Color, out _), pair.A.Color);
            Assert.True(SKColor.TryParse(pair.B.Color, out _), pair.B.Color);
            Assert.NotEqual(pair.A.Name, pair.B.Name);
            Assert.NotEqual(pair.A.Emoji, pair.B.Emoji);
            Assert.False(string.IsNullOrWhiteSpace(pair.A.Emoji));
            Assert.False(string.IsNullOrWhiteSpace(pair.B.Emoji));
        }
    }

    [Fact]
    public void Pick_is_deterministic_per_seed()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var a = ColorPairs.Pick(seed);
            var b = ColorPairs.Pick(seed);
            Assert.Same(a.Left, b.Left);
            Assert.Same(a.Right, b.Right);
        }
    }

    [Fact]
    public void Pick_uses_many_pairs_and_both_sides()
    {
        var picks = Enumerable.Range(0, 400).Select(i => ColorPairs.Pick((ulong)i)).ToList();
        int distinctPairs = picks.Select(s => string.Join("|", new[] { s.Left.Color, s.Right.Color }.Order())).Distinct().Count();
        Assert.Equal(ColorPairs.Default.Count, distinctPairs);

        // The same pair shows up with its colors on either side.
        var first = ColorPairs.Default[0];
        Assert.Contains(picks, s => s.Left == first.A);
        Assert.Contains(picks, s => s.Left == first.B);
    }

    [Fact]
    public void Pick_rejects_empty_list() =>
        Assert.Throws<ArgumentException>(() => ColorPairs.Pick(1, []));
}
