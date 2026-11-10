using System.Globalization;
using System.Text;
using TurfGuess.Bot.Data;
using TurfGuess.Core.Grid;

namespace TurfGuess.Bot.Game;

/// <summary>
/// All player-facing text, kept free of Discord types so it can be unit-tested.
/// Timestamps use Discord's &lt;t:unix:style&gt; markup, which every client shows in its own time zone.
/// </summary>
public static class MessageText
{
    /// <summary>Discord's message content limit.</summary>
    public const int DiscordMessageLimit = 2000;

    public static string RoundTitle(string gameName, long roundId) => $"{gameName} · Round #{roundId}";

    /// <summary>Footer on the round message.</summary>
    public static string RoundFooter(long roundId) => $"Round #{roundId}";

    /// <summary>Footer on the reveal; recovery finds an already posted reveal by this exact text.</summary>
    public static string RevealFooter(long roundId) => $"Round #{roundId} · Results";

    public static string ButtonId(long roundId, Side side) => $"vote:{roundId}:{SideCodec.Format(side)}";

    public static string ButtonIdPrefix(long roundId) => $"vote:{roundId}:";

    public static string RoundDescription(RoundRecord round, int? voters, bool closed)
    {
        var sb = new StringBuilder();
        if (closed)
        {
            sb.Append("**Voting is closed.** The results are below.");
        }
        else
        {
            sb.Append("Which color covered more floor? Vote with the buttons below.\n");
            sb.Append($"Results <t:{round.RevealAt}:R> (<t:{round.RevealAt}:f>).");
        }

        if (voters is { } n)
            sb.Append($"\n\n{VoterCount(n)}");
        return sb.ToString();
    }

    public static string VoterCount(int n) => n switch
    {
        0 => "No votes yet.",
        1 => "1 player has voted.",
        _ => $"{n} players have voted.",
    };

    public static string VoteAccepted(RoundRecord round, Side side) =>
        $"You voted {round.TeamEmoji(side)} **{round.TeamName(side)}**. You can change your vote until the reveal.";

    public const string VotingClosed = "Voting for this round is closed.";

    /// <summary>
    /// The reveal text: winner, coverage, vote counts and the correct guessers as mentions
    /// (sent with mentions disabled, so nobody is pinged). The list is cut off at
    /// <paramref name="maxListed"/> names and kept within <paramref name="limit"/> characters.
    /// </summary>
    public static string Reveal(RoundRecord round, RevealSummary s, int maxListed, int limit = DiscordMessageLimit)
    {
        var w = round.WinnerSide;
        var l = Side.Left;
        var r = Side.Right;

        var sb = new StringBuilder();
        sb.Append($"## {round.TeamEmoji(w)} {round.TeamName(w)} wins!\n");
        sb.Append($"{round.TeamEmoji(l)} {round.TeamName(l)} **{Pct(round.ShareLeft)}** · ");
        sb.Append($"{round.TeamEmoji(r)} {round.TeamName(r)} **{Pct(round.ShareRight)}** · ");
        sb.Append($"{Pct(round.Unpainted)} unpainted\n");
        sb.Append($"Votes: {round.TeamEmoji(l)} {s.LeftVotes} · {round.TeamEmoji(r)} {s.RightVotes}\n\n");

        if (s.CorrectGuessers.Count == 0)
        {
            sb.Append(s.LeftVotes + s.RightVotes == 0 ? "Nobody voted this round." : "Nobody guessed right this round.");
            return sb.ToString();
        }

        string header = $"**Correct guessers ({s.CorrectGuessers.Count}):** ";
        sb.Append(header);

        int listed = 0;
        int budget = limit - sb.Length - 30; // room for "…and N more"
        var names = new StringBuilder();
        foreach (var id in s.CorrectGuessers)
        {
            if (listed >= maxListed) break;
            string part = (listed == 0 ? "" : ", ") + $"<@{id}>";
            if (names.Length + part.Length > budget) break;
            names.Append(part);
            listed++;
        }

        sb.Append(names);
        int rest = s.CorrectGuessers.Count - listed;
        if (rest > 0) sb.Append(listed == 0 ? $"{rest} players" : $" …and {rest} more");
        return sb.ToString();
    }

    private static string Pct(double share) => (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
