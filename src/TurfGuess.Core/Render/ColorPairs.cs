using TurfGuess.Core.Util;

namespace TurfGuess.Core.Render;

/// <summary>Two opposing ink colors.</summary>
public sealed class ColorPair
{
    public TeamStyle A { get; set; } = new();
    public TeamStyle B { get; set; } = new();
}

/// <summary>
/// The built-in color pairs and the per-round pick (DESIGN.md §5, Colors).
/// Defaults: three hand-picked regular-battle ink pairs from the Inkipedia ink gallery, plus five extra combos.
/// </summary>
public static class ColorPairs
{
    public static IReadOnlyList<ColorPair> Default { get; } =
    [
        Pair("Pink", "#E0559A", "🩷", "Green", "#1E9A52", "💚"),
        Pair("Orange", "#DE6624", "🧡", "Blue", "#343BC4", "💙"),
        Pair("Yellow", "#D4AE0A", "💛", "Blue", "#343BC4", "💙"),
        Pair("Purple", "#8E3CC4", "💜", "Sky Blue", "#17A2F5", "🩵"),
        Pair("Blue Purple", "#6C3FD1", "💜", "Orange", "#DE6624", "🧡"),
        Pair("Lime Green", "#62C024", "💚", "Purple", "#8E3CC4", "💜"),
        Pair("Silver", "#C4C9D3", "🩶", "Royal Blue", "#5A7FEA", "💙"),
        Pair("Silver", "#C4C9D3", "🩶", "Gold", "#D4AF37", "💛"),
        Pair("Scarlet", "#B8141F", "❤️", "Pink", "#E0559A", "🩷"),
        Pair("Scarlet", "#E0282E", "❤️", "Blue", "#343BC4", "💙"),
    ];

    /// <summary>
    /// Picks a pair for a round from its seed, and which color goes to the left team.
    /// Deterministic: the same seed always gives the same colors.
    /// </summary>
    public static RenderStyle Pick(ulong seed, IReadOnlyList<ColorPair>? pairs = null, string gameName = "Turf Guess")
    {
        pairs ??= Default;
        if (pairs.Count == 0) throw new ArgumentException("At least one color pair is required.", nameof(pairs));

        var rng = new Rng(Rng.Derive(seed, 0xC0102));
        var pair = pairs[rng.Next(0, pairs.Count)];
        bool swap = rng.Chance(0.5);
        return new RenderStyle
        {
            GameName = gameName,
            Left = swap ? pair.B : pair.A,
            Right = swap ? pair.A : pair.B,
        };
    }

    private static ColorPair Pair(string aName, string aColor, string aEmoji, string bName, string bColor, string bEmoji) => new()
    {
        A = new TeamStyle { Name = aName, Color = aColor, Emoji = aEmoji },
        B = new TeamStyle { Name = bName, Color = bColor, Emoji = bEmoji },
    };
}
