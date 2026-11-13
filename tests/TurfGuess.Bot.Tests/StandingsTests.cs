using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Tests;

/// <summary>/turf standings and /turf all-time: the weekly standings and the all-time win/loss record.</summary>
public class StandingsTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static DateTimeOffset Local(int d, int h)
    {
        var dt = new DateTime(2026, 11, d, h, 0, 0);
        return new DateTimeOffset(dt, Amsterdam.GetUtcOffset(dt));
    }

    [Fact]
    public async Task All_time_counts_ranked_and_regular_wins_and_losses_per_player()
    {
        using var env = new TestEnvironment();

        // A ranked round: ann right, bo wrong.
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var ranked = env.Repo.GetActive()!;
        env.Votes.Cast(ranked.Id, 1, "ann", ranked.WinnerSide);
        env.Votes.Cast(ranked.Id, 2, "bo", ranked.WinnerSide.Opposite());
        await env.Rounds.AdvanceAsync(Local(11, 9), default);

        // A regular battle: ann wrong, bo right.
        var (battle, _) = await env.Rounds.StartBattleAsync(1, false, Local(11, 12), default);
        env.Votes.Cast(battle.Id, 1, "ann", battle.WinnerSide.Opposite());
        env.Votes.Cast(battle.Id, 2, "bo", battle.WinnerSide);
        await env.Rounds.AdvanceAsync(Local(11, 13), default);

        var all = env.Scores.AllTime();

        Assert.Equal(new AllTimeEntry(1, 0, 1, 0, 0, 1), all.Single(e => e.UserId == 1));
        Assert.Equal(new AllTimeEntry(2, 0, 0, 1, 1, 0), all.Single(e => e.UserId == 2));
    }

    [Fact]
    public async Task All_time_ignores_open_and_cancelled_rounds()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var round = env.Repo.GetActive()!;
        env.Votes.Cast(round.Id, 1, "ann", round.WinnerSide);
        Assert.Empty(env.Scores.AllTime());                      // still open

        await env.Rounds.ResetRoundAsync(default);               // cancelled, never revealed
        Assert.Empty(env.Scores.AllTime());
    }

    [Fact]
    public void All_time_ranks_by_trophies_then_ranked_wins()
    {
        var text = MessageText.AllTimeStandings([
            new AllTimeEntry(1, 2, 5, 5, 0, 0),
            new AllTimeEntry(2, 1, 9, 1, 3, 1),
        ]);

        Assert.StartsWith("🏛️ **All-time standings**", text);
        Assert.Contains("1. <@1> 🏆 2 · ⚓ 5W–5L (50%) · 🔫 –", text);
        Assert.Contains("2. <@2> 🏆 1 · ⚓ 9W–1L (90%) · 🔫 3W–1L (75%)", text);
    }

    [Fact]
    public void Standings_lists_are_cut_to_fit_one_message_and_count_the_rest()
    {
        var many = Enumerable.Range(1, 200).Select(i => new AllTimeEntry((ulong)(1_000_000_000_000_000 + i), 0, 10, 10, 10, 10)).ToList();

        var text = MessageText.AllTimeStandings(many);

        Assert.True(text.Length <= 2000, $"{text.Length} characters");
        Assert.Matches(@"…and \d+ more", text);
    }

    [Fact]
    public void Empty_standings_say_so()
    {
        Assert.Contains("Nobody has scored yet", MessageText.WeeklyStandings("2026-11-09", []));
        Assert.Contains("No round has been revealed yet", MessageText.AllTimeStandings([]));
    }

    [Fact]
    public void Weekly_standings_show_places_points_wins_and_the_leader_trophy()
    {
        var text = MessageText.WeeklyStandings("2026-11-09", [new StandingEntry(1, 6, 2), new StandingEntry(2, 3, 1)]);

        Assert.StartsWith($"📊 **Weekly standings · Week {Weekly.WeekNumber("2026-11-09")}**", text);
        Assert.Contains("1. 🏆 <@1> 6 pts · 2 wins", text);
        Assert.Contains("2. <@2> 3 pts · 1 win", text);
    }
}
