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

        if (Paint.UnpaintedMin < 0 || Paint.UnpaintedMax >= 0.5 || Paint.UnpaintedMin > Paint.UnpaintedMax) errors.Add("Paint.UnpaintedMin/Max must satisfy 0 <= min <= max < 0.5.");
        if (Paint.PixelsPerCell is < 2 or > 64) errors.Add("Paint.PixelsPerCell must be in [2, 64].");

        if (Difficulty.MinWinnerShare <= 50 || Difficulty.MaxWinnerShare >= 100 || Difficulty.MinWinnerShare > Difficulty.MaxWinnerShare)
            errors.Add("Difficulty.MinWinnerShare/MaxWinnerShare must satisfy 50 < min <= max < 100.");

        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
    }
}

public sealed class MapOptions
{
    public int GridWidth { get; set; } = 96;
    public int GridHeight { get; set; } = 54;
    public SymmetryMode Symmetry { get; set; } = SymmetryMode.Rotate;

    /// <summary>Target fraction of floor cells covered by walls/pillars.</summary>
    public double ObstacleDensity { get; set; } = 0.06;

    /// <summary>Walkable cells as a fraction of the whole grid.</summary>
    public double MinFloor { get; set; } = 0.45;
    public double MaxFloor { get; set; } = 0.70;

    /// <summary>How many sub-seeds to try before giving up.</summary>
    public int MaxAttempts { get; set; } = 50;
}

public sealed class PaintOptions
{
    /// <summary>Fraction of total floor left unpainted, picked per round in [min, max].</summary>
    public double UnpaintedMin { get; set; } = 0.05;
    public double UnpaintedMax { get; set; } = 0.15;

    /// <summary>Seeded splat stamps in the ownership score.</summary>
    public bool Splatter { get; set; } = true;

    /// <summary>Mask and render resolution.</summary>
    public int PixelsPerCell { get; set; } = 16;
}

public sealed class DifficultyOptions
{
    /// <summary>Winner share of the PAINTED floor, in percent. Must be &gt; 50.</summary>
    public double MinWinnerShare { get; set; } = 50.5;
    public double MaxWinnerShare { get; set; } = 58.0;
}
