namespace TurfGuess.Bot.Game;

/// <summary>
/// A private message to one player. With a <see cref="Title"/> it is shown as a small card (like the round
/// messages in the channel), otherwise as plain text.
/// </summary>
/// <param name="Title">Card title, or null for a plain text message.</param>
/// <param name="Description">The body. Markdown works.</param>
/// <param name="Footer">Small print under the card.</param>
/// <param name="ColorHex">Accent colour of the card, like <c>#3366ff</c>.</param>
/// <param name="LinkedMessageId">A message in the game channel to link to, with <paramref name="LinkLabel"/> as the link text.</param>
/// <param name="LinkLabel">Text of the link to <paramref name="LinkedMessageId"/>.</param>
/// <param name="LinkedChannelId">The channel that message is in. Null means the game's configured channel.</param>
/// <param name="OptOutButton">Adds a button that lets the player stop (and later resume) these messages right here.</param>
public sealed record DirectMessage(
    string? Title,
    string Description,
    string? Footer = null,
    string? ColorHex = null,
    ulong? LinkedMessageId = null,
    string? LinkLabel = null,
    bool OptOutButton = false,
    ulong? LinkedChannelId = null)
{
    public static DirectMessage Plain(string text) => new(null, text);
}

/// <summary>
/// Sends a player a direct message. Used for the result messages (<c>/turf subscribe</c>): a bot can only
/// send an ephemeral message as the reply to a player's own action, so an unprompted ping has to be a DM.
/// </summary>
public interface IPlayerNotifier
{
    /// <summary>Sends the message. Throws when the player can't be reached (for example, DMs from server members are off).</summary>
    Task SendDirectAsync(ulong userId, DirectMessage message, CancellationToken ct);
}
