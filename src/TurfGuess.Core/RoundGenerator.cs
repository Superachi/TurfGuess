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
    int Rerolls,
    GameMap Map,
    PaintResult Paint,
    ScoreResult Score);

/// <summary>Seed + options in, round out. Pure and deterministic.</summary>
public static class RoundGenerator
{
    /// <summary>Bump whenever a change alters what a given seed produces.</summary>
    public const int GeneratorVersion = 5;

    /// <summary>
    /// Generates the round for <paramref name="seed"/>. An easy round is rerolled (from sub-seeds derived
    /// from the seed, so the result stays deterministic) up to the limits in
    /// <see cref="DifficultyOptions"/>. The budget is a total, set by the best rank found so far:
    /// the harder the best round, the fewer rerolls remain. The hardest round wins; ties go to the
    /// closer winner share. A regular battle uses its own, smaller reroll limits.
    /// </summary>
    public static GeneratedRound Generate(ulong seed, GeneratorOptions options, bool regular = false)
    {
        options.Validate();

        var best = GenerateOnce(seed, seed, 0, options);
        var bestRank = best.Score.Rank(options.Difficulty);
        for (int reroll = 1; reroll <= options.Difficulty.RerollLimit(bestRank, regular); reroll++)
        {
            var candidate = GenerateOnce(seed, Rng.Derive(seed, 0x5EED0000UL + (ulong)reroll), reroll, options);
            var rank = candidate.Score.Rank(options.Difficulty);
            if (rank > bestRank || (rank == bestRank && candidate.Score.WinnerShare < best.Score.WinnerShare))
            {
                best = candidate;
                bestRank = rank;
            }
        }
        return best;
    }

    private static GeneratedRound GenerateOnce(ulong seed, ulong baseSeed, int reroll, GeneratorOptions options)
    {

        // Round-level choices come from their own stream, so they don't change between attempts.
        var rng = new Rng(Rng.Derive(baseSeed, 0xD1FF));
        var winner = rng.Chance(0.5) ? Side.Left : Side.Right;
        double winnerShare = rng.Range(options.Difficulty.MinWinnerShare, options.Difficulty.MaxWinnerShare) / 100.0;
        double unpaintedShare = rng.Range(options.Paint.UnpaintedMin, options.Paint.UnpaintedMax);

        for (int attempt = 0; attempt < options.Map.MaxAttempts; attempt++)
        {
            var map = MapGenerator.Generate(Rng.Derive(baseSeed, 2UL * (ulong)attempt + 1), options.Map);
            var paint = PaintSimulator.Paint(map, options.Paint, winner, winnerShare, unpaintedShare, Rng.Derive(baseSeed, 2UL * (ulong)attempt + 2));
            if (paint is null) continue;

            var score = Score.Count(paint);
            return new GeneratedRound(seed, GeneratorVersion, winner, winnerShare, unpaintedShare, attempt + 1, reroll, map, paint, score);
        }

        throw new MapGenerationException($"Seed {seed}: no valid painted round after {options.Map.MaxAttempts} attempts.");
    }
}
