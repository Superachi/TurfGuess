using Discord;
using Discord.Interactions;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;

namespace TurfGuess.Bot.Discord;

/// <summary>
/// Admin slash commands under <c>/turf-admin</c>. A separate top-level command, so it can be limited
/// to administrators while future player commands (<c>/turf stats</c>, <c>/turf leaderboard</c>) stay public.
/// </summary>
[Group("turf-admin", "Turf Guess admin commands")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class AdminModule(
    RoundService rounds,
    RoundRepository repo,
    SchedulerSignal scheduler,
    TimeProvider clock) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("reveal-now", "Reveal the current round now and post the next one.")]
    public async Task RevealNowAsync()
    {
        if (!rounds.RevealNow(clock.GetUtcNow()))
        {
            await RespondAsync("There's no open round to reveal right now.", ephemeral: true);
            return;
        }

        scheduler.Wake();
        await RespondAsync("Revealing the current round now. The next round follows straight after.", ephemeral: true);
    }

    [SlashCommand("status", "Show the current round.")]
    public async Task StatusAsync()
    {
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
