using TurfGuess.Core.Grid;

namespace TurfGuess.Core;

/// <summary>All knobs for generating a round. Mutable so it can be bound from configuration.</summary>
public sealed class GeneratorOptions
{
    public MapOptions Map { get; set; } = new();
    public PaintOptions Paint { get; set; } = new();
    public DifficultyOptions Difficulty { get; set; } = new();

    /// <summary>Throws <see cref="ArgumentException"/> when the options are inconsistent.</summary>
    public void Validate()
    {
        var errors = new List<string>();

        if (Map.GridWidth < 16 || Map.GridWidth % 2 != 0) errors.Add("Map.GridWidth must be an even number >= 16.");
        if (Map.GridHeight < 16) errors.Add("Map.GridHeight must be >= 16.");
        if (Map.ObstacleDensity is < 0 or > 0.3) errors.Add("Map.ObstacleDensity must be in [0, 0.3].");
        if (Map.MinFloor <= 0 || Map.MaxFloor >= 1 || Map.MinFloor >= Map.MaxFloor) errors.Add("Map.MinFloor/MaxFloor must satisfy 0 < min < max < 1.");
        if (Map.MaxAttempts < 1) errors.Add("Map.MaxAttempts must be >= 1.");
        if (Map.MaxObstaclesPerHalf is < 0 or > 20) errors.Add("Map.MaxObstaclesPerHalf must be in [0, 20].");

        if (Paint.UnpaintedMin < 0 || Paint.UnpaintedMax >= 0.5 || Paint.UnpaintedMin > Paint.UnpaintedMax) errors.Add("Paint.UnpaintedMin/Max must satisfy 0 <= min <= max < 0.5.");
        if (Paint.PixelsPerCell is < 2 or > 64) errors.Add("Paint.PixelsPerCell must be in [2, 64].");
        if (Paint.DiligentSpawnChance is < 0 or > 1) errors.Add("Paint.DiligentSpawnChance must be in [0, 1].");
        if (Paint.SpawnNeglectVariance is < 0 or > 1) errors.Add("Paint.SpawnNeglectVariance must be in [0, 1].");
        if (Paint.StrayInkChance is < 0 or > 1) errors.Add("Paint.StrayInkChance must be in [0, 1].");
        if (Paint.ChaosIntensityMin is < 0 or > 1) errors.Add("Paint.ChaosIntensityMin must be in [0, 1].");
        if (Paint.ChaosIntensityMax is < 0 or > 1) errors.Add("Paint.ChaosIntensityMax must be in [0, 1].");
        if (Paint.ChaosIntensityMin > Paint.ChaosIntensityMax) errors.Add("Paint.ChaosIntensityMin must not exceed Paint.ChaosIntensityMax.");
        if (Paint.RaidTrailsPerTeam is < 0 or > 12) errors.Add("Paint.RaidTrailsPerTeam must be in [0, 12].");
        if (Paint.InfiltratorChancePerTeam is < 0 or > 1) errors.Add("Paint.InfiltratorChancePerTeam must be in [0, 1].");
        if (Paint.InfiltratorExtraChance is < 0 or > 1) errors.Add("Paint.InfiltratorExtraChance must be in [0, 1].");
        if (Paint.FrontTiltMaxDegrees is < 0 or > 60) errors.Add("Paint.FrontTiltMaxDegrees must be in [0, 60].");
        if (Paint.CornerRadius is < 0 or >= 1) errors.Add("Paint.CornerRadius must be in [0, 1).");
        if (Paint.InnerCornerRadius is < 0 or >= 1) errors.Add("Paint.InnerCornerRadius must be in [0, 1).");

        if (Difficulty.MinWinnerShare <= 50 || Difficulty.MaxWinnerShare >= 100 || Difficulty.MinWinnerShare > Difficulty.MaxWinnerShare)
            errors.Add("Difficulty.MinWinnerShare/MaxWinnerShare must satisfy 50 < min <= max < 100.");

        if (Difficulty.RerollsS < 0 || Difficulty.RerollsA < 0 || Difficulty.RerollsB < 0 || Difficulty.RerollsC < 0)
            errors.Add("Difficulty.Rerolls* must be >= 0.");

        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
    }
}

public sealed class MapOptions
{
    public int GridWidth { get; set; } = 96;
    public int GridHeight { get; set; } = 54;
    public SymmetryMode Symmetry { get; set; } = SymmetryMode.Rotate;

    /// <summary>Target fraction of floor cells covered by obstacles (capped by <see cref="MaxObstaclesPerHalf"/>).</summary>
    public double ObstacleDensity { get; set; } = 0.03;

