using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Tests;

public class ResetTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static DateTimeOffset Local(int d, int h, int mi = 0)
    {
        var dt = new DateTime(2026, 11, d, h, mi, 0);
        return new DateTimeOffset(dt, Amsterdam.GetUtcOffset(dt));
    }

    [Fact]
    public async Task Resetting_with_no_round_does_nothing()
    {
        using var env = new TestEnvironment();
        Assert.Null(await env.Rounds.ResetRoundAsync(default));
    }

    [Fact]
    public async Task Reset_cancels_the_open_round_starts_a_new_one_and_keeps_the_weekly_scores()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var first = env.Repo.GetActive()!;
        env.Votes.Cast(first.Id, 1, "ann", first.WinnerSide);
        await env.Rounds.AdvanceAsync(Local(11, 9), default); // round 1 revealed and scored
        var standingsBefore = env.Scores.Standings(Weekly.WeekStart(first.RevealAtTime, Amsterdam));
        Assert.NotEmpty(standingsBefore);

        var second = env.Repo.GetActive()!;
        env.Votes.Cast(second.Id, 2, "bo", Side.Left);
        env.Votes.ToggleBet(second.Id, 2, "bo");

        var cancelled = await env.Rounds.ResetRoundAsync(default);

        Assert.Equal(second.Id, cancelled!.Id);
        Assert.Contains(second.Id, env.Channel.CancelledRounds);
        Assert.Equal(RoundStatus.Done, env.Repo.Get(second.Id)!.Status);
        Assert.Null(env.Repo.GetActive());

        // Votes and bets on the cancelled round are rejected, so its old buttons are dead.
        Assert.Equal(VoteResult.Closed, env.Votes.Cast(second.Id, 3, "cy", Side.Left));
        Assert.Null(env.Votes.ToggleBet(second.Id, 3, "cy"));

        await env.Rounds.AdvanceAsync(Local(11, 10), default);
        var third = env.Repo.GetActive()!;
        Assert.True(third.Id > second.Id); // ids are never reused, so old messages can't match
        Assert.Equal(RoundStatus.Open, third.Status);
        Assert.Equal(3, env.Channel.RoundPosts);
        Assert.Single(env.Channel.Reveals); // the cancelled round was never revealed

        // Scores are exactly as before, and the cancelled round scored nothing.
        Assert.Equal(standingsBefore, env.Scores.Standings(Weekly.WeekStart(first.RevealAtTime, Amsterdam)));
        Assert.Empty(env.Scores.GetResults(second.Id));
    }

    [Fact]
    public async Task Reset_also_works_on_a_round_that_is_still_pending()
    {
        using var env = new TestEnvironment();
        env.Channel.CrashAfterNextPost = true;
        await Assert.ThrowsAsync<IOException>(() => env.Rounds.AdvanceAsync(Local(10, 12), default));
        Assert.Equal(RoundStatus.Pending, env.Repo.GetActive()!.Status);

        Assert.NotNull(await env.Rounds.ResetRoundAsync(default));
        await env.Rounds.AdvanceAsync(Local(10, 13), default);
        Assert.Equal(RoundStatus.Open, env.Repo.GetActive()!.Status);
    }

    [Fact]
    public void Messages_older_than_the_round_are_not_adopted()
    {
        var created = new DateTimeOffset(2026, 11, 13, 1, 41, 15, TimeSpan.Zero);
        var round = new RoundRecord { Id = 2, CreatedAt = created.ToUnixTimeSeconds() };

        Assert.False(MessageText.IsFromThisRound(created.AddHours(-9), round));   // leftover of an earlier game
        Assert.False(MessageText.IsFromThisRound(created.AddMinutes(-5), round));
        Assert.True(MessageText.IsFromThisRound(created.AddSeconds(-30), round)); // clock differences
        Assert.True(MessageText.IsFromThisRound(created, round));
        Assert.True(MessageText.IsFromThisRound(created.AddMinutes(10), round));  // posted before a crash, found afterwards
    }

    [Fact]
    public async Task Wiping_scores_removes_points_wins_weeks_and_trophies_but_keeps_rounds()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(5, 12), default);
        for (int day = 6; day <= 9; day++) // reveals Fri 6th … Mon 9th: the week ends, a trophy is awarded
        {
            var round = env.Repo.GetActive()!;
            env.Votes.Cast(round.Id, 1, "ann", round.WinnerSide);
            await env.Rounds.AdvanceAsync(Local(day, 9), default);
        }
        var counts = env.Scores.Counts();
        Assert.True(counts.Results > 0);
        Assert.Equal(1, counts.Players);
        Assert.Equal(1, counts.Weeks);
        Assert.Equal(1, env.Scores.TrophyCount(1));
        long rounds = env.Repo.GetActive()!.Id;

        env.Scores.WipeAll();

        Assert.Equal((0, 0, 0), env.Scores.Counts());
        Assert.Equal(0, env.Scores.TrophyCount(1));
        Assert.Empty(env.Scores.Standings("2026-11-02"));
        Assert.Equal(rounds, env.Repo.GetActive()!.Id); // the game goes on
    }
}
