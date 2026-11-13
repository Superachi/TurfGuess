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

    public async Task<ulong?> FindRoundMessageAsync(RoundRecord round, CancellationToken ct)
    {
        string prefix = MessageText.ButtonIdPrefix(round.Id);
        foreach (var m in await RecentOwnMessagesAsync(round, ct))
            if (ButtonIds(m.Components).Any(id => id.StartsWith(prefix, StringComparison.Ordinal)))
                return m.Id;
        return null;
    }

    public async Task<ulong> PostRoundAsync(RoundRecord round, string imagePath, CancellationToken ct)
    {
        var channel = await ChannelAsync();
        string fileName = Path.GetFileName(imagePath);
        IReadOnlyList<Voter>? voters = options.Game.ShowVoteCount || round.ShowVotes ? [] : null;

        var embed = RoundEmbed(round, MessageText.RoundDescription(round, voters, closed: false, difficulty: options.Generator.Difficulty), fileName);

        using var file = new FileAttachment(imagePath, fileName);
        var message = await channel.SendFileAsync(file, embed: embed, components: Buttons(round, disabled: false),
            allowedMentions: AllowedMentions.None, options: Request(ct));
        return message.Id;
    }

    public async Task CloseRoundMessageAsync(RoundRecord round, IReadOnlyList<Voter>? voters, CancellationToken ct)
    {
        var message = await RoundMessageAsync(round, ct);
        if (message is null) return;
        await message.ModifyAsync(p =>
        {
            p.Components = Buttons(round, disabled: true);
            p.Embed = RoundEmbed(round, MessageText.RoundDescription(round, voters, closed: true, difficulty: options.Generator.Difficulty), AttachedFileName(message, round));
        }, Request(ct));
    }

    public async Task UpdateVotersAsync(RoundRecord round, IReadOnlyList<Voter> voters, CancellationToken ct)
    {
        var message = await RoundMessageAsync(round, ct);
        if (message is null) return;
        await message.ModifyAsync(
            p => p.Embed = RoundEmbed(round, MessageText.RoundDescription(round, voters, closed: false, difficulty: options.Generator.Difficulty), AttachedFileName(message, round)),
            Request(ct));
    }

    public async Task CancelRoundMessageAsync(RoundRecord round, CancellationToken ct)
    {
        var message = await RoundMessageAsync(round, ct);
        if (message is null) return;
        await message.ModifyAsync(p =>
        {
            p.Components = Buttons(round, disabled: true);
            p.Embed = RoundEmbed(round, MessageText.RoundCancelled, AttachedFileName(message, round));
        }, Request(ct));
    }

    public async Task<ulong?> FindRevealMessageAsync(RoundRecord round, CancellationToken ct)
    {
        string footer = MessageText.RevealFooter(round);
        foreach (var m in await RecentOwnMessagesAsync(round, ct))
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
            .WithImageUrl(MessageText.AttachmentUrl(fileName))
            .WithFooter(MessageText.RevealFooter(round))
            .Build();

        using var file = new FileAttachment(imagePath, fileName);
        // Mentions are disabled, so the correct guessers are named without being pinged.
        var message = await channel.SendFileAsync(file, embed: embed, allowedMentions: AllowedMentions.None, options: Request(ct));
        return message.Id;
    }

    public async Task PostWeekSummaryAsync(string text, CancellationToken ct)
    {
        var channel = await ChannelAsync();
        await channel.SendMessageAsync(text, allowedMentions: AllowedMentions.None, options: Request(ct));
    }

    // ---------------------------------------------------------------------------------------

    private async Task<IMessageChannel> ChannelAsync()
    {
        if (client.GetChannel(options.Discord.ChannelId) is IMessageChannel cached) return cached;
        return await client.Rest.GetChannelAsync(options.Discord.ChannelId) as IMessageChannel
               ?? throw new InvalidOperationException($"Channel {options.Discord.ChannelId} not found, or it isn't a text channel.");
    }

    private async Task<IReadOnlyList<IMessage>> RecentOwnMessagesAsync(RoundRecord round, CancellationToken ct)
    {
        var channel = await ChannelAsync();
        var messages = await channel.GetMessagesAsync(RecoveryScanLimit, options: Request(ct)).FlattenAsync();
        return messages.Where(m => m.Author.Id == client.CurrentUser.Id && MessageText.IsFromThisRound(m.Timestamp, round)).ToList();
    }

    private async Task<IUserMessage?> RoundMessageAsync(RoundRecord round, CancellationToken ct)
    {
        if (round.MessageId is not { } id) return null;
        var channel = await ChannelAsync();
        return await channel.GetMessageAsync((ulong)id, options: Request(ct)) as IUserMessage;
    }

    /// <summary>Keeps the original embed (and its image) and swaps the description.</summary>
    /// <summary>
    /// The round embed, built the same way for the first post and for every edit. The image must
    /// always point at the message's own attachment (<c>attachment://file.png</c>): an edit that
    /// copies the existing embed gets the image back as a CDN link, and Discord then shows the
    /// now-unreferenced attachment a second time above the embed.
    /// </summary>
    private Embed RoundEmbed(RoundRecord round, string description, string fileName) => new EmbedBuilder()
        .WithTitle(MessageText.RoundTitle(options.Game.Name, round))
        .WithDescription(description)
        .WithImageUrl(MessageText.AttachmentUrl(fileName))
        .WithFooter(MessageText.RoundFooter(round))
        .Build();

    /// <summary>The file name of the image attached to the round message (kept on edits).</summary>
    private static string AttachedFileName(IUserMessage message, RoundRecord round) =>
        message.Attachments.FirstOrDefault()?.Filename ?? Path.GetFileName(round.ImagePath);

    /// <summary>The two team buttons, plus Bet for ranked rounds (regular battles aren't scored, so there is nothing to bet on).</summary>
    private static MessageComponent Buttons(RoundRecord round, bool disabled)
    {
        var builder = new ComponentBuilder()
            .WithButton(round.LeftName, MessageText.ButtonId(round.Id, Side.Left), ButtonStyle.Secondary,
                emote: new Emoji(round.LeftEmoji), disabled: disabled)
            .WithButton(round.RightName, MessageText.ButtonId(round.Id, Side.Right), ButtonStyle.Secondary,
                emote: new Emoji(round.RightEmoji), disabled: disabled);
        if (round.Kind == RoundKind.Ranked)
            builder.WithButton(MessageText.BetButtonLabel, MessageText.BetButtonId(round.Id), ButtonStyle.Primary,
                emote: new Emoji(MessageText.BetButtonEmoji), disabled: disabled);
        return builder.Build();
    }

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
