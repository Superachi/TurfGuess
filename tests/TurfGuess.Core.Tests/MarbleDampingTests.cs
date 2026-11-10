using TurfGuess.Core.Render;

namespace TurfGuess.Core.Tests;

public class MarbleDampingTests
{
    [Theory]
    [InlineData(105)] // yellow
    [InlineData(120)]
    [InlineData(142)] // green
    public void Yellow_and_green_hues_are_fully_damped(double hue) =>
        Assert.Equal(0.35, MapRenderer.MarbleHueShiftScale(hue), 6);

    [Theory]
    [InlineData(45)]  // orange
    [InlineData(270)] // blue
    [InlineData(355)] // pink
    public void Other_hues_keep_the_full_shift(double hue) =>
        Assert.Equal(1.0, MapRenderer.MarbleHueShiftScale(hue), 6);

    [Fact]
    public void Damping_fades_smoothly_at_the_edges()
    {
        double previous = MapRenderer.MarbleHueShiftScale(150);
        for (double h = 151; h <= 175; h++)
        {
            double s = MapRenderer.MarbleHueShiftScale(h);
            Assert.True(s >= previous - 1e-9, $"not monotonic at {h}");
            previous = s;
        }
        Assert.InRange(MapRenderer.MarbleHueShiftScale(162.5), 0.6, 0.75);
    }
}
