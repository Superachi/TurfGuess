using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Tests;

/// <summary>/turf subscribe: a private result message for subscribed players after a round is revealed.</summary>
public class SubscriptionTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static DateTimeOffset Local(int d, int h, int mi = 0)
    {
        var dt = new DateTime(2026, 11, d, h, mi, 0);
        return new DateTimeOffset(dt, Amsterdam.GetUtcOffset(dt));
    }

    [Fact]
    public void Subscribing_twice_and_unsubscribing_report_what_changed()
    {
        using var env = new TestEnvironment();
        Assert.True(env.Subscriptions.Subscribe(7, 100));
        Assert.False(env.Subscriptions.Subscribe(7, 200));
        Assert.True(env.Subscriptions.IsSubscribed(7));
        Assert.Equal(new HashSet<long> { 7 }, env.Subscriptions.SubscribedAmong([6, 7, 8]));
        Assert.True(env.Subscriptions.Unsubscribe(7));
        Assert.False(env.Subscriptions.Unsubscribe(7));
        Assert.Empty(env.Subscriptions.SubscribedAmong([7]));
        Assert.Empty(env.Subscriptions.SubscribedAmong([]));
    }

    [Fact]
    public async Task Ranked_reveal_messages_only_subscribed_voters_with_result_bet_and_points()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var round = env.Repo.GetActive()!;
        var winner = round.WinnerSide;
        int points = MessageText.PointsFor(round, env.Options.Generator.Difficulty);

        env.Votes.Cast(round.Id, 1, "ann", winner);               // right, auto-bet (0 points)
        env.Votes.Cast(round.Id, 2, "bo", winner.Opposite());     // wrong, auto-bet but nothing to lose
        env.Votes.Cast(round.Id, 3, "cy", winner);                // not subscribed
        env.Subscriptions.Subscribe(1, 0);
        env.Subscriptions.Subscribe(2, 0);
        env.Subscriptions.Subscribe(9, 0);                        // subscribed but didn't vote

        await env.Rounds.AdvanceAsync(Local(11, 9), default);

        Assert.Equal([1UL, 2UL], env.Notifier.Sent.Select(s => s.UserId).Order());
        var ann = env.Notifier.Sent.Single(s => s.UserId == 1).Text;
        Assert.Contains("You guessed right!", ann);
        Assert.EndsWith(MessageText.UnsubscribeFooter, ann);
        Assert.Contains("🎲 bet on", ann);
        Assert.Contains($"+{2 * points} points", ann);
        Assert.Contains($"Round #{round.DisplayNumber}", ann);
        Assert.Contains(round.TeamName(winner), ann);
        var bo = env.Notifier.Sent.Single(s => s.UserId == 2).Text;
        Assert.Contains("You guessed wrong.", bo);
        Assert.Contains("no change in points", bo);
    }

    [Fact]
    public async Task Battle_reveal_messages_subscribers_without_points()
    {
        using var env = new TestEnvironment();
        var now = Local(10, 12);
        var (round, _) = await env.Rounds.StartBattleAsync(1, false, now, default);
        env.Votes.Cast(round.Id, 1, "ann", round.WinnerSide.Opposite());
        env.Subscriptions.Subscribe(1, 0);

        await env.Rounds.AdvanceAsync(now.AddHours(1), default);

        var (userId, text) = Assert.Single(env.Notifier.Sent);
        Assert.Equal(1UL, userId);
        Assert.Contains("Regular Battle", text);
        Assert.Contains("You guessed wrong.", text);
        Assert.Contains("no points awarded", text);
        Assert.DoesNotContain("bet", text);
    }

    [Fact]
    public async Task A_subscriber_who_cannot_be_reached_does_not_stop_the_others_or_the_game()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var round = env.Repo.GetActive()!;
        env.Votes.Cast(round.Id, 1, "ann", round.WinnerSide);
        env.Votes.Cast(round.Id, 2, "bo", round.WinnerSide);
        env.Subscriptions.Subscribe(1, 0);
        env.Subscriptions.Subscribe(2, 0);
        env.Notifier.Unreachable.Add(1);

        await env.Rounds.AdvanceAsync(Local(11, 9), default);

        Assert.Equal(2UL, Assert.Single(env.Notifier.Sent).UserId);
        Assert.Equal(RoundStatus.Open, env.Repo.GetActive()!.Status); // the next round is already up
        Assert.Contains(env.Log.Messages, m => m.Contains("couldn't send the result to subscriber 1"));
    }
}
