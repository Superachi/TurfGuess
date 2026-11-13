using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;

namespace TurfGuess.Bot.Discord;

/// <summary>
/// Player commands under <c>/turf</c>: start a 🔫 Regular Battle (a short round that isn't scored) and
/// end it early. Only one regular battle runs at a time. The daily ⚓ Ranked Battle is unaffected.
/// </summary>
[Group("turf", "Play the daily guessing game")]
public sealed class BattleModule(
    RoundService rounds,
    RoundRepository repo,
    SchedulerSignal scheduler,
    TimeProvider clock,
    BotOptions options,
    ILogger<BattleModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("battle", "Start a Regular Battle: a quick round that doesn't count towards the weekly score.")]
    public async Task BattleAsync(
        [Summary("show-votes", "Show who voted for which team while the battle is running (default: no)")] bool showVotes = false)
    {
        if (await WrongChannelAsync()) return;

        await DeferAsync(ephemeral: true);
        try
        {
            var (round, started) = await rounds.StartBattleAsync(Context.User.Id, showVotes, clock.GetUtcNow(), default);
            logger.LogInformation("{UserName} ({UserId}) ran /turf battle: {Result}.", Context.User.Username, Context.User.Id,
                started ? $"round {round.Id} started" : $"refused, round {round.Id} is still running");

            await FollowupAsync(MessageText.BattleStarted(round, started), ephemeral: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Starting a regular battle for {UserName} ({UserId}) failed.", Context.User.Username, Context.User.Id);
            scheduler.Wake(); // a round that was created but not posted is retried by the scheduler
            await FollowupAsync("Couldn't start the Regular Battle. Please try again in a moment.", ephemeral: true);
        }
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

    /// <summary>Battles are posted in the game's channel, so the commands only work there.</summary>
    private async Task<bool> WrongChannelAsync()
    {
        if (Context.Channel.Id == options.Discord.ChannelId) return false;
        await RespondAsync($"Regular Battles are played in <#{options.Discord.ChannelId}>. Use this command there.", ephemeral: true);
        return true;
    }
}
