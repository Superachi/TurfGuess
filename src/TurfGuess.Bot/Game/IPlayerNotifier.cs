namespace TurfGuess.Bot.Game;

/// <summary>
/// Sends a player a direct message. Used for the result pings of <c>/turf subscribe</c>: a bot can only
/// send an ephemeral message as the reply to a player's own action, so an unprompted ping has to be a DM.
/// </summary>
public interface IPlayerNotifier
{
    /// <summary>Sends the text. Throws when the player can't be reached (for example, DMs from server members are off).</summary>
    Task SendDirectAsync(ulong userId, string text, CancellationToken ct);
}
