using Discord;
using Discord.WebSocket;
using SkiaSharp;
using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;

namespace TurfGuess.Bot.Discord;

/// <summary>The Discord implementation of <see cref="IPlayerNotifier"/>.</summary>
public sealed class DiscordPlayerNotifier(DiscordSocketClient client, BotOptions options) : IPlayerNotifier
{
    public async Task SendDirectAsync(ulong userId, DirectMessage message, CancellationToken ct)
    {
        var request = new RequestOptions { CancelToken = ct };
        IUser user = client.GetUser(userId) ?? (IUser?)await client.Rest.GetUserAsync(userId, request)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        string description = message.Description;
        if (message.LinkedMessageId is { } linked && await MessageUrlAsync(linked) is { } url)
            description += $"\n[{message.LinkLabel ?? "Go to the message"}]({url})";

        if (message.Title is null)
        {
            await user.SendMessageAsync(description, allowedMentions: AllowedMentions.None, options: request);
            return;
        }

        var embed = new EmbedBuilder().WithTitle(message.Title).WithDescription(description);
        if (message.Footer is not null) embed.WithFooter(message.Footer);
        if (message.ColorHex is not null && SKColor.TryParse(message.ColorHex, out var c))
            embed.WithColor(new Color(c.Red, c.Green, c.Blue));
        await user.SendMessageAsync(embed: embed.Build(), allowedMentions: AllowedMentions.None, options: request,
            components: message.OptOutButton ? OptButton(subscribed: true) : null);
    }

    /// <summary>
    /// The button under a result message: stop the messages while they are on, turn them back on once stopped.
    /// Routed by its custom id, so it keeps working after a restart.
    /// </summary>
    public static MessageComponent OptButton(bool subscribed) => new ComponentBuilder()
        .WithButton(
            subscribed ? MessageText.OptOutButtonLabel : MessageText.OptInButtonLabel,
            subscribed ? MessageText.OptOutButtonId : MessageText.OptInButtonId,
            subscribed ? ButtonStyle.Secondary : ButtonStyle.Success,
            emote: new Emoji(subscribed ? MessageText.OptOutButtonEmoji : MessageText.OptInButtonEmoji))
        .Build();

    /// <summary>A jump link to a message in the game channel, or null when the channel isn't in a server.</summary>
    private async Task<string?> MessageUrlAsync(ulong messageId)
    {
        var channel = client.GetChannel(options.Discord.ChannelId) as IGuildChannel
            ?? await client.Rest.GetChannelAsync(options.Discord.ChannelId) as IGuildChannel;
        return channel is null ? null : $"https://discord.com/channels/{channel.GuildId}/{channel.Id}/{messageId}";
    }
}
