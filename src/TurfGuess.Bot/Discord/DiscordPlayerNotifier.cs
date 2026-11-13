using Discord;
using Discord.WebSocket;
using TurfGuess.Bot.Game;

namespace TurfGuess.Bot.Discord;

/// <summary>The Discord implementation of <see cref="IPlayerNotifier"/>.</summary>
public sealed class DiscordPlayerNotifier(DiscordSocketClient client) : IPlayerNotifier
{
    public async Task SendDirectAsync(ulong userId, string text, CancellationToken ct)
    {
        var options = new RequestOptions { CancelToken = ct };
        IUser user = client.GetUser(userId) ?? (IUser?)await client.Rest.GetUserAsync(userId, options)
            ?? throw new InvalidOperationException($"User {userId} not found.");
        await user.SendMessageAsync(text, allowedMentions: AllowedMentions.None, options: options);
    }
}
