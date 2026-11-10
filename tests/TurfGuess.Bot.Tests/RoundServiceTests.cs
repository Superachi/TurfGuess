using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Tests;

public class RoundServiceTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static DateTimeOffset Local(int d, int h, int mi = 0)
    {
        var dt = new DateTime(2026, 11, d, h, mi, 0);
        return new DateTimeOffset(dt, Amsterdam.GetUtcOffset(dt));
    }

    [Fact]
    public async Task First_start_creates_and_posts_a_round_and_waits_for_its_reveal()
    {
        using var env = new TestEnvironment();
        var next = await env.Rounds.AdvanceAsync(Local(10, 12), default);

        var round = env.Repo.GetActive()!;
        Assert.Equal(RoundStatus.Open, round.Status);
        Assert.Equal(1, env.Channel.RoundPosts);
        Assert.Equal(Local(11, 9), next);
        Assert.Equal(Local(11, 9), round.RevealAtTime);
        Assert.True(File.Exists(round.ImagePath));
        Assert.True(File.Exists(round.RevealImagePath));
        Assert.NotNull(round.MessageId);
    }

    [Fact]
    public async Task Nothing_happens_before_the_reveal_time()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        await env.Rounds.AdvanceAsync(Local(10, 20), default);
        await env.Rounds.AdvanceAsync(Local(11, 8, 59), default);

        Assert.Equal(1, env.Channel.RoundPosts);
        Assert.Equal(0, env.Channel.RevealPosts);
    }

    [Fact]
    public async Task At_the_round_time_the_round_is_revealed_and_the_next_one_posted()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var first = env.Repo.GetActive()!;

        var votes = env.Votes;
        var winner = first.WinnerSide;
        votes.Cast(first.Id, 1, "player1", winner);
        votes.Cast(first.Id, 2, "player2", winner.Opposite());
        votes.Cast(first.Id, 3, "player3", winner);
        votes.Cast(first.Id, 3, "player3", winner.Opposite()); // changed their mind
        votes.Cast(first.Id, 3, "player3", winner);            // and back

        var next = await env.Rounds.AdvanceAsync(Local(11, 9), default);

        Assert.Equal(RoundStatus.Done, env.Repo.Get(first.Id)!.Status);
        Assert.Contains(first.Id, env.Channel.ClosedRounds);
        Assert.Equal(new ulong[] { 1, 2, 3 }, env.Channel.ClosedWithVoters[first.Id]);
        var (roundId, summary) = Assert.Single(env.Channel.Reveals);
        Assert.Equal(first.Id, roundId);
        Assert.Equal(new ulong[] { 1, 3 }, summary.CorrectGuessers);
        Assert.Equal(3, summary.LeftVotes + summary.RightVotes);

        var second = env.Repo.GetActive()!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(RoundStatus.Open, second.Status);
        Assert.Equal(Local(12, 9), next);
        Assert.Equal(2, env.Channel.RoundPosts);
    }

    [Fact]
    public async Task Votes_are_rejected_once_the_round_is_revealed()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var first = env.Repo.GetActive()!;
        await env.Rounds.AdvanceAsync(Local(11, 9), default);

        var votes = env.Votes;
        Assert.Equal(VoteResult.Closed, votes.Cast(first.Id, 42, "player42", Side.Left));
        Assert.Equal(VoteResult.Closed, votes.Cast(999, 42, "player42", Side.Left)); // unknown round
    }

    [Fact]
    public async Task Downtime_over_several_days_reveals_once_and_posts_one_new_round()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);

        // Back three days later at 15:00.
        var next = await env.Rounds.AdvanceAsync(Local(14, 15), default);

        Assert.Equal(1, env.Channel.RevealPosts);
        Assert.Equal(2, env.Channel.RoundPosts);
        Assert.Equal(Local(15, 9), next); // the catch-up round is shorter, the schedule doesn't drift
    }

    [Fact]
    public async Task A_crash_right_after_posting_a_round_never_posts_it_twice()
    {
        using var env = new TestEnvironment();
        env.Channel.CrashAfterNextPost = true;

        await Assert.ThrowsAsync<IOException>(() => env.Rounds.AdvanceAsync(Local(10, 12), default));
        Assert.Equal(RoundStatus.Pending, env.Repo.GetActive()!.Status);

        await env.Rounds.AdvanceAsync(Local(10, 12, 1), default);

        Assert.Equal(1, env.Channel.RoundPosts);
        var round = env.Repo.GetActive()!;
        Assert.Equal(RoundStatus.Open, round.Status);
        Assert.Equal((long)env.Channel.Messages.Single().Id, round.MessageId);
    }

    [Fact]
    public async Task A_crash_right_after_posting_a_reveal_never_posts_it_twice()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var first = env.Repo.GetActive()!;

        env.Channel.CrashAfterNextPost = true;
        await Assert.ThrowsAsync<IOException>(() => env.Rounds.AdvanceAsync(Local(11, 9), default));
        Assert.Equal(RoundStatus.Revealed, env.Repo.Get(first.Id)!.Status);

        await env.Rounds.AdvanceAsync(Local(11, 9, 1), default);

        Assert.Equal(1, env.Channel.RevealPosts);
        Assert.Equal(RoundStatus.Done, env.Repo.Get(first.Id)!.Status);
    }

    [Fact]
    public async Task Missing_images_are_regenerated_from_the_seed()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var round = env.Repo.GetActive()!;

        File.Delete(round.RevealImagePath);
        await env.Rounds.AdvanceAsync(Local(11, 9), default); // the reveal needs the image

        Assert.True(File.Exists(round.RevealImagePath));
        Assert.Equal(1, env.Channel.RevealPosts);
    }

    [Fact]
    public async Task Reveal_now_moves_the_reveal_to_now()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);

        Assert.True(env.Rounds.RevealNow(Local(10, 13)));
        await env.Rounds.AdvanceAsync(Local(10, 13), default);

        Assert.Equal(1, env.Channel.RevealPosts);
        Assert.Equal(2, env.Channel.RoundPosts);
    }

    [Fact]
    public void Reveal_now_without_an_open_round_does_nothing()
    {
        using var env = new TestEnvironment();
        Assert.False(env.Rounds.RevealNow(Local(10, 13)));
    }

    [Fact]
    public async Task Rounds_store_their_colors_and_exact_results()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var r = env.Repo.GetActive()!;

        Assert.NotEqual(r.LeftName, r.RightName);
        Assert.StartsWith("#", r.LeftColor);
        Assert.InRange(r.WinnerShare * 100, env.Options.Generator.Difficulty.MinWinnerShare - 0.01, env.Options.Generator.Difficulty.MaxWinnerShare + 0.01);
        Assert.Equal(1.0, r.ShareLeft + r.ShareRight + r.Unpainted, 9);
        Assert.Equal(Core.RoundGenerator.GeneratorVersion, r.GeneratorVersion);
    }
}
