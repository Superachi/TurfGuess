namespace TurfGuess.Core.Util;

/// <summary>
/// Small deterministic PRNG (SplitMix64). Used instead of <see cref="System.Random"/> so that
/// generation never depends on a runtime's Random implementation.
/// </summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed) => _state = seed;

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform int in [minInclusive, maxExclusive). Returns min when the range is empty.</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
    }

    public double Range(double min, double max) => min + NextDouble() * (max - min);

    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>Derives an independent sub-seed from a seed and a salt, without consuming state.</summary>
    public static ulong Derive(ulong seed, ulong salt) => Mix(seed ^ Mix(salt + 0x632BE59BD9B4E019UL));

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
