using TurfGuess.Core.Grid;
using TurfGuess.Core.Paint;

namespace TurfGuess.Core.Tests;

public class SpawnPaintTests
{
    private static GeneratorOptions Options(double diligentChance) =>
        new() { Paint = { PixelsPerCell = 4, DiligentSpawnChance = diligentChance } };

    /// <summary>Unpainted pixels within <paramref name="cells"/> cells of a spawn pad's centre.</summary>
    private static int UnpaintedNear(GeneratedRound r, Side side, int cells)
    {
        var spawn = r.Map.Spawn(side);
        int p = r.Paint.PixelsPerCell;
        double cx = (spawn.X + spawn.W / 2.0) * p, cy = (spawn.Y + spawn.H / 2.0) * p, radius = cells * p;
        int count = 0;
        for (int y = 0; y < r.Paint.Height; y++)
            for (int x = 0; x < r.Paint.Width; x++)
                if (r.Paint[x, y] == PixelKind.Unpainted && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                    count++;
        return count;
    }

    [Fact]
    public void Chance_zero_and_one_control_diligence()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var never = RoundGenerator.Generate(seed, Options(0));
            Assert.False(never.Paint.DiligentLeft);
            Assert.False(never.Paint.DiligentRight);

            var always = RoundGenerator.Generate(seed, Options(1));
            Assert.True(always.Paint.DiligentLeft);
            Assert.True(always.Paint.DiligentRight);
        }
    }

    [Fact]
    public void Default_chance_gives_a_mix_across_seeds()
    {
        var flags = Enumerable.Range(1, 30)
            .Select(i => RoundGenerator.Generate((ulong)i, Options(0.3)).Paint)
            .SelectMany(p => new[] { p.DiligentLeft, p.DiligentRight })
            .ToList();
        Assert.Contains(true, flags);
        Assert.Contains(false, flags);
    }

    [Fact]
    public void Both_teams_are_diligent_or_neither_is()
    {
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var p = RoundGenerator.Generate(seed, Options(0.5)).Paint;
            Assert.Equal(p.DiligentLeft, p.DiligentRight);
        }
    }

    [Fact]
    public void Diligent_teams_leave_less_unpainted_floor_near_their_spawn()
    {
        int lazy = 0, diligent = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var lazyRound = RoundGenerator.Generate(seed, Options(0));
            var diligentRound = RoundGenerator.Generate(seed, Options(1));
            foreach (var side in new[] { Side.Left, Side.Right })
            {
                lazy += UnpaintedNear(lazyRound, side, 12);
                diligent += UnpaintedNear(diligentRound, side, 12);
            }
        }
        Assert.True(diligent < lazy * 0.7, $"diligent {diligent} vs lazy {lazy}");
    }

    [Fact]
    public void Diligence_never_changes_the_targets()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var r = RoundGenerator.Generate(seed, Options(1));
            Assert.InRange(Math.Abs(r.Score.WinnerShare - r.TargetWinnerShare), 0, 1.0 / r.Score.PaintedPixels);
            Assert.InRange(Math.Abs(r.Score.UnpaintedShare - r.TargetUnpaintedShare), 0, 1.0 / r.Score.FloorPixels);
        }
    }

    [Fact]
    public void Invalid_chance_is_rejected() =>
        Assert.Throws<ArgumentException>(() => RoundGenerator.Generate(1, Options(1.5)));
}
