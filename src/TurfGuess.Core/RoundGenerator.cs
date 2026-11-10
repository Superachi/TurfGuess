using TurfGuess.Core.Grid;
using TurfGuess.Core.MapGen;
using TurfGuess.Core.Paint;
using TurfGuess.Core.Scoring;
using TurfGuess.Core.Util;

namespace TurfGuess.Core;

/// <summary>A fully generated round: map, final paint mask and the score counted from it.</summary>
public sealed record GeneratedRound(
    ulong Seed,
    int GeneratorVersion,
    Side Winner,
    double TargetWinnerShare,
    double TargetUnpaintedShare,
    int Attempts,
    GameMap Map,
    PaintResult Paint,
    ScoreResult Score);

/// <summary>Seed + options in, round out. Pure and deterministic.</summary>
public static class RoundGenerator
{
    /// <summary>Bump whenever a change alters what a given seed produces.</summary>
    public const int GeneratorVersion = 2;

    public static GeneratedRound Generate(ulong seed, GeneratorOptions options)
    {
        options.Validate();

        // Round-level choices come from their own stream, so they don't change between attempts.
        var rng = new Rng(Rng.Derive(seed, 0xD1FF));
        var winner = rng.Chance(0.5) ? Side.Left : Side.Right;
        double winnerShare = rng.Range(options.Difficulty.MinWinnerShare, options.Difficulty.MaxWinnerShare) / 100.0;
        double unpaintedShare = rng.Range(options.Paint.UnpaintedMin, options.Paint.UnpaintedMax);

        for (int attempt = 0; attempt < options.Map.MaxAttempts; attempt++)
        {
            var map = MapGenerator.Generate(Rng.Derive(seed, 2UL * (ulong)attempt + 1), options.Map);
            var paint = PaintSimulator.Paint(map, options.Paint, winner, winnerShare, unpaintedShare, Rng.Derive(seed, 2UL * (ulong)attempt + 2));
            if (paint is null) continue;

            var score = Score.Count(paint);
            return new GeneratedRound(seed, GeneratorVersion, winner, winnerShare, unpaintedShare, attempt + 1, map, paint, score);
        }

        throw new MapGenerationException($"Seed {seed}: no valid painted round after {options.Map.MaxAttempts} attempts.");
    }
}
