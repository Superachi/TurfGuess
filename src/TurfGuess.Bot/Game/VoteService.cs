using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Data;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Game;

public enum VoteResult
{
    Accepted,
    Closed,
}

/// <summary>Records votes. Closing is enforced by the database, not by disabled buttons.</summary>
public sealed class VoteService(RoundRepository repo, TimeProvider clock, ILogger<VoteService> logger)
{
    /// <summary>Raised after a vote is recorded, with the round id (used for the live vote counter).</summary>
    public event Action<long>? VoteRecorded;

    public VoteResult Cast(long roundId, ulong userId, string userName, Side side)
    {
        var previous = repo.GetVote(roundId, (long)userId);
        bool ok = repo.TryUpsertVote(roundId, (long)userId, userName, side, clock.GetUtcNow().ToUnixTimeSeconds());

        if (!ok)
        {
            logger.LogInformation("Round {RoundId}: {UserName} ({UserId}) clicked {Side} after voting closed; rejected.",
                roundId, userName, userId, SideCodec.Format(side));
            return VoteResult.Closed;
        }

        var round = repo.Get(roundId);
        string team = round is null ? SideCodec.Format(side) : round.TeamName(side);
        if (previous is null)
            logger.LogInformation("Round {RoundId}: {UserName} ({UserId}) voted {Team}.", roundId, userName, userId, team);
        else if (previous.ChoiceSide != side)
            logger.LogInformation("Round {RoundId}: {UserName} ({UserId}) changed their vote from {OldTeam} to {Team}.",
                roundId, userName, userId, round is null ? previous.Choice : round.TeamName(previous.ChoiceSide), team);
        else
            logger.LogInformation("Round {RoundId}: {UserName} ({UserId}) voted {Team} again (no change).", roundId, userName, userId, team);

        VoteRecorded?.Invoke(roundId);
        return VoteResult.Accepted;
    }

    /// <summary>
    /// Flips the player's bet on the round. Returns the new state (true = betting) or null when
    /// voting is closed. A bet only counts if the player has also voted when the round is revealed.
    /// </summary>
    public bool? ToggleBet(long roundId, ulong userId, string userName)
    {
        bool? bet = repo.ToggleBet(roundId, (long)userId);
        if (bet is null)
        {
            logger.LogInformation("Round {RoundId}: {UserName} ({UserId}) pressed Bet after voting closed; rejected.", roundId, userName, userId);
            return null;
        }

        logger.LogInformation("Round {RoundId}: {UserName} ({UserId}) turned their bet {State}.", roundId, userName, userId, bet.Value ? "on" : "off");
        VoteRecorded?.Invoke(roundId);
        return bet;
    }

    /// <summary>Parses a button id like "vote:12:left".</summary>
    public static bool TryParseButton(string roundIdText, string sideText, out long roundId, out Side side)
    {
        side = Side.Left;
        return long.TryParse(roundIdText, out roundId) && SideCodec.TryParse(sideText, out side);
    }
}
