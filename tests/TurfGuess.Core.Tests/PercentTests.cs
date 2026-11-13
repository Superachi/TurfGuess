using TurfGuess.Core.Util;

namespace TurfGuess.Core.Tests;

public class PercentTests
{
    [Theory]
    [InlineData(0.479, "47.9%")]
    [InlineData(0.5004, "50.04%")]
    [InlineData(0.4996, "49.96%")]
    [InlineData(0.50004, "50.004%")]
    [InlineData(0.5, "50.0000%")]
    [InlineData(0.512, "51.2%")]
    public void Share_AddsDecimalsOnlyWhenItWouldRoundTo50(double share, string expected) =>
        Assert.Equal(expected, Percent.Share(share));
}
