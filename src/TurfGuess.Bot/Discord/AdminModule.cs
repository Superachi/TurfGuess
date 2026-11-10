using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;

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
            $"**Round #{round.Id}** · {round.Status}\n" +
            $"{round.LeftEmoji} {round.LeftName} vs {round.RightEmoji} {round.RightName}\n" +
            $"Reveal: <t:{round.RevealAt}:f> (<t:{round.RevealAt}:R>)\n" +
            $"Votes so far: {repo.CountVotes(round.Id)}";
        await RespondAsync(text, ephemeral: true);
    }
}
