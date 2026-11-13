using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;

namespace TurfGuess.Bot.Discord;

/// <summary>
/// Player commands under <c>/turf</c>: start a 🔫 Regular Battle (a short round that isn't scored) and
/// end it early, plus <c>/turf subscribe</c> and <c>/turf unsubscribe</c> (private result messages; they live here because
/// two modules can't share the group name "turf"). Only one regular battle runs at a time. The daily ⚓ Ranked Battle is unaffected.
/// </summary>
[Group("turf", "Play the daily guessing game")]
public sealed class BattleModule(
    RoundService rounds,
    RoundRepository repo,
    IRoundChannel channelPosts,
    SchedulerSignal scheduler,
    SubscriptionRepository subscriptions,
    IPlayerNotifier notifier,
    TimeProvider clock,
    BotOptions options,
    ILogger<BattleModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("battle", "Start a Regular Battle: a quick round that doesn't count towards the weekly score.")]
    public async Task BattleAsync(
        [Summary("show-votes", "Show who voted for which team while the battle is running (default: no)")] bool showVotes = false)
    {
        if (await WrongChannelAsync()) return;
        if (!TryBeginSetup())
        {
            await RespondAsync(MessageText.BattleSettingUp, ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        try
        {
            var now = clock.GetUtcNow();

            // Today's battle message is reused (reset) instead of posting another one.
            var last = repo.GetLatestPosted(RoundKind.Regular);
            ulong? reuse = last?.MessageId is { } id && SameLocalDay(last.CreatedAtTime, now) ? (ulong)id : null;

            var (round, started) = await rounds.StartBattleAsync(Context.User.Id, showVotes, now, default, reuse);
            bool reused = started && reuse is not null && round.MessageId == (long)reuse;
            logger.LogInformation("{UserName} ({UserId}) ran /turf battle: {Result}.", Context.User.Username, Context.User.Id,
                !started ? $"refused, round {round.Id} is still running"
                : reused ? $"round {round.Id} started over message {reuse}" : $"round {round.Id} started");

            string? link = reused && Context.Guild is { } guild
                ? $"https://discord.com/channels/{guild.Id}/{options.Discord.ChannelId}/{reuse}" : null;
            await FollowupAsync(reused ? MessageText.BattleReusedMessage(round, link) : MessageText.BattleStarted(round, started), ephemeral: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Starting a regular battle for {UserName} ({UserId}) failed.", Context.User.Username, Context.User.Id);
            scheduler.Wake(); // a round that was created but not posted is retried by the scheduler
            await FollowupAsync("Couldn't start the Regular Battle. Please try again in a moment.", ephemeral: true);
        }
        finally { EndSetup(); }
    }

    [SlashCommand("battle-reveal", "Reveal the running Regular Battle now. Only the player who started it, or an admin.")]
    public async Task BattleRevealAsync()
    {
        if (await WrongChannelAsync()) return;

        var round = repo.GetActive(RoundKind.Regular);
        if (round is not { Status: RoundStatus.Open })
        {
            await RespondAsync(MessageText.NoBattleRunning, ephemeral: true);
            return;
        }

        bool isAdmin = Context.User is IGuildUser { GuildPermissions.Administrator: true };
        if (round.StartedBy != (long)Context.User.Id && !isAdmin)
        {
            await RespondAsync(MessageText.BattleRevealNotAllowed, ephemeral: true);
            return;
        }

        if (!rounds.RevealNow(clock.GetUtcNow(), RoundKind.Regular))
        {
            await RespondAsync(MessageText.NoBattleRunning, ephemeral: true);
            return;
        }

        logger.LogInformation("{UserName} ({UserId}) ran /turf battle-reveal; revealing round {RoundId} early.",
            Context.User.Username, Context.User.Id, round.Id);
        scheduler.Wake();
        await RespondAsync(MessageText.BattleRevealing, ephemeral: true);
    }

    [ComponentInteraction("battle-reveal:*", ignoreGroupNames: true)]
    public async Task RevealButtonAsync(string roundIdText)
    {
        var round = repo.GetActive(RoundKind.Regular);
        if (!long.TryParse(roundIdText, out long roundId) || round is not { Status: RoundStatus.Open } || round.Id != roundId)
        {
            await RespondAsync(MessageText.NoBattleRunning, ephemeral: true);
            return;
        }

        bool isAdmin = Context.User is IGuildUser { GuildPermissions.Administrator: true };
        if (round.StartedBy != (long)Context.User.Id && !isAdmin)
        {
            await RespondAsync(MessageText.BattleRevealNotAllowed, ephemeral: true);
            return;
        }

        if (!rounds.RevealNow(clock.GetUtcNow(), RoundKind.Regular))
        {
            await RespondAsync(MessageText.NoBattleRunning, ephemeral: true);
            return;
        }

        logger.LogInformation("{UserName} ({UserId}) pressed Reveal; revealing round {RoundId} early.",
            Context.User.Username, Context.User.Id, round.Id);
        scheduler.Wake();
        await RespondAsync(MessageText.BattleRevealing, ephemeral: true);
    }

    /// <summary>Reset on a finished battle: the same message becomes a fresh battle.</summary>
    [ComponentInteraction("battle-reset:*", ignoreGroupNames: true)]
    public async Task ResetButtonAsync(string roundIdText)
    {
        // Rendering a battle takes a while. While one is being set up, further presses only get a hint to wait.
        if (!TryBeginSetup())
        {
            await RespondAsync(MessageText.BattleSettingUp, ephemeral: true);
            return;
        }

        try
        {
            await ResetAsync(roundIdText);
        }
        finally { EndSetup(); }
    }

    private async Task ResetAsync(string roundIdText)
    {
        var component = (SocketMessageComponent)Context.Interaction;
        ulong messageId = component.Message.Id;
        var running = repo.GetActive(RoundKind.Regular);
        if (running is not null)
        {
            await ShowRevealInsteadAsync(messageId, running);
            return;
        }

        bool showVotes = long.TryParse(roundIdText, out long oldId) && repo.Get(oldId)?.ShowVotes == true;

        // Show a disabled "Setting up…" button right away and tell the presser (privately) to wait.
        await component.UpdateAsync(p => p.Components = new ComponentBuilder()
            .WithButton(MessageText.BattleSettingUpLabel, "battle-setting-up", ButtonStyle.Secondary, disabled: true).Build());
        await FollowupAsync(MessageText.BattleSettingUp, ephemeral: true);
        try
        {
            var (round, started) = await rounds.StartBattleAsync(Context.User.Id, showVotes, clock.GetUtcNow(), default, messageId);
            logger.LogInformation("{UserName} ({UserId}) pressed Reset: {Result}.", Context.User.Username, Context.User.Id,
                started ? $"round {round.Id} started over message {messageId}" : $"refused, round {round.Id} is still running");
            if (!started) await ShowRevealInsteadAsync(messageId, round);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Resetting the Regular Battle for {UserName} ({UserId}) failed.", Context.User.Username, Context.User.Id);
            scheduler.Wake();
            try
            {
                await ModifyOriginalResponseAsync(p => p.Components = new ComponentBuilder()
                    .WithButton(MessageText.BattleResetButtonLabel, MessageText.BattleResetButtonId(long.TryParse(roundIdText, out long id) ? id : 0),
                        ButtonStyle.Success, emote: new Emoji(MessageText.BattleResetButtonEmoji)).Build());
            }
            catch (Exception restore) { logger.LogWarning(restore, "Couldn't restore the Reset button on message {MessageId}.", messageId); }
            await FollowupAsync("Couldn't reset the Regular Battle. Please try again in a moment.", ephemeral: true);
        }
    }

    private async Task ShowRevealInsteadAsync(ulong messageId, RoundRecord running)
    {
        try { await channelPosts.ShowBattleRevealButtonAsync(messageId, running, default); }
        catch (Exception ex) { logger.LogWarning(ex, "Couldn't swap the Reset button for Reveal on message {MessageId}.", messageId); }
        var text = MessageText.BattleAlreadyRunning(running);
        if (Context.Interaction.HasResponded) await FollowupAsync(text, ephemeral: true);
        else await RespondAsync(text, ephemeral: true);
    }

    // One battle is set up at a time; presses and commands that arrive meanwhile are told to wait.
    private static int _settingUp;
    private static bool TryBeginSetup() => Interlocked.CompareExchange(ref _settingUp, 1, 0) == 0;
    private static void EndSetup() => Interlocked.Exchange(ref _settingUp, 0);

    private bool SameLocalDay(DateTimeOffset a, DateTimeOffset b)
    {
        var zone = options.Game.ParsedTimeZone;
        return TimeZoneInfo.ConvertTime(a, zone).Date == TimeZoneInfo.ConvertTime(b, zone).Date;
    }

    /// <summary>Battles are posted in the game's channel, so the commands only work there.</summary>
    private async Task<bool> WrongChannelAsync()
    {
        if (Context.Channel.Id == options.Discord.ChannelId) return false;
        await RespondAsync($"Regular Battles are played in <#{options.Discord.ChannelId}>. Use this command there.", ephemeral: true);
        return true;
    }

    [SlashCommand("subscribe", "Get a private message with your result after every round you voted in.")]
    public async Task SubscribeAsync()
    {
        if (!subscriptions.Subscribe((long)Context.User.Id, clock.GetUtcNow().ToUnixTimeSeconds()))
        {
            await RespondAsync(MessageText.AlreadySubscribed, ephemeral: true);
            return;
        }

        logger.LogInformation("{UserName} ({UserId}) subscribed to result messages.", Context.User.Username, Context.User.Id);
        await DeferAsync(ephemeral: true);

        // Check right away that messages get through, so a closed DM isn't discovered after the next reveal.
        string reply = MessageText.SubscribeConfirmation;
        try { await notifier.SendDirectAsync(Context.User.Id, MessageText.SubscribeTestMessage, default); }
        catch (Exception ex)
        {
            logger.LogWarning("Couldn't send a test message to new subscriber {UserId}: {Reason}", Context.User.Id, ex.Message);
            reply = MessageText.SubscribeNoDms;
        }
        await FollowupAsync(reply, ephemeral: true);
    }

    [SlashCommand("unsubscribe", "Stop the private result messages.")]
    public async Task UnsubscribeAsync()
    {
        bool was = subscriptions.Unsubscribe((long)Context.User.Id);
        if (was) logger.LogInformation("{UserName} ({UserId}) unsubscribed from result messages.", Context.User.Username, Context.User.Id);
        await RespondAsync(was ? MessageText.Unsubscribed : MessageText.NotSubscribed, ephemeral: true);
    }
}