    /// <summary>Hard cap on obstacles per half of the map.</summary>
    public int MaxObstaclesPerHalf { get; set; } = 4;

    /// <summary>Walkable cells as a fraction of the whole grid.</summary>
    public double MinFloor { get; set; } = 0.45;
    public double MaxFloor { get; set; } = 0.70;

    /// <summary>How many sub-seeds to try before giving up.</summary>
    public int MaxAttempts { get; set; } = 50;
}

public sealed class PaintOptions
{
    /// <summary>Fraction of total floor left unpainted, picked per round in [min, max].</summary>
    public double UnpaintedMin { get; set; } = 0.03;
    public double UnpaintedMax { get; set; } = 0.10;

    /// <summary>Seeded splat stamps: in the fight zone, and paint sputters at the unpainted border near spawns.</summary>
    public bool Splatter { get; set; } = true;

    /// <summary>
    /// Chance, per round, that both teams painted their own spawn area diligently (otherwise neither did).
    /// Diligent teams leave far less unpainted floor near its spawn.
    /// </summary>
    public double DiligentSpawnChance { get; set; } = 0.3;

    /// <summary>
    /// How much each team's spawn neglect varies independently, as a fraction: each team's neglect is
    /// multiplied by a random factor in [1 − v, 1 + v]. 0 = both spawns equally bare.
    /// </summary>
    public double SpawnNeglectVariance { get; set; } = 0.6;

    /// <summary>
    /// Chance, per team and round, of stray ink deep in the other team's territory: either a trail
    /// (a player running through) or some splotches (bombs). The two teams roll independently.
    /// </summary>
    public double StrayInkChance { get; set; } = 0.4;

    /// <summary>
    /// Long, curving, broken-up trails each team runs through the other team's turf, starting at
    /// its own side of the front line, like players running through the enemy half. 0 = none.
    /// </summary>
    public int RaidTrailsPerTeam { get; set; } = 6;

    /// <summary>
    /// How much swapped ink there is on both sides: noise-shaped blotches of each team's colour inside
    /// the other half, anywhere. Each round picks its intensity between min and max, so rounds vary.
    /// 0 = none, 1 = a lot of the arena is affected.
    /// </summary>
    public double ChaosIntensityMin { get; set; } = 0.5;

    /// <summary>Upper end of the per-round chaos intensity range (see <see cref="ChaosIntensityMin"/>).</summary>
    public double ChaosIntensityMax { get; set; } = 0.9;

    /// <summary>
    /// Chance, per team and round, that a player sneaked past the front and painted part of the enemy
    /// base: a thin approach trail ending in a blotchy patch near the enemy spawn. 0 = none.
    /// </summary>
    public double InfiltratorChancePerTeam { get; set; } = 0;

    /// <summary>Chance that an infiltrating team sent a second sneak.</summary>
    public double InfiltratorExtraChance { get; set; } = 0.3;

    /// <summary>
    /// Largest tilt of the front line away from vertical, in degrees. Each round picks an angle in
    /// [−max, +max]. 0 = always vertical.
    /// </summary>
    public double FrontTiltMaxDegrees { get; set; } = 20;

    /// <summary>Mask and render resolution.</summary>
    public int PixelsPerCell { get; set; } = 16;

    /// <summary>
    /// Radius of the rounded OUTER (convex) corners of the map outline, in cells, like CSS
    /// border-radius. 0 = sharp. Must stay below 1, or 2-cell-wide parts of the map would vanish.
    /// </summary>
    public double CornerRadius { get; set; } = 0.75;

    /// <summary>
    /// Radius of the curve filling INNER (concave) corners of the outline, in cells. 0 = sharp.
    /// Must stay below 1, or 2-cell-wide gaps would fill in.
    /// </summary>
    public double InnerCornerRadius { get; set; } = 0.5;
}

public sealed class DifficultyOptions
{
    /// <summary>Winner share of the PAINTED floor, in percent. Must be &gt; 50.</summary>
    public double MinWinnerShare { get; set; } = 50.3;
    public double MaxWinnerShare { get; set; } = 52.3;

    /// <summary>
    /// How many times an easy round may be rerolled, keeping the hardest one found. The limit that
    /// applies is the one for the best rank found so far (a total, not per rank). S, A, B and C
    /// default to 3, 5, 8 and 10; X is never rerolled.
    /// </summary>
    public int RerollsS { get; set; } = 3;
    public int RerollsA { get; set; } = 5;
    public int RerollsB { get; set; } = 8;
    public int RerollsC { get; set; } = 10;
}
