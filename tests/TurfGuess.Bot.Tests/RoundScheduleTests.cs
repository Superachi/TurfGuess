using TurfGuess.Bot.Game;

namespace TurfGuess.Bot.Tests;

public class RoundScheduleTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly TimeOnly NineAm = new(9, 0);

    private static DateTimeOffset Local(int y, int mo, int d, int h, int mi = 0) =>
        new DateTimeOffset(new DateTime(y, mo, d, h, mi, 0), Amsterdam.GetUtcOffset(new DateTime(y, mo, d, h, mi, 0)));

    [Fact]
    public void Before_the_round_time_the_next_occurrence_is_today()
    {
        var next = RoundSchedule.NextOccurrence(Local(2026, 11, 10, 8, 30), NineAm, Amsterdam);
        Assert.Equal(Local(2026, 11, 10, 9), next);
    }

    [Fact]
    public void At_or_after_the_round_time_the_next_occurrence_is_tomorrow()
    {
        Assert.Equal(Local(2026, 11, 11, 9), RoundSchedule.NextOccurrence(Local(2026, 11, 10, 9), NineAm, Amsterdam));
        Assert.Equal(Local(2026, 11, 11, 9), RoundSchedule.NextOccurrence(Local(2026, 11, 10, 15), NineAm, Amsterdam));
    }

    [Fact]
    public void Catch_up_rounds_are_shorter_and_never_drift()
    {
        // The bot was down and catches up at 15:00: the new round still reveals at the next 09:00.
        var created = Local(2026, 11, 10, 15);
        var reveal = RoundSchedule.RevealAt(created, NineAm, Amsterdam, isFirstRound: false, TimeSpan.FromHours(6));
        Assert.Equal(Local(2026, 11, 11, 9), reveal);
        Assert.Equal(TimeSpan.FromHours(18), reveal - created);
    }

    [Fact]
    public void Clocks_going_back_make_a_25_hour_round()
    {
        // Europe/Amsterdam falls back on 25 October 2026.
        var created = Local(2026, 10, 24, 9);
        var reveal = RoundSchedule.RevealAt(created, NineAm, Amsterdam, false, TimeSpan.Zero);
        Assert.Equal(Local(2026, 10, 25, 9), reveal);
        Assert.Equal(TimeSpan.FromHours(25), reveal - created);
    }

    [Fact]
    public void Clocks_going_forward_make_a_23_hour_round()
    {
        // Europe/Amsterdam springs forward on 29 March 2026.
        var created = Local(2026, 3, 28, 9);
        var reveal = RoundSchedule.RevealAt(created, NineAm, Amsterdam, false, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(23), reveal - created);
    }

    [Fact]
    public void A_round_time_skipped_by_daylight_saving_moves_to_the_first_valid_time()
    {
        // 02:30 doesn't exist on 29 March 2026 in Amsterdam.
        var next = RoundSchedule.NextOccurrence(Local(2026, 3, 28, 12), new TimeOnly(2, 30), Amsterdam);
        var local = TimeZoneInfo.ConvertTime(next, Amsterdam);
        Assert.Equal(new DateTime(2026, 3, 29, 3, 0, 0), local.DateTime);
    }

    [Fact]
    public void First_round_that_would_be_too_short_skips_a_day()
    {
        var created = Local(2026, 11, 10, 8, 55); // 5 minutes before the round time
        var reveal = RoundSchedule.RevealAt(created, NineAm, Amsterdam, isFirstRound: true, TimeSpan.FromHours(6));
        Assert.Equal(Local(2026, 11, 11, 9), reveal);
    }

    [Fact]
    public void Later_rounds_are_never_extended()
    {
        var created = Local(2026, 11, 10, 8, 55);
        var reveal = RoundSchedule.RevealAt(created, NineAm, Amsterdam, isFirstRound: false, TimeSpan.FromHours(6));
        Assert.Equal(Local(2026, 11, 10, 9), reveal);
    }
}
