using TurfGuess.Bot.Data;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Game;

public enum VoteResult
{
    Accepted,
    Closed,
}

/// <summary>Records votes. Closing is enforced by the database, not by disabled buttons.</summary>
public sealed class VoteService(RoundRepository repo, TimeProvider clock)
{
    /// <summary>Raised after a vote is recorded, with the round id (used for the live vote counter).</summary>
    public event Action<long>? VoteRecorded;

    public VoteResult Cast(long roundId, ulong userId, Side side)
    {
        bool ok = repo.TryUpsertVote(roundId, (long)userId, side, clock.GetUtcNow().ToUnixTimeSeconds());
        if (!ok) return VoteResult.Closed;
        VoteRecorded?.Invoke(roundId);
        return VoteResult.Accepted;
    }

    /// <summary>Parses a button id like "vote:12:left".</summary>
    public static bool TryParseButton(string roundIdText, string sideText, out long roundId, out Side side)
    {
        side = Side.Left;
        return long.TryParse(roundIdText, out roundId) && SideCodec.TryParse(sideText, out side);
    }
}
