using TurfGuess.Bot.Data;

namespace TurfGuess.Bot.Game;

/// <summary>
/// Everything the game needs from the chat platform. The Discord implementation lives in
/// <c>Discord/DiscordRoundChannel</c>; tests use a fake. Every "find" method lets recovery adopt a
/// message that was posted just before a crash instead of posting it twice.
/// </summary>
public interface IRoundChannel
{
    /// <summary>
    /// Looks for an already posted message for this round (by its button ids). Only messages from
    /// after the round was created count, so leftovers of an earlier game are ignored. Returns the message id.
    /// </summary>
    Task<ulong?> FindRoundMessageAsync(RoundRecord round, CancellationToken ct);

    /// <summary>
    /// Posts the round image with the vote buttons. Returns the message id. With <paramref name="replaceMessageId"/>
    /// the round is written over that existing message (a Regular Battle reset) instead of a new post.
    /// </summary>
    Task<ulong> PostRoundAsync(RoundRecord round, string imagePath, CancellationToken ct, ulong? replaceMessageId = null);

    /// <summary>Turns a finished Regular Battle's message (in <paramref name="channelId"/>) into a Reveal button, because a battle is running elsewhere.</summary>
    Task ShowBattleRevealButtonAsync(ulong channelId, ulong messageId, RoundRecord runningBattle, CancellationToken ct);

    /// <summary>
    /// Disables the buttons and marks the round message closed. <paramref name="voters"/> is null
    /// when the voter list is turned off. Must be safe to call twice.
    /// </summary>
    Task CloseRoundMessageAsync(RoundRecord round, IReadOnlyList<Voter>? voters, CancellationToken ct);

    /// <summary>Updates the list of who has voted on the round message (names and bets only, never their choice).</summary>
    Task UpdateVotersAsync(RoundRecord round, IReadOnlyList<Voter> voters, CancellationToken ct);

    /// <summary>Looks for an already posted reveal for this round (same age rule as above). Returns the message id.</summary>
    Task<ulong?> FindRevealMessageAsync(RoundRecord round, CancellationToken ct);

    /// <summary>Disables the buttons of a round that an admin cancelled and says so on the message.</summary>
    Task CancelRoundMessageAsync(RoundRecord round, CancellationToken ct);

    /// <summary>Posts the reveal (text + image). Returns the message id.</summary>
    Task<ulong> PostRevealAsync(RoundRecord round, RevealSummary summary, string imagePath, CancellationToken ct);

    /// <summary>Posts the summary of a finished week in the ranked channel (text only, nobody is pinged).</summary>
    Task PostWeekSummaryAsync(string text, CancellationToken ct);
}

/// <summary>Vote results for a reveal.</summary>
public sealed record RevealSummary(
    int LeftVotes,
    int RightVotes,
    IReadOnlyList<PlayerOutcome> Winners,
    IReadOnlyList<PlayerOutcome> Losers,
    IReadOnlyList<StandingEntry> Standings);
