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

    private static readonly IReadOnlyDictionary<ulong, string> NoNames = new Dictionary<ulong, string>();

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
    public async Task Latest_names_use_the_name_of_each_players_most_recent_vote()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var first = env.Repo.GetActive()!;
        env.Votes.Cast(first.Id, 1, "old_name", first.WinnerSide);
        env.Votes.Cast(first.Id, 2, "bo", first.WinnerSide);
        await env.Rounds.AdvanceAsync(Local(11, 9), default);
        var second = env.Repo.GetActive()!;
        env.Votes.Cast(second.Id, 1, "new_name", second.WinnerSide);

        var names = env.Scores.LatestNames([1, 2, 3]);

        Assert.Equal("new_name", names[1]);
        Assert.Equal("bo", names[2]);
        Assert.False(names.ContainsKey(3));
    }

    [Fact]
    public void All_time_is_a_code_block_table_ranked_by_trophies_then_ranked_wins()
    {
        var names = new Dictionary<ulong, string> { [1] = "ann", [2] = "bo" };
        var text = MessageText.AllTimeStandings([
            new AllTimeEntry(1, 2, 5, 5, 0, 0),
            new AllTimeEntry(2, 1, 9, 1, 3, 1),
        ], names);

        var lines = text.Split('\n');
        Assert.StartsWith("🏛️ **All-time standings**", lines[0]);
        Assert.Equal("```", lines[1]);
        Assert.Equal($"  #  {"Player",-24} {"Tr",2}  {"Ranked",-12}  Regular", lines[2]);
        Assert.Equal($"  1  {"ann",-24}  2  5W-5L    50%  -", lines[3]);
        Assert.Equal($"  2  {"bo",-24}  1  9W-1L    90%  3W-1L    75%", lines[4]);
        Assert.Equal("```", lines[5]);
    }

    [Fact]
    public void Names_are_cut_to_24_characters_with_dots_and_cannot_break_the_table()
    {
        var names = new Dictionary<ulong, string>
        {
            [1] = "TheLongestUsernameEverMade_xX",   // 29 characters
            [2] = "exactly_24_characters_abc",       // 25: one too many
            [3] = "twenty_four_characters_ab",       // 25 as well, checked below
            [4] = "a`b\nc",
        };

        string cut = MessageText.ShortName(names, 1);
        Assert.Equal("TheLongestUsernameEve...", cut);
        Assert.Equal(MessageText.NameWidth, cut.Length);
        Assert.Equal("exactly_24_characters...", MessageText.ShortName(names, 2));
        Assert.Equal("a'b c", MessageText.ShortName(names, 4));
        Assert.Equal("123", MessageText.ShortName(names, 123));   // unknown name: the id
        Assert.Equal("exactly_24_characters_ab", MessageText.ShortName(
            new Dictionary<ulong, string> { [9] = "exactly_24_characters_ab" }, 9));   // exactly 24 is kept
    }

    [Fact]
    public void Standings_are_cut_to_fit_one_message_and_count_the_rest()
    {
        var many = Enumerable.Range(1, 200).Select(i => new AllTimeEntry((ulong)i, 0, 10, 10, 10, 10)).ToList();
        var names = many.ToDictionary(e => e.UserId, e => $"player_with_a_long_name_{e.UserId}");

        var text = MessageText.AllTimeStandings(many, names);

        Assert.True(text.Length <= 2000, $"{text.Length} characters");
        Assert.Matches(@"…and \d+ more", text);
        Assert.Equal(2, text.Split("```").Length - 1);   // the table stays one closed code block
    }

    [Fact]
    public void Empty_standings_say_so()
    {
        Assert.Contains("Nobody has scored yet", MessageText.WeeklyStandings("2026-11-09", [], NoNames));
        Assert.Contains("No round has been revealed yet", MessageText.AllTimeStandings([], NoNames));
    }

    [Fact]
    public void Weekly_standings_show_places_points_wins_and_mark_the_leader()
    {
        var names = new Dictionary<ulong, string> { [1] = "ann", [2] = "bo" };
        var text = MessageText.WeeklyStandings("2026-11-09", [new StandingEntry(1, 6, 2), new StandingEntry(2, 3, 1)], names);

        var lines = text.Split('\n');
        Assert.StartsWith($"📊 **Weekly standings · Week {Weekly.WeekNumber("2026-11-09")}**", lines[0]);
        Assert.Equal($"  #  {"Player",-24}  {"Pts",4}  {"Wins",4}", lines[2]);
        Assert.Equal($"* 1  {"ann",-24}     6     2", lines[3]);
        Assert.Equal($"  2  {"bo",-24}     3     1", lines[4]);
        Assert.Contains("* leading the week", text);
    }
}
