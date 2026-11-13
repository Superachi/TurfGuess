using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Core.Grid;
using TurfGuess.Core.Scoring;

namespace TurfGuess.Bot.Tests;

public class WeeklyRulesTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    [Theory]
    [InlineData(3, true, false, 3)]   // correct, no bet
    [InlineData(3, true, true, 6)]    // correct, bet: double
    [InlineData(3, false, false, 0)]  // wrong, no bet: nothing lost
    [InlineData(3, false, true, -3)]  // wrong, bet: lose the points
    [InlineData(5, true, true, 10)]   // X rank
    [InlineData(1, false, true, -1)]  // C rank
    public void Raw_delta_follows_the_rules(int points, bool correct, bool bet, int expected) =>
        Assert.Equal(expected, Weekly.RawDelta(points, correct, bet));

    [Theory]
    [InlineData(10, -3, -3)]
    [InlineData(2, -5, -2)]   // capped at what the player has
    [InlineData(0, -5, 0)]    // already at the floor
    [InlineData(0, 6, 6)]
    [InlineData(4, 0, 0)]
    public void A_score_never_drops_below_zero(int current, int raw, int applied) =>
        Assert.Equal(applied, Weekly.AppliedDelta(current, raw));

    [Fact]
    public void Ranks_are_worth_one_to_five_points()
    {
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, new[] { DifficultyRank.C, DifficultyRank.B, DifficultyRank.A, DifficultyRank.S, DifficultyRank.X }.Select(r => r.Points()));
    }

    private static DateTimeOffset Local(int month, int day, int hour, int minute = 0)
    {
        var dt = new DateTime(2026, month, day, hour, minute, 0);
        return new DateTimeOffset(dt, Amsterdam.GetUtcOffset(dt));
    }

    [Fact]
    public void The_week_starts_on_monday_in_the_configured_time_zone()
    {
        Assert.Equal("2026-11-02", Weekly.WeekStart(Local(11, 2, 0), Amsterdam));       // Monday 00:00
        Assert.Equal("2026-11-02", Weekly.WeekStart(Local(11, 8, 23, 59), Amsterdam));  // Sunday night
        Assert.Equal("2026-11-09", Weekly.WeekStart(Local(11, 9, 0), Amsterdam));       // next Monday 00:00
        Assert.Equal("2026-11-09", Weekly.WeekStart(Local(11, 9, 9), Amsterdam));

        // 23:30 UTC on Sunday is already Monday in Amsterdam.
        var sundayUtc = new DateTimeOffset(2026, 11, 8, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal("2026-11-09", Weekly.WeekStart(sundayUtc, Amsterdam));
        Assert.Equal("2026-11-02", Weekly.WeekStart(sundayUtc, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Week_winners_are_decided_by_points_then_wins_and_remaining_ties_all_win()
    {
        Assert.Empty(Weekly.WeekWinners([]));
        Assert.Empty(Weekly.WeekWinners([new StandingEntry(1, 0, 0)])); // nobody scored

        var a = new StandingEntry(1, 10, 3);
        var b = new StandingEntry(2, 10, 5);
        var c = new StandingEntry(3, 10, 5);
        var d = new StandingEntry(4, 8, 9);

        Assert.Equal([a], Weekly.WeekWinners([a, d]));
        Assert.Equal([b], Weekly.WeekWinners([b, a, d]));       // same points, more wins
        Assert.Equal([b, c], Weekly.WeekWinners([b, c, a, d])); // still tied: both win
    }
}

public class ScoreRepositoryTests
{
    private const string Week = "2026-11-02";

    /// <summary>A finished round, so several can exist at once.</summary>
    private static long NewRound(TestEnvironment env)
    {
        long id = env.Repo.Insert(Template());
        env.Repo.MarkOpen(id, 77, 150);
        env.Repo.MarkRevealed(id);
        env.Repo.MarkDone(id, 78);
        return id;
    }

    private static RoundRecord Template() => new()
    {
        ChannelId = 1, Seed = 5, GeneratorVersion = 1, CreatedAt = 100, RevealAt = 200,
        LeftName = "Pink", LeftColor = "#E0559A", LeftEmoji = "🩷",
        RightName = "Green", RightColor = "#2E7D32", RightEmoji = "💚",
        Winner = "left", WinnerShare = 0.52, ShareLeft = 0.47, ShareRight = 0.43, Unpainted = 0.10,
    };

    [Fact]
    public void Points_accumulate_over_the_week_and_the_floor_holds()
    {
        using var env = new TestEnvironment();
        long r1 = NewRound(env), r2 = NewRound(env), r3 = NewRound(env);

        // Player 1 loses a 5-point bet at 0 points: nothing to lose. Player 2 wins with a bet.
        env.Scores.ScoreRound(r1, Week, 5, [new(1, false, true), new(2, true, true)]);
        // Player 1 wins 3 without a bet, then loses a 5-point bet: capped at the 3 they have.
        env.Scores.ScoreRound(r2, Week, 3, [new(1, true, false), new(2, true, false)]);
        env.Scores.ScoreRound(r3, Week, 5, [new(1, false, true), new(2, false, false)]);

        var standings = env.Scores.Standings(Week);
        Assert.Equal([new StandingEntry(2, 13, 2), new StandingEntry(1, 0, 1)], standings);
        Assert.Equal(new long[] { 0 }, env.Scores.GetResults(r1).Where(x => x.UserId == 1).Select(x => x.Delta));
        Assert.Equal(new long[] { -3 }, env.Scores.GetResults(r3).Where(x => x.UserId == 1).Select(x => x.Delta));
    }

    [Fact]
    public void Scoring_a_round_twice_changes_nothing()
    {
        using var env = new TestEnvironment();
        long r = NewRound(env);

        env.Scores.ScoreRound(r, Week, 3, [new(1, true, true)]);
        var again = env.Scores.ScoreRound(r, Week, 3, [new(1, true, true), new(2, true, true)]);

        Assert.Single(again);
        Assert.Equal([new StandingEntry(1, 6, 1)], env.Scores.Standings(Week));
    }

    [Fact]
    public void Weeks_are_separate_and_closing_a_week_records_trophies()
    {
        using var env = new TestEnvironment();
        long r1 = NewRound(env), r2 = NewRound(env);
        env.Scores.ScoreRound(r1, Week, 3, [new(1, true, false)]);
        env.Scores.ScoreRound(r2, "2026-11-09", 3, [new(2, true, false)]);

        Assert.Equal([new StandingEntry(1, 3, 1)], env.Scores.Standings(Week));
        Assert.Equal([new StandingEntry(2, 3, 1)], env.Scores.Standings("2026-11-09"));
        Assert.Equal([Week], env.Scores.UnclosedWeeksBefore("2026-11-09"));
        Assert.Empty(env.Scores.UnclosedWeeksBefore(Week));

        env.Scores.CloseWeek(Week, Weekly.WeekWinners(env.Scores.Standings(Week)));
        env.Scores.CloseWeek(Week, Weekly.WeekWinners(env.Scores.Standings(Week))); // repeating is harmless

        Assert.Empty(env.Scores.UnclosedWeeksBefore("2026-11-09"));
        Assert.Equal(1, env.Scores.TrophyCount(1));
        Assert.Equal(0, env.Scores.TrophyCount(2));
    }

    [Fact]
    public void Bets_toggle_only_while_the_round_is_open()
    {
        using var env = new TestEnvironment();
        long id = env.Repo.Insert(Template());
        Assert.Null(env.Repo.ToggleBet(id, 1)); // still pending

        env.Repo.MarkOpen(id, 77, 150);
        Assert.True(env.Repo.ToggleBet(id, 1));
        Assert.True(env.Repo.HasBet(id, 1));
        Assert.False(env.Repo.ToggleBet(id, 1));
        Assert.False(env.Repo.HasBet(id, 1));

        env.Repo.ToggleBet(id, 2);
        env.Repo.TryUpsertVote(id, 2, "bob", Side.Left, 160);
        env.Repo.TryUpsertVote(id, 3, "carol", Side.Right, 161);
        Assert.Equal([new Voter(2, true), new Voter(3, false)], env.Repo.GetVoters(id));

        env.Repo.MarkRevealed(id);
        Assert.Null(env.Repo.ToggleBet(id, 1)); // closed
        Assert.True(env.Repo.HasBet(id, 2));    // unchanged
    }
}

public class WeeklyRoundTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static DateTimeOffset Local(int d, int h, int mi = 0)
    {
        var dt = new DateTime(2026, 11, d, h, mi, 0);
        return new DateTimeOffset(dt, Amsterdam.GetUtcOffset(dt));
    }

    [Fact]
    public async Task Reveal_scores_the_round_and_bets_double_or_lose()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var round = env.Repo.GetActive()!;
        var winner = round.WinnerSide;
        int points = MessageText.PointsFor(round, env.Options.Generator.Difficulty);

        env.Votes.Cast(round.Id, 1, "ann", winner);
        env.Votes.Cast(round.Id, 2, "bo", winner);
        env.Votes.Cast(round.Id, 3, "cy", winner.Opposite());
        env.Votes.Cast(round.Id, 4, "dee", winner.Opposite());
        env.Votes.ToggleBet(round.Id, 1, "ann");
        env.Votes.ToggleBet(round.Id, 3, "cy");
        env.Votes.ToggleBet(round.Id, 5, "eli"); // bet without a vote: ignored
        env.Votes.ToggleBet(round.Id, 2, "bo");
        env.Votes.ToggleBet(round.Id, 2, "bo");  // and off again

        await env.Rounds.AdvanceAsync(Local(11, 9), default);

        var (_, summary) = Assert.Single(env.Channel.Reveals);
        Assert.Equal([new PlayerOutcome(1, true, 2 * points), new PlayerOutcome(2, false, points)], summary.Winners);
        // Cy bet and lost, but had no points yet this week, so there was nothing to lose.
        Assert.Equal([new PlayerOutcome(3, true, 0), new PlayerOutcome(4, false, 0)], summary.Losers);
        Assert.Equal([new StandingEntry(1, 2 * points, 1), new StandingEntry(2, points, 1), new StandingEntry(3, 0, 0), new StandingEntry(4, 0, 0)], summary.Standings);
    }

    [Fact]
    public async Task The_week_summary_follows_the_first_reveal_of_the_new_week()
    {
        using var env = new TestEnvironment();
        // Thursday 5 Nov: round 1 is revealed Friday 6th, round 2 Saturday 7th, round 3 Sunday 8th,
        // round 4 Monday 9th (the first reveal of the new week).
        await env.Rounds.AdvanceAsync(Local(5, 12), default);

        async Task Play(int revealDay, params (ulong User, bool Correct, bool Bet)[] players)
        {
            var round = env.Repo.GetActive()!;
            foreach (var (user, correct, bet) in players)
            {
                env.Votes.Cast(round.Id, user, "player" + user, correct ? round.WinnerSide : round.WinnerSide.Opposite());
                if (bet) env.Votes.ToggleBet(round.Id, user, "player" + user);
            }
            await env.Rounds.AdvanceAsync(Local(revealDay, 9), default);
        }

        await Play(6, (1, true, true), (2, true, false));
        await Play(7, (2, true, false));
        await Play(8, (2, false, false));
        Assert.Empty(env.Channel.WeekSummaries); // still the same week

        await Play(9, (1, true, false));         // first reveal of the new week

        string summary = Assert.Single(env.Channel.WeekSummaries);
        Assert.Contains("Week 45 is over!", summary);
        Assert.True(env.Scores.TrophyCount(1) + env.Scores.TrophyCount(2) >= 1); // more than one only on an exact tie

        // The new week starts from zero.
        var (_, reveal) = env.Channel.Reveals.Last();
        Assert.Single(reveal.Standings);
        Assert.Equal(1UL, reveal.Standings[0].UserId);

        // Revealing more rounds doesn't post the old summary again.
        await Play(10, (1, true, false));
        Assert.Single(env.Channel.WeekSummaries);
    }
}
