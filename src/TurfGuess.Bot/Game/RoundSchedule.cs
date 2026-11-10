namespace TurfGuess.Bot.Game;

/// <summary>
/// When rounds end (DESIGN.md §2, Schedule rules). The round time is wall-clock time in a time
/// zone, not a fixed 24-hour gap, so daylight-saving changes don't shift it and catch-up rounds
/// are simply shorter.
/// </summary>
public static class RoundSchedule
{
    /// <summary>The first moment strictly after <paramref name="after"/> when the clock in <paramref name="zone"/> shows <paramref name="time"/>.</summary>
    public static DateTimeOffset NextOccurrence(DateTimeOffset after, TimeOnly time, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(after, zone);
        var date = DateOnly.FromDateTime(local.DateTime);

        for (int day = 0; day < 3; day++)
        {
            var candidate = date.AddDays(day).ToDateTime(time, DateTimeKind.Unspecified);

            // A time skipped by a daylight-saving jump (e.g. 02:30 on the spring-forward night)
            // doesn't exist; use the first valid time after it.
            while (zone.IsInvalidTime(candidate)) candidate = candidate.AddMinutes(30);

            var instant = new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
            if (instant > after) return instant.ToUniversalTime();
        }

        throw new InvalidOperationException("No next occurrence found."); // unreachable
    }

    /// <summary>
    /// When a round created at <paramref name="createdAt"/> is revealed: the next round time.
    /// On the very first round, a round shorter than <paramref name="minFirstRound"/> skips to the
    /// round time after that, so the first round isn't just a few minutes long.
    /// </summary>
    public static DateTimeOffset RevealAt(DateTimeOffset createdAt, TimeOnly time, TimeZoneInfo zone, bool isFirstRound, TimeSpan minFirstRound)
    {
        var reveal = NextOccurrence(createdAt, time, zone);
        if (isFirstRound && reveal - createdAt < minFirstRound)
            reveal = NextOccurrence(reveal, time, zone);
        return reveal;
    }
}
