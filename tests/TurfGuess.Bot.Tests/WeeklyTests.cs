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

public class WeeklyLoopSimulationTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static DateTimeOffset At(DateTime localDate, int hour)
    {
        var dt = localDate.Date.AddHours(hour);
        return new DateTimeOffset(dt, Amsterdam.GetUtcOffset(dt));
    }

    /// <summary>What a scripted player does on reveal number <c>day</c> (0 = Monday 2 November): vote correctly or not, and bet or not. Null = skips the round.</summary>
    private static (bool Correct, bool Bet)? Script(ulong player, int day) => player switch
    {
        1 => (true, false),                                   // always right, never bets
        2 => (day % 2 == 0, true),                            // always bets, right every other day
        3 => (day % 3 == 0, day % 3 == 1),                    // bets only on days it gets wrong
        4 => day % 7 >= 5 ? (day % 2 == 0, day % 4 == 0) : null, // weekends only
        _ => null,
    };

    /// <summary>
    /// Plays three weeks of rounds with four scripted players against a plain model of the rules,
    /// checking the standings after every reveal, the weekly summaries and the trophy tally.
    /// </summary>
    [Fact]
    public async Task Three_weeks_of_rounds_match_a_plain_model_of_the_rules()
    {
        using var env = new TestEnvironment();
        var monday = new DateTime(2026, 11, 2);
        await env.Rounds.AdvanceAsync(At(monday.AddDays(-1), 12), default); // Sunday: round 1 opens, revealed Monday 09:00

        var players = new ulong[] { 1, 2, 3, 4 };
        var score = new Dictionary<ulong, int>();
        var wins = new Dictionary<ulong, int>();
        var trophies = players.ToDictionary(p => p, _ => 0);
        int summariesExpected = 0;
        int lastWeek = 0;

        for (int day = 0; day < 15; day++) // Monday 2 Nov … Monday 16 Nov: three weeks, the last with one round
        {
            int week = day / 7;
            if (week != lastWeek)
            {
                // The new week's first reveal also announces the old week. Work out who should have won it.
                var best = players.Where(p => score.GetValueOrDefault(p) > 0)
                    .GroupBy(p => (Points: score[p], Wins: wins[p])).OrderByDescending(g => g.Key.Points).ThenByDescending(g => g.Key.Wins)
                    .FirstOrDefault();
                if (best is not null)
                {
                    summariesExpected++;
                    foreach (var p in best) trophies[p]++;
                }
                score.Clear();
                wins.Clear();
                lastWeek = week;
            }

            var round = env.Repo.GetActive()!;
            int points = MessageText.PointsFor(round, env.Options.Generator.Difficulty);

            foreach (var p in players)
            {
                if (Script(p, day) is not var (correct, bet)) continue;
                env.Votes.Cast(round.Id, p, "player" + p, correct ? round.WinnerSide : round.WinnerSide.Opposite());
                if (bet) env.Votes.ToggleBet(round.Id, p, "player" + p);

                int raw = correct ? (bet ? 2 * points : points) : (bet ? -points : 0);
                score[p] = Math.Max(0, score.GetValueOrDefault(p) + raw);
                if (correct) wins[p] = wins.GetValueOrDefault(p) + 1;
            }

            await env.Rounds.AdvanceAsync(At(monday.AddDays(day), 9), default);

            var (_, reveal) = env.Channel.Reveals.Last();
            var expected = score.Keys
                .OrderByDescending(p => score[p]).ThenByDescending(p => wins.GetValueOrDefault(p)).ThenBy(p => p)
                .Select(p => new StandingEntry(p, score[p], wins.GetValueOrDefault(p))).ToList();
            Assert.Equal(expected, reveal.Standings);
            Assert.True(reveal.Standings.All(s => s.Points >= 0));
            Assert.Equal(summariesExpected, env.Channel.WeekSummaries.Count);
        }

        Assert.Equal(2, summariesExpected); // weeks 1 and 2 were announced; week 3 is still running
        Assert.True(env.Channel.WeekSummaries.All(s => s.Contains("is over!") && s.Contains("🏆")));
        Assert.Contains("Week 45 is over!", env.Channel.WeekSummaries[0]);
        Assert.Contains("Week 46 is over!", env.Channel.WeekSummaries[1]);
        foreach (var p in players)
            Assert.Equal(trophies[p], env.Scores.TrophyCount(p));

        // The summary names exactly the players who got a trophy.
        foreach (var p in players)
            Assert.Equal(trophies[p] > 0, env.Channel.WeekSummaries.Any(s => s.Contains($"<@{p}>")));
    }
}
