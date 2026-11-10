using System.Globalization;
using System.Text;
using TurfGuess.Bot.Data;
using TurfGuess.Core.Grid;
using TurfGuess.Core.Scoring;

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

    /// <summary>
    /// How an embed refers to an image attached to the same message. Every embed, including the
    /// ones built for edits, must use this, or Discord shows the attachment separately as well.
    /// </summary>
    public static string AttachmentUrl(string fileName) => $"attachment://{fileName}";

    public static string ButtonId(long roundId, Side side) => $"vote:{roundId}:{SideCodec.Format(side)}";

    public static string ButtonIdPrefix(long roundId) => $"vote:{roundId}:";

    /// <summary>Discord's limit for an embed description.</summary>
    public const int EmbedDescriptionLimit = 4096;

    /// <summary>Most voters named on the round message; the rest are counted.</summary>
    public const int MaxListedVoters = 50;

    /// <summary>
    /// The round message. <paramref name="voters"/> are the players who voted, in display order,
    /// or null to leave the voter line out (Game:ShowVoteCount off). Only who voted, never what.
    /// </summary>
    public static string RoundDescription(RoundRecord round, IReadOnlyList<ulong>? voters, bool closed,
        int maxListed = MaxListedVoters, int limit = EmbedDescriptionLimit)
    {
        var sb = new StringBuilder();
        if (closed)
        {
            sb.Append("**Voting is closed.** The results are below.\n");
            sb.Append(DifficultyLine(round));
        }
        else
        {
            sb.Append("Which team do you think painted more? Cast your vote!\n");
            sb.Append(DifficultyLine(round)).Append('\n');
            sb.Append($"Results <t:{round.RevealAt}:R> (<t:{round.RevealAt}:f>).");
        }

        if (voters is null) return sb.ToString();

        sb.Append("\n\n");
        if (voters.Count == 0)
        {
            sb.Append("No votes yet.");
            return sb.ToString();
        }

        sb.Append(voters.Count == 1 ? "**1 player has voted:** " : $"**{voters.Count} players have voted:** ");
        AppendMentions(sb, voters, maxListed, limit);
        return sb.ToString();
    }

    /// <summary>
    /// How close the round is, as a rank from C (easiest) to X (hardest). It says nothing about
    /// who won, so it's shown while voting is open.
    /// </summary>
    public static string DifficultyLine(RoundRecord round)
    {
        var rank = Difficulty.RankFor(round.WinnerShare);
        int flames = (int)rank + 1; // C = 1 … X = 5
        return $"{rank}-rank difficulty {string.Concat(Enumerable.Repeat("🔥", flames))}";
    }

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

        sb.Append($"**Correct guessers ({s.CorrectGuessers.Count}):** ");
        AppendMentions(sb, s.CorrectGuessers, maxListed, limit);
        return sb.ToString();
    }

    /// <summary>
    /// Appends players as mentions (the bot sends them with mentions disabled, so nobody is pinged),
    /// at most <paramref name="maxListed"/> of them and keeping the whole text within
    /// <paramref name="limit"/> characters. Anyone left out is counted: "…and N more".
    /// </summary>
    private static void AppendMentions(StringBuilder sb, IReadOnlyList<ulong> ids, int maxListed, int limit)
    {
        int listed = 0;
        int budget = limit - sb.Length - 30; // room for "…and N more"
        var names = new StringBuilder();
        foreach (var id in ids)
        {
            if (listed >= maxListed) break;
            string part = (listed == 0 ? "" : ", ") + $"<@{id}>";
            if (names.Length + part.Length > budget) break;
            names.Append(part);
            listed++;
        }

        sb.Append(names);
        int rest = ids.Count - listed;
        if (rest > 0) sb.Append(listed == 0 ? $"{rest} players" : $" …and {rest} more");
    }

    private static string Pct(double share) => (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
