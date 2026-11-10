using TurfGuess.Core.Grid;
using TurfGuess.Core.MapGen;

namespace TurfGuess.Core.Tests;

public class MapGeneratorTests
{
    public static TheoryData<ulong> Seeds => new() { 1, 2, 3, 7, 42, 99, 1234, 98765 };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Same_seed_gives_same_map(ulong seed)
    {
        var a = MapGenerator.Generate(seed, new MapOptions());
        var b = MapGenerator.Generate(seed, new MapOptions());
        Assert.Equal(a.Cells.Cells, b.Cells.Cells);
        Assert.Equal(a.SpawnLeft, b.SpawnLeft);
    }

    [Fact]
    public void Different_seeds_give_different_maps()
    {
        var a = MapGenerator.Generate(1, new MapOptions());
        var b = MapGenerator.Generate(2, new MapOptions());
        Assert.NotEqual(a.Cells.Cells, b.Cells.Cells);
    }

    [Theory]
    [InlineData(SymmetryMode.Rotate)]
    [InlineData(SymmetryMode.Mirror)]
    public void Maps_are_symmetric(SymmetryMode mode)
    {
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var map = MapGenerator.Generate(seed, new MapOptions { Symmetry = mode });
            var g = map.Cells;
            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    var (mx, my) = Symmetry.Map(x, y, g.Width, g.Height, mode);
                    Assert.Equal(Symmetry.Swap(g[x, y]), g[mx, my]);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_walkable_cell_is_reachable_from_both_spawns(ulong seed)
    {
        var map = MapGenerator.Generate(seed, new MapOptions());
        var fromLeft = MapGenerator.FloodFrom(map.Cells, map.SpawnLeft);
        var fromRight = MapGenerator.FloodFrom(map.Cells, map.SpawnRight);

        for (int i = 0; i < map.Cells.Cells.Length; i++)
        {
            if (!map.Cells.Cells[i].IsWalkable()) continue;
            Assert.True(fromLeft[i], $"cell {i} unreachable from left spawn");
            Assert.True(fromRight[i], $"cell {i} unreachable from right spawn");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Floor_fraction_and_spawns_are_valid(ulong seed)
    {
        var o = new MapOptions();
        var map = MapGenerator.Generate(seed, o);
        double fraction = map.WalkableCells / (double)(map.Width * map.Height);
        Assert.InRange(fraction, o.MinFloor, o.MaxFloor);

        Assert.True(map.SpawnLeft.X < map.Width / 2);
        Assert.True(map.SpawnRight.X >= map.Width / 2);
        Assert.Equal(CellType.SpawnLeft, map.Cells[map.SpawnLeft.X, map.SpawnLeft.Y]);
        Assert.Equal(CellType.SpawnRight, map.Cells[map.SpawnRight.X, map.SpawnRight.Y]);
    }
}
