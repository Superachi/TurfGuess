using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using Discord.WebSocket;

namespace TurfGuess.Bot.Discord;

/// <summary>
/// Admin slash commands under <c>/turf-admin</c>. A separate top-level command, so it can be limited
/// to administrators while future player commands (<c>/turf stats</c>, <c>/turf leaderboard</c>) stay public.
/// </summary>
[Group("turf-admin", "Admin commands for the daily guessing game")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class AdminModule(
    RoundService rounds,
    RoundRepository repo,
    ScoreRepository scores,
    SchedulerSignal scheduler,
    TimeProvider clock,
    ILogger<AdminModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("reveal-now", "Reveal the current round now and post the next one.")]
    public async Task RevealNowAsync()
    {
        if (!rounds.RevealNow(clock.GetUtcNow()))
        {
            logger.LogInformation("Admin {UserName} ({UserId}) ran /turf-admin reveal-now, but no round was open.",
                Context.User.Username, Context.User.Id);
            await RespondAsync("There's no open round to reveal right now.", ephemeral: true);
            return;
        }

        logger.LogInformation("Admin {UserName} ({UserId}) ran /turf-admin reveal-now; revealing the current round early.",
            Context.User.Username, Context.User.Id);
        scheduler.Wake();
        await RespondAsync("Revealing the current round now. The next round follows straight after.", ephemeral: true);
    }

    [SlashCommand("reset-round", "Cancel the current round and start a new one. Weekly scores are kept.")]
    public async Task ResetRoundAsync()
    {
        await DeferAsync(ephemeral: true);
        var cancelled = await rounds.ResetRoundAsync(default);
        logger.LogInformation("Admin {UserName} ({UserId}) ran /turf-admin reset-round; {Result}.",
            Context.User.Username, Context.User.Id, cancelled is null ? "there was no active round" : $"round {cancelled.Id} was cancelled");

        scheduler.Wake();
        await FollowupAsync(cancelled is null
            ? "There was no active round. A new one is posted straight away."
            : $"Round #{cancelled.DisplayNumber} is cancelled and its votes are dropped. A new round is posted straight away. Weekly scores are unchanged.",
            ephemeral: true);
    }

    private const string WipeConfirmId = "admin-wipe-scores:confirm";
    private const string WipeCancelId = "admin-wipe-scores:cancel";

    [SlashCommand("reset-scores", "Wipe all weekly points, wins and trophies (asks for confirmation).")]
    public async Task ResetScoresAsync()
    {
        var (results, players, weeks) = scores.Counts();
        logger.LogInformation("Admin {UserName} ({UserId}) ran /turf-admin reset-scores (asking for confirmation).", Context.User.Username, Context.User.Id);

        var buttons = new ComponentBuilder()
            .WithButton("Wipe all scores", WipeConfirmId, ButtonStyle.Danger)
            .WithButton("Cancel", WipeCancelId, ButtonStyle.Secondary)
            .Build();
        await RespondAsync(
            $"**Wipe all weekly scores?** This deletes the points and wins of {players} player(s) ({results} results), " +
            $"{weeks} finished week(s) and the trophy tally. Rounds are kept. This can't be undone.",
            components: buttons, ephemeral: true);
    }

    [ComponentInteraction(WipeConfirmId, ignoreGroupNames: true)]
    public async Task ConfirmWipeAsync()
    {
        if (Context.User is not IGuildUser { GuildPermissions.Administrator: true })
        {
            await RespondAsync("Only administrators can do this.", ephemeral: true);
            return;
        }

        scores.WipeAll();
        logger.LogWarning("Admin {UserName} ({UserId}) wiped all weekly scores.", Context.User.Username, Context.User.Id);
        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Content = "All weekly scores are wiped. Everyone starts again from 0.";
            p.Components = new ComponentBuilder().Build();
        });
    }

    [ComponentInteraction(WipeCancelId, ignoreGroupNames: true)]
    public async Task CancelWipeAsync() =>
        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(p =>
        {
            p.Content = "Cancelled. The scores are unchanged.";
            p.Components = new ComponentBuilder().Build();
        });

    [SlashCommand("status", "Show the current round.")]
    public async Task StatusAsync()
    {
        logger.LogInformation("Admin {UserName} ({UserId}) ran /turf-admin status.", Context.User.Username, Context.User.Id);
        var round = repo.GetActive();
        if (round is null)
        {
            await RespondAsync("No active round. One is created on the scheduler's next run.", ephemeral: true);
            return;
        }

        string text =
            $"**Round #{round.DisplayNumber}** · {round.Status}\n" +
            $"{round.LeftEmoji} {round.LeftName} vs {round.RightEmoji} {round.RightName}\n" +
            $"Reveal: <t:{round.RevealAt}:f> (<t:{round.RevealAt}:R>)\n" +
            $"Votes so far: {repo.CountVotes(round.Id)}";
        await RespondAsync(text, ephemeral: true);
    }
}
