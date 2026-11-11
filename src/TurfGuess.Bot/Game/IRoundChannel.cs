using TurfGuess.Bot.Data;

namespace TurfGuess.Bot.Game;

/// <summary>
/// Everything the game needs from the chat platform. The Discord implementation lives in
/// <c>Discord/DiscordRoundChannel</c>; tests use a fake. Every "find" method lets recovery adopt a
/// message that was posted just before a crash instead of posting it twice.
/// </summary>
public interface IRoundChannel
{
    /// <summary>Looks for an already posted message for this round (by its button ids). Returns the message id.</summary>
    Task<ulong?> FindRoundMessageAsync(long roundId, CancellationToken ct);

    /// <summary>Posts the round image with the two vote buttons. Returns the message id.</summary>
    Task<ulong> PostRoundAsync(RoundRecord round, string imagePath, CancellationToken ct);

    /// <summary>
    /// Disables the buttons and marks the round message closed. <paramref name="voters"/> is null
    /// when the voter list is turned off. Must be safe to call twice.
    /// </summary>
    Task CloseRoundMessageAsync(RoundRecord round, IReadOnlyList<ulong>? voters, CancellationToken ct);

    /// <summary>Updates the list of who has voted on the round message (names only, never their choice).</summary>
    Task UpdateVotersAsync(RoundRecord round, IReadOnlyList<ulong> voters, CancellationToken ct);

    /// <summary>Looks for an already posted reveal for this round. Returns the message id.</summary>
    Task<ulong?> FindRevealMessageAsync(long roundId, CancellationToken ct);

    /// <summary>Posts the reveal (text + image). Returns the message id.</summary>
    Task<ulong> PostRevealAsync(RoundRecord round, RevealSummary summary, string imagePath, CancellationToken ct);
}

/// <summary>Vote results for a reveal.</summary>
public sealed record RevealSummary(int LeftVotes, int RightVotes, IReadOnlyList<ulong> CorrectGuessers)
{
    /// <summary>Players who pressed "Notify me"; they are pinged by the reveal message.</summary>
    public IReadOnlyList<ulong> Notify { get; init; } = [];
}
