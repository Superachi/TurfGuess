using System.Globalization;

namespace TurfGuess.Bot.Game;

/// <summary>A player's place in a week's standings.</summary>
public sealed record StandingEntry(ulong UserId, int Points, int Wins);

/// <summary>A player who voted, and whether they placed a bet. Never says which team they picked.</summary>
public sealed record Voter(ulong Id, bool Bet);

/// <summary>One player's result in a revealed round. <see cref="Delta"/> is the change actually applied (after the 0 floor).</summary>
public sealed record PlayerOutcome(ulong UserId, bool Bet, int Delta);

/// <summary>Pure rules for the weekly competition (docs/WEEKLY_COMPETITION.md). No database, no Discord.</summary>
public static class Weekly
{
    /// <summary>Points before the floor: a correct guess earns the round's points (double with a bet); a wrong bet loses them.</summary>
    public static int RawDelta(int points, bool correct, bool bet) =>
        correct ? (bet ? 2 * points : points) : (bet ? -points : 0);

    /// <summary>The change actually applied: a score never drops below 0.</summary>
    public static int AppliedDelta(int currentScore, int rawDelta) => Math.Max(rawDelta, -currentScore);

    /// <summary>The Monday (as yyyy-MM-dd) of the week that contains <paramref name="at"/> in <paramref name="zone"/>.</summary>
    public static string WeekStart(DateTimeOffset at, TimeZoneInfo zone)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
        int sinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-sinceMonday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>The ISO week number of a week start, for the weekly summary title.</summary>
    public static int WeekNumber(string weekStart) =>
        ISOWeek.GetWeekOfYear(DateTime.ParseExact(weekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>
    /// Who wins a week from standings ordered best first: the most points, then the most wins.
    /// Anyone still tied is a winner too. Nobody wins a week in which nobody scored.
    /// </summary>
    public static IReadOnlyList<StandingEntry> WeekWinners(IReadOnlyList<StandingEntry> standings)
    {
        if (standings.Count == 0 || standings[0].Points <= 0) return [];
        var best = standings[0];
        return standings.Where(s => s.Points == best.Points && s.Wins == best.Wins).ToList();
    }
}
