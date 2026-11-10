using Discord.Interactions;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Game;

namespace TurfGuess.Bot.Discord;

/// <summary>
/// Handles the vote buttons. Buttons are routed by their custom id ("vote:{roundId}:{side}"),
/// not by an in-memory view, so they keep working after a restart.
/// </summary>
public sealed class VoteModule(VoteService votes, RoundRepository repo) : InteractionModuleBase<SocketInteractionContext>
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
        await RespondAsync(round is null ? MessageText.VotingClosed : MessageText.VoteAccepted(round, side), ephemeral: true);
    }
}
