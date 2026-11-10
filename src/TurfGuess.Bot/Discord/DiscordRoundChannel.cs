using Discord;
using Discord.WebSocket;
using SkiaSharp;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Discord;

/// <summary>The Discord implementation of <see cref="IRoundChannel"/>.</summary>
public sealed class DiscordRoundChannel(DiscordSocketClient client, BotOptions options) : IRoundChannel
{
    /// <summary>How many recent messages recovery looks through for an already posted message.</summary>
    private const int RecoveryScanLimit = 50;

    public async Task<ulong?> FindRoundMessageAsync(long roundId, CancellationToken ct)
    {
        string prefix = MessageText.ButtonIdPrefix(roundId);
        foreach (var m in await RecentOwnMessagesAsync(ct))
            if (ButtonIds(m.Components).Any(id => id.StartsWith(prefix, StringComparison.Ordinal)))
                return m.Id;
        return null;
    }

    public async Task<ulong> PostRoundAsync(RoundRecord round, string imagePath, CancellationToken ct)
    {
        var channel = await ChannelAsync();
        string fileName = Path.GetFileName(imagePath);
        int? voters = options.Game.ShowVoteCount ? 0 : null;

        var embed = new EmbedBuilder()
            .WithTitle(MessageText.RoundTitle(options.Game.Name, round.Id))
            .WithDescription(MessageText.RoundDescription(round, voters, closed: false))
            .WithImageUrl($"attachment://{fileName}")
            .WithFooter(MessageText.RoundFooter(round.Id))
            .Build();

        using var file = new FileAttachment(imagePath, fileName);
        var message = await channel.SendFileAsync(file, embed: embed, components: Buttons(round, disabled: false),
            allowedMentions: AllowedMentions.None, options: Request(ct));
        return message.Id;
    }

    public async Task CloseRoundMessageAsync(RoundRecord round, int? voters, CancellationToken ct)
    {
        var message = await RoundMessageAsync(round, ct);
        if (message is null) return;
        await message.ModifyAsync(p =>
        {
            p.Components = Buttons(round, disabled: true);
            p.Embed = RebuildEmbed(message, MessageText.RoundDescription(round, voters, closed: true));
        }, Request(ct));
    }

    public async Task UpdateVoteCountAsync(RoundRecord round, int voters, CancellationToken ct)
    {
        var message = await RoundMessageAsync(round, ct);
        if (message is null) return;
        await message.ModifyAsync(p => p.Embed = RebuildEmbed(message, MessageText.RoundDescription(round, voters, closed: false)),
            Request(ct));
    }

    public async Task<ulong?> FindRevealMessageAsync(long roundId, CancellationToken ct)
    {
        string footer = MessageText.RevealFooter(roundId);
        foreach (var m in await RecentOwnMessagesAsync(ct))
            if (m.Embeds.Any(e => e.Footer?.Text == footer))
                return m.Id;
        return null;
    }

    public async Task<ulong> PostRevealAsync(RoundRecord round, RevealSummary summary, string imagePath, CancellationToken ct)
    {
        var channel = await ChannelAsync();
        string fileName = Path.GetFileName(imagePath);
        var winnerColor = SKColor.Parse(round.WinnerSide == Side.Left ? round.LeftColor : round.RightColor);

        var embed = new EmbedBuilder()
            .WithDescription(MessageText.Reveal(round, summary, options.Game.MaxListedWinners))
            .WithColor(new Color(winnerColor.Red, winnerColor.Green, winnerColor.Blue))
            .WithImageUrl($"attachment://{fileName}")
            .WithFooter(MessageText.RevealFooter(round.Id))
            .Build();

        using var file = new FileAttachment(imagePath, fileName);
        // Mentions are disabled, so the correct guessers are named without being pinged.
        var message = await channel.SendFileAsync(file, embed: embed, allowedMentions: AllowedMentions.None, options: Request(ct));
        return message.Id;
    }

    // ---------------------------------------------------------------------------------------

    private async Task<IMessageChannel> ChannelAsync()
    {
        if (client.GetChannel(options.Discord.ChannelId) is IMessageChannel cached) return cached;
        return await client.Rest.GetChannelAsync(options.Discord.ChannelId) as IMessageChannel
               ?? throw new InvalidOperationException($"Channel {options.Discord.ChannelId} not found, or it isn't a text channel.");
    }

    private async Task<IReadOnlyList<IMessage>> RecentOwnMessagesAsync(CancellationToken ct)
    {
        var channel = await ChannelAsync();
        var messages = await channel.GetMessagesAsync(RecoveryScanLimit, options: Request(ct)).FlattenAsync();
        return messages.Where(m => m.Author.Id == client.CurrentUser.Id).ToList();
    }

    private async Task<IUserMessage?> RoundMessageAsync(RoundRecord round, CancellationToken ct)
    {
        if (round.MessageId is not { } id) return null;
        var channel = await ChannelAsync();
        return await channel.GetMessageAsync((ulong)id, options: Request(ct)) as IUserMessage;
    }

    /// <summary>Keeps the original embed (and its image) and swaps the description.</summary>
    private static Embed RebuildEmbed(IUserMessage message, string description)
    {
        var builder = message.Embeds.FirstOrDefault()?.ToEmbedBuilder() ?? new EmbedBuilder();
        return builder.WithDescription(description).Build();
    }

    private static MessageComponent Buttons(RoundRecord round, bool disabled) => new ComponentBuilder()
        .WithButton(round.LeftName, MessageText.ButtonId(round.Id, Side.Left), ButtonStyle.Secondary,
            emote: new Emoji(round.LeftEmoji), disabled: disabled)
        .WithButton(round.RightName, MessageText.ButtonId(round.Id, Side.Right), ButtonStyle.Secondary,
            emote: new Emoji(round.RightEmoji), disabled: disabled)
        .Build();

    private static IEnumerable<string> ButtonIds(IEnumerable<IMessageComponent> components)
    {
        foreach (var c in components)
        {
            switch (c)
            {
                case ActionRowComponent row:
                    foreach (var id in ButtonIds(row.Components)) yield return id;
                    break;
                case ButtonComponent { CustomId: { } id }:
                    yield return id;
                    break;
            }
        }
    }

    private static RequestOptions Request(CancellationToken ct) => new() { CancelToken = ct };
}
