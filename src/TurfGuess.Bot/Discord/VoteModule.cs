using Discord.Interactions;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;
using TurfGuess.Bot.Options;

namespace TurfGuess.Bot.Discord;

/// <summary>
/// Handles the vote buttons. Buttons are routed by their custom id ("vote:{roundId}:{side}"),
/// not by an in-memory view, so they keep working after a restart.
/// </summary>
public sealed class VoteModule(VoteService votes, RoundRepository repo, BotOptions options) : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("vote:*:*", ignoreGroupNames: true)]
    public async Task VoteAsync(string roundIdText, string sideText)
    {
        if (!VoteService.TryParseButton(roundIdText, sideText, out long roundId, out var side))
        {
            await RespondAsync(MessageText.VotingClosed, ephemeral: true);
            return;
        }

        var result = votes.Cast(roundId, Context.User.Id, Context.User.Username, side);
        var round = result == VoteResult.Accepted ? repo.Get(roundId) : null;

        // Only the voter sees the reply, so nobody can see how others voted.
        await RespondAsync(round is null
            ? MessageText.VotingClosed
            : MessageText.VoteAccepted(round, side, repo.HasBet(roundId, (long)Context.User.Id), options.Generator.Difficulty), ephemeral: true);
    }

    [ComponentInteraction("bet:*", ignoreGroupNames: true)]
    public async Task BetAsync(string roundIdText)
    {
        if (!long.TryParse(roundIdText, out long roundId))
        {
            await RespondAsync(MessageText.VotingClosed, ephemeral: true);
            return;
        }

        bool? bet = votes.ToggleBet(roundId, Context.User.Id, Context.User.Username);
        var round = bet is null ? null : repo.Get(roundId);
        if (bet is null || round is null)
        {
            await RespondAsync(MessageText.VotingClosed, ephemeral: true);
            return;
        }

        bool voted = repo.GetVote(roundId, (long)Context.User.Id) is not null;
        await RespondAsync(MessageText.BetToggled(round, bet.Value, voted, options.Generator.Difficulty), ephemeral: true);
    }
}
