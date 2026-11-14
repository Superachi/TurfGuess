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
    public void Everyone_is_subscribed_by_default_until_they_opt_out()
    {
        using var env = new TestEnvironment();
        Assert.True(env.Subscriptions.IsSubscribed(7));
        Assert.False(env.Subscriptions.Subscribe(7));             // already subscribed by default
        Assert.Equal(new HashSet<long> { 6, 7, 8 }, env.Subscriptions.SubscribedAmong([6, 7, 8]));

        Assert.True(env.Subscriptions.Unsubscribe(7, 100));
        Assert.False(env.Subscriptions.Unsubscribe(7, 200));      // already opted out
        Assert.False(env.Subscriptions.IsSubscribed(7));
        Assert.Equal(new HashSet<long> { 6, 8 }, env.Subscriptions.SubscribedAmong([6, 7, 8]));
        Assert.Empty(env.Subscriptions.SubscribedAmong([]));

        Assert.True(env.Subscriptions.Subscribe(7));              // opted back in
        Assert.True(env.Subscriptions.IsSubscribed(7));
    }

    [Fact]
    public async Task Ranked_reveal_messages_every_voter_except_opt_outs_with_result_bet_and_points()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var round = env.Repo.GetActive()!;
        var winner = round.WinnerSide;
        int points = MessageText.PointsFor(round, env.Options.Generator.Difficulty);

        env.Votes.Cast(round.Id, 1, "ann", winner);               // right, auto-bet (0 points)
        env.Votes.Cast(round.Id, 2, "bo", winner.Opposite());     // wrong, auto-bet but nothing to lose
        env.Votes.Cast(round.Id, 3, "cy", winner);                // opted out
        env.Subscriptions.Unsubscribe(3, 0);
        // Player 9 never voted, so gets nothing even though they're subscribed by default.

        await env.Rounds.AdvanceAsync(Local(11, 9), default);

        Assert.Equal([1UL, 2UL], env.Notifier.Sent.Select(s => s.UserId).Order());
        var ann = env.Notifier.Sent.Single(s => s.UserId == 1).Text;
        Assert.Contains("You guessed right!", ann);
        Assert.EndsWith(MessageText.UnsubscribeFooter, ann);
        Assert.Contains("🎲 Bet on", ann);
        Assert.Contains($"+{2 * points} points", ann);
        Assert.StartsWith($"{MessageText.RoundTitle(round)}{'\n'}", ann);
        Assert.Contains($"{round.TeamName(winner)} won with {TurfGuess.Core.Util.Percent.Share(round.WinnerShare)}", ann);
        var card = env.Notifier.Cards.Single(s => s.UserId == 1).Message;
        Assert.Equal((ulong)env.Repo.Get(round.Id)!.RevealMessageId!.Value, card.LinkedMessageId);
        Assert.Equal(winner == Side.Left ? round.LeftColor : round.RightColor, card.ColorHex);
        Assert.True(card.OptOutButton);
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
        env.Notifier.Sent.Clear(); // the "battle is ready" ping
        env.Votes.Cast(round.Id, 1, "ann", round.WinnerSide.Opposite());

        await env.Rounds.AdvanceAsync(now.AddHours(1), default);

        var (userId, text) = Assert.Single(env.Notifier.Sent);
        Assert.Equal(1UL, userId);
        Assert.Contains("Regular Battle", text);
        Assert.Contains("You guessed wrong.", text);
        Assert.DoesNotContain("points", text);
        Assert.DoesNotContain("bet", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_subscriber_who_cannot_be_reached_does_not_stop_the_others_or_the_game()
    {
        using var env = new TestEnvironment();
        await env.Rounds.AdvanceAsync(Local(10, 12), default);
        var round = env.Repo.GetActive()!;
        env.Votes.Cast(round.Id, 1, "ann", round.WinnerSide);
        env.Votes.Cast(round.Id, 2, "bo", round.WinnerSide);
        env.Notifier.Unreachable.Add(1);

        await env.Rounds.AdvanceAsync(Local(11, 9), default);

        Assert.Equal(2UL, Assert.Single(env.Notifier.Sent).UserId);
        Assert.Equal(RoundStatus.Open, env.Repo.GetActive()!.Status); // the next round is already up
        Assert.Contains(env.Log.Messages, m => m.Contains("couldn't send the result to subscriber 1"));
    }
}
