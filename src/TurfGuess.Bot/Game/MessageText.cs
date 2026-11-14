using System.Globalization;
using System.Text;
using TurfGuess.Bot.Data;
using TurfGuess.Core;
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

    public const string RankedEmoji = "⚓";
    public const string RegularEmoji = "🔫";

    /// <summary>"⚓ Ranked Battle" or "🔫 Regular Battle".</summary>
    public static string KindName(RoundKind kind) =>
        kind == RoundKind.Regular ? $"{RegularEmoji} Regular Battle" : $"{RankedEmoji} Ranked Battle";

    public static string RoundTitle(RoundRecord round) =>
        $"{KindName(round.Kind)} · Round #{round.DisplayNumber}";

    /// <summary>Footer on the round message.</summary>
    public static string RoundFooter(RoundRecord round) =>
        round.Kind == RoundKind.Regular ? $"Regular Battle #{round.DisplayNumber}" : $"Round #{round.DisplayNumber}";

    /// <summary>Footer on the reveal; recovery finds an already posted reveal by this exact text.</summary>
    public static string RevealFooter(RoundRecord round) =>
        round.Kind == RoundKind.Regular ? $"Regular Battle #{round.DisplayNumber} · Results" : $"Round #{round.DisplayNumber} · Results";

    /// <summary>
    /// How an embed refers to an image attached to the same message. Every embed, including the
    /// ones built for edits, must use this, or Discord shows the attachment separately as well.
    /// </summary>
    public static string AttachmentUrl(string fileName) => $"attachment://{fileName}";

    public static string ButtonId(long roundId, Side side) => $"vote:{roundId}:{SideCodec.Format(side)}";

    public static string ButtonIdPrefix(long roundId) => $"vote:{roundId}:";

    public static string BetButtonId(long roundId) => $"bet:{roundId}";

    public const string BetButtonLabel = "Bet";
    public const string BetButtonEmoji = "🎲";

    /// <summary>Discord's limit for an embed description.</summary>
    public const int EmbedDescriptionLimit = 4096;

    /// <summary>Most voters named on the round message; the rest are counted.</summary>
    public const int MaxListedVoters = 50;

    /// <summary>
    /// The round message. <paramref name="voters"/> are the players who voted, in display order
    /// (a 🎲 marks those betting), or null to leave the voter line out (Game:ShowVoteCount off).
    /// Only who voted and who bets, never what they picked. An open regular battle leaves out its rank.
    /// </summary>
    public static string RoundDescription(RoundRecord round, IReadOnlyList<Voter>? voters, bool closed,
        int maxListed = MaxListedVoters, int limit = EmbedDescriptionLimit, DifficultyOptions? difficulty = null)
    {
        var sb = new StringBuilder();
        sb.Append(closed ? "**Voting is closed.** The results are below.\n\n" : "Which team do you think painted more? Cast your vote!\n\n");
        // A regular battle keeps its rank secret until the reveal; a ranked round shows it, as it sets the points.
        if (round.Kind != RoundKind.Regular || closed) sb.Append(DifficultyLine(round, difficulty)).Append('\n');
        sb.Append(round.Kind == RoundKind.Regular ? BattleLine(round) : PointsLine(round, difficulty));
        if (!closed) sb.Append($"\nResults <t:{round.RevealAt}:R> (<t:{round.RevealAt}:f>).");

        if (voters is null) return sb.ToString();

        sb.Append("\n\n");
        if (voters.Count == 0)
        {
            sb.Append("No votes yet.");
            return sb.ToString();
        }

        if (round.ShowVotes && voters.All(v => v.Choice is not null))
        {
            AppendVotesByTeam(sb, round, voters, maxListed, limit);
            return sb.ToString();
        }

        sb.Append(voters.Count == 1 ? "**1 player has voted:** " : $"**{voters.Count} players have voted:** ");
        AppendList(sb, voters.Select(v => (v.Bet ? "🎲" : "") + $"<@{v.Id}>").ToList(), maxListed, limit - sb.Length);
        return sb.ToString();
    }

    /// <summary>
    /// How close the round is, as a rank from C (easiest) to X (hardest). It says nothing about
    /// who won, so it's shown while voting is open.
    /// </summary>
    public static string DifficultyLine(RoundRecord round, DifficultyOptions? difficulty = null)
    {
        var rank = Difficulty.RankFor(round.WinnerShare, difficulty ?? new DifficultyOptions());
        int count = (int)rank + 1; // C = 1 … X = 5
        string icon = round.Kind == RoundKind.Regular ? "🐟" : "🔥";
        return $"**Rank {rank}** · {count}{icon}";
    }

    /// <summary>What a regular battle is: unscored, who started it, and whether votes are public.</summary>
    public static string BattleLine(RoundRecord round) =>
        "Just for fun: no points!" +
        (round.ShowVotes ? "\nVotes are public" : "\nVotes are hidden") +
        (round.StartedBy != 0 ? $"\nStarted by <@{round.StartedBy}>" : "");

    /// <summary>Reply to /turf battle (only the caller sees it).</summary>
    public static string BattleStarted(RoundRecord round, bool started) => started
        ? $"{RegularEmoji} Regular Battle started! Voting ends <t:{round.RevealAt}:R>."
        : $"A Regular Battle is already running, so the command is disabled until it ends <t:{round.RevealAt}:R>.";

    public static string BattleRevealButtonId(long roundId) => $"battle-reveal:{roundId}";
    public static string BattleResetButtonId(long roundId) => $"battle-reset:{roundId}";
    public const string BattleRevealButtonLabel = "Reveal";
    public const string BattleSettingUpLabel = "Setting up…";
    public const string BattleSettingUp = "⏳ BATTLE TIME! Inklings are currently participating in battle. Please take cover and wait for the match to be over.";
    public const string BattleResetButtonLabel = "Reset";
    public const string BattleResetButtonEmoji = "🔁";
    public const string BattleResetting = "Setting up a fresh Regular Battle…";
    public static string BattleAlreadyRunning(RoundRecord round) =>
        $"A Regular Battle is already running (ends <t:{round.RevealAt}:R>), so Reset is now a Reveal button on this message.";

    /// <summary>Reply to /turf battle when today's battle message was reset instead of posting a new one.</summary>
    public static string BattleReusedMessage(RoundRecord round, string? link) =>
        $"{RegularEmoji} A fresh Regular Battle is running! Voting ends <t:{round.RevealAt}:R>.\n" +
        (link is null ? "Look at the previous Regular Battle message in the channel."
                      : $"Look at the previous Regular Battle message: {link}");

    /// <summary>The command list for <c>/turf help</c>. Admin commands are only listed for administrators.</summary>
    public static string Help(bool isAdmin)
    {
        var sb = new StringBuilder();
        sb.AppendLine("**Turf Guess commands**");
        sb.AppendLine("`/turf battle [show-votes]`: start a 🔫 Regular Battle in this channel (not scored); you get a private message when it is ready");
        sb.AppendLine("`/turf battle-reveal`: reveal the running battle now (whoever started it, or an admin)");
        sb.AppendLine("`/turf standings`: this week's standings");
        sb.AppendLine("`/turf all-time`: trophies and ranked and regular battle records");
        sb.AppendLine("`/turf subscribe`: get a private message with your result after each round");
        sb.AppendLine("`/turf unsubscribe`: stop those private messages");
        sb.Append("`/turf help`: show this list");
        if (isAdmin)
        {
            sb.AppendLine().AppendLine().AppendLine("**Admin commands**");
            sb.AppendLine("`/turf-admin status`: show the current ranked round");
            sb.AppendLine("`/turf-admin reveal-now`: reveal the current ranked round now and post the next one");
            sb.AppendLine("`/turf-admin reset-round`: cancel the current ranked round and start a new one");
            sb.AppendLine("`/turf-admin reset-scores`: wipe all weekly scores (asks for confirmation)");
            sb.Append("`/turf-admin set-ranked-channel`: choose the channel ranked battles are posted in");
        }
        return sb.ToString();
    }

    public static string RankedChannelChanged(ulong channelId, bool roundOpen) =>
        $"⚓ Ranked Battles are now posted in <#{channelId}>." +
        (roundOpen ? " The round that is running now stays where it is; the next one goes to the new channel." : "");

    public static string RankedChannelUnusable(ulong channelId, string missing) =>
        $"I can't use <#{channelId}>: I'm missing the {missing} permission there. Give me access and run the command again.";

    public const string NoBattleRunning = "There's no Regular Battle running right now.";
    public const string BattleRevealNotAllowed = "Only the player who started this Regular Battle, or an admin, can reveal it early.";
    public const string BattleRevealing = "Revealing the Regular Battle now.";

    /// <summary>"🔴 **Reds** (2): @A, @C" for each team, for battles where votes are public.</summary>
    private static void AppendVotesByTeam(StringBuilder sb, RoundRecord round, IReadOnlyList<Voter> voters, int maxListed, int limit)
    {
        var sides = new[] { Side.Left, Side.Right };
        for (int i = 0; i < sides.Length; i++)
        {
            var side = sides[i];
            var picked = voters.Where(v => v.Choice == side).Select(v => $"<@{v.Id}>").ToList();
            if (i > 0) sb.Append('\n');
            sb.Append($"{round.TeamEmoji(side)} **{round.TeamName(side)}** ({picked.Count})");
            if (picked.Count == 0) continue;

            sb.Append(": ");
            int room = (limit - sb.Length) / (sides.Length - i) - 4; // each team gets its share of what is left
            AppendList(sb, picked, maxListed, room);
        }
    }

    /// <summary>What the round is worth, and what a bet would win or lose.</summary>
    public static string PointsLine(RoundRecord round, DifficultyOptions? difficulty = null)
    {
        int points = PointsFor(round, difficulty);
        return $"Worth {Plural(points, "point")} · bet: win +{2 * points} / lose −{points}";
    }

    public static int PointsFor(RoundRecord round, DifficultyOptions? difficulty = null) =>
        Difficulty.RankFor(round.WinnerShare, difficulty ?? new DifficultyOptions()).Points();

    private static string Plural(int n, string noun) => n == 1 ? $"{n} {noun}" : $"{n} {noun}s";

    /// <summary>Only the voter sees this. <paramref name="bet"/> says whether their bet is on.</summary>
    public static string VoteAccepted(RoundRecord round, Side side, bool bet = false, DifficultyOptions? difficulty = null)
    {
        string text = $"You voted {round.TeamEmoji(side)} **{round.TeamName(side)}**. You can change your vote until the reveal.";
        if (bet)
        {
            int points = PointsFor(round, difficulty);
            text += $"\n🎲 Your bet is on: win +{2 * points} / lose −{points}.";
        }
        if (round.Kind == RoundKind.Regular && round.ShowVotes) text += "\nYour vote is public in this battle.";
        return text;
    }

    /// <summary>Only the player sees this, after pressing Bet.</summary>
    public static string BetToggled(RoundRecord round, bool bet, bool hasVoted, DifficultyOptions? difficulty = null)
    {
        int points = PointsFor(round, difficulty);
        if (!bet) return $"🎲 Your bet is off. A correct guess still earns +{points}, and a wrong one costs nothing.";

        string text = $"🎲 Your bet is on: win +{2 * points} / lose −{points} (your score can't drop below 0). Press Bet again to turn it off before the reveal.";
        if (!hasVoted) text += "\nA bet only counts if you vote for a team too.";
        return text;
    }

    public const string VotingClosed = "Voting for this round is closed.";

    /// <summary>Shown on a round message after an admin cancelled the round.</summary>
    public const string RoundCancelled = "**This round was cancelled by an admin.** Voting is closed.";

    /// <summary>How far before the round was created a message may still be from it (clock differences).</summary>
    public static readonly TimeSpan MessageAgeTolerance = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether a message could belong to this round. Messages older than the round are leftovers from an
    /// earlier game (for example before the database was replaced) and must not be adopted.
    /// </summary>
    public static bool IsFromThisRound(DateTimeOffset messageTime, RoundRecord round) =>
        messageTime >= round.CreatedAtTime - MessageAgeTolerance;

    /// <summary>How many players the weekly standings in the reveal show.</summary>
    public const int StandingsShown = 3;

    /// <summary>
    /// The reveal text: winner, coverage, vote counts, who guessed right and wrong with their points
    /// (🎲 marks a bet), and the top of the weekly standings. Players are mentions (sent with mentions
    /// disabled, so nobody is pinged). Each list is cut off at <paramref name="maxListed"/> names and
    /// the whole text is kept within <paramref name="limit"/> characters.
    /// </summary>
    public static string Reveal(RoundRecord round, RevealSummary s, int maxListed, int limit = DiscordMessageLimit, DifficultyOptions? difficulty = null)
    {
        if (round.Kind == RoundKind.Regular) return BattleReveal(round, s, maxListed, limit, difficulty);
        var w = round.WinnerSide;
        var l = Side.Left;
        var r = Side.Right;

        var sb = new StringBuilder();
        sb.Append($"## {round.TeamEmoji(w)} {round.TeamName(w)} wins!\n");
        sb.Append($"{round.TeamEmoji(l)} {round.TeamName(l)} **{Pct(round.ShareLeft)}** · ");
        sb.Append($"{round.TeamEmoji(r)} {round.TeamName(r)} **{Pct(round.ShareRight)}** · ");
        sb.Append($"{Pct(round.Unpainted)} unpainted\n");
        sb.Append($"Votes: {round.TeamEmoji(l)} {s.LeftVotes} · {round.TeamEmoji(r)} {s.RightVotes}\n\n");

        string standings = StandingsText(s.Standings);

        if (s.Winners.Count == 0 && s.Losers.Count == 0)
        {
            sb.Append("Nobody voted this round.");
        }
        else
        {
            if (s.Winners.Count == 0)
            {
                sb.Append("Nobody guessed right this round.");
            }
            else
            {
                string label = $"**Winners ({s.Winners.Count}):** ";
                int room = limit - sb.Length - standings.Length - label.Length;
                if (s.Losers.Count > 0) room = (room - 40) / 2; // leave the losers their half
                sb.Append(label);
                AppendList(sb, s.Winners.Select(Outcome).ToList(), maxListed, room);
            }

            if (s.Losers.Count > 0)
            {
                if (s.Winners.Count > 0) sb.Append('\n');
                sb.Append($"**Losers ({s.Losers.Count}):** ");
                AppendList(sb, s.Losers.Select(Outcome).ToList(), maxListed, limit - sb.Length - standings.Length);
            }
        }

        sb.Append(standings);
        return sb.ToString();
    }

    /// <summary>
    /// The reveal of a regular battle: who was right and wrong, no points and no standings. It overwrites
    /// the battle message, so it is also where the rank, hidden while voting, finally shows.
    /// </summary>
    private static string BattleReveal(RoundRecord round, RevealSummary s, int maxListed, int limit, DifficultyOptions? difficulty)
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

        string footer = $"\n{RegularEmoji} Regular Battle · {DifficultyLine(round, difficulty)} · no points awarded";
        if (s.Winners.Count == 0 && s.Losers.Count == 0)
        {
            sb.Append("Nobody voted this battle.");
        }
        else
        {
            int room = limit - sb.Length - footer.Length;
            if (s.Winners.Count > 0)
            {
                string label = $"✅ **Correct ({s.Winners.Count}):** ";
                sb.Append(label);
                AppendList(sb, s.Winners.Select(p => $"<@{p.UserId}>").ToList(), maxListed, s.Losers.Count > 0 ? (room - label.Length - 40) / 2 : room - label.Length);
            }
            else
            {
                sb.Append("Nobody guessed right this battle.");
            }

            if (s.Losers.Count > 0)
            {
                if (s.Winners.Count > 0) sb.Append('\n');
                sb.Append($"❌ **Wrong ({s.Losers.Count}):** ");
                AppendList(sb, s.Losers.Select(p => $"<@{p.UserId}>").ToList(), maxListed, limit - sb.Length - footer.Length);
            }
        }

        sb.Append(footer);
        return sb.ToString();
    }

    public const string SubscribeConfirmation =
        "🔔 You're subscribed. After every round you voted in, I'll send you a private message with the result.";
    public const string SubscribeNoDms =
        "🔔 You're subscribed, but I couldn't send you a message. Allow direct messages from server members " +
        "(Server Settings → Privacy Settings), or the results can't reach you.";
    public const string AlreadySubscribed = "You're already getting result messages (that's the default). Use `/turf unsubscribe` to stop them.";
    public const string Unsubscribed = "🔕 You're unsubscribed. No more result messages. Use `/turf subscribe` to get them again.";
    public const string NotSubscribed = "You were already unsubscribed.";

    /// <summary>The first direct message after subscribing again, to check that messages get through.</summary>
    public const string SubscribeTestMessage = "🔔 You're subscribed to round results again. This is where they'll show up.";

    /// <summary>Footer of every result message.</summary>
    public const string UnsubscribeFooter = "To stop these messages, press the button below or use /turf unsubscribe in the server.";

    /// <summary>Footer of a result message after the player pressed its button to stop the messages.</summary>
    public const string OptedOutFooter = "You won't get these messages anymore. To get them again, press the button below or use /turf subscribe in the server.";

    public const string OptOutButtonId = "dm:optout";
    public const string OptInButtonId = "dm:optin";
    public const string OptOutButtonLabel = "Stop these messages";
    public const string OptInButtonLabel = "Turn them back on";
    public const string OptOutButtonEmoji = "🔕";
    public const string OptInButtonEmoji = "🔔";

    public const string BattleReadyLinkLabel = "Go to the battle";

    /// <summary>The private ping to the player who started a Regular Battle once its image is rendered and posted.</summary>
    public static DirectMessage BattleReadyPing(RoundRecord battle, ulong messageId) => new(
        RoundTitle(battle),
        $"🔫 Your battle is ready! Voting is open until <t:{battle.RevealAt}:t> (<t:{battle.RevealAt}:R>).",
        UnsubscribeFooter, LinkedMessageId: messageId, LinkLabel: BattleReadyLinkLabel, OptOutButton: true,
        LinkedChannelId: (ulong)battle.ChannelId);

    public const string RevealLinkLabel = "Go to the result in the server";

    /// <summary>
    /// The private result card for one player: a title, right or wrong, who won, and for scored rounds the
    /// bet and points. Self-contained apart from the link to the reveal message, which is added by the
    /// notifier. <paramref name="delta"/> is null when the round isn't scored.
    /// </summary>
    public static DirectMessage RevealPing(RoundRecord round, bool correct, bool bet, int? delta, ulong revealMessageId)
    {
        var w = round.WinnerSide;
        var o = w.Opposite();

        var sb = new StringBuilder();
        sb.Append(correct ? "✅ **You guessed right!**\n" : "❌ **You guessed wrong.**\n");
        sb.Append($"{round.TeamEmoji(w)} **{round.TeamName(w)} won with {Pct(round.WinnerShare)}** ");
        sb.Append($"vs. {round.TeamEmoji(o)} {round.TeamName(o)} {Pct(o == Side.Left ? round.ShareLeft : round.ShareRight)}");
        if (delta is { } d)
            sb.Append($"\n{(bet ? "🎲 Bet on" : "No bet")} · {(d == 0 ? "no change in points" : $"{Signed(d)} points")}");

        string color = w == Side.Left ? round.LeftColor : round.RightColor;
        return new DirectMessage(RoundTitle(round), sb.ToString(), UnsubscribeFooter, color, revealMessageId, RevealLinkLabel, OptOutButton: true, LinkedChannelId: (ulong)round.ChannelId);
    }

    /// <summary>"🎲&lt;@id&gt; (+6)": a 🎲 for a bet, then the points gained, lost, or ~ for no change.</summary>
    private static string Outcome(PlayerOutcome o) => (o.Bet ? "🎲" : "") + $"<@{o.UserId}> ({Signed(o.Delta)})";

    private static string Signed(int delta) => delta switch
    {
        > 0 => $"+{delta}",
        < 0 => $"−{-delta}",
        _ => "~",
    };

    /// <summary>The top of the weekly standings, with a blank line before it. Empty when nobody has scored.</summary>
    private static string StandingsText(IReadOnlyList<StandingEntry> standings)
    {
        if (standings.Count == 0) return "";
        var leaders = Weekly.WeekWinners(standings);
        var sb = new StringBuilder("\n\n**Weekly standings**");
        for (int i = 0; i < Math.Min(standings.Count, StandingsShown); i++)
        {
            var e = standings[i];
            string trophy = leaders.Contains(e) ? "🏆 " : "";
            sb.Append($"\n{i + 1}. {trophy}<@{e.UserId}> {Plural(e.Points, "pt")} · {Plural(e.Wins, "win")}");
        }
        return sb.ToString();
    }

    /// <summary>Most characters of a standings message; Discord allows 2000.</summary>
    private const int ListBudget = 1900;

    /// <summary>Longest name shown in a standings table; longer ones end in "...".</summary>
    public const int NameWidth = 24;

    /// <summary>The current week's standings for <c>/turf standings</c>, as a table with as many players as fit in one message.</summary>
    public static string WeeklyStandings(string weekStart, IReadOnlyList<StandingEntry> standings, IReadOnlyDictionary<ulong, string> names)
    {
        string title = $"📊 **Weekly standings · Week {Weekly.WeekNumber(weekStart)}**";
        if (standings.Count == 0) return $"{title}\nNobody has scored yet this week.";

        var leaders = Weekly.WeekWinners(standings);
        string Row(string mark, string rank, string name, string points, string wins) =>
            $"{mark}{rank,2}  {name,-NameWidth}  {points,4}  {wins,4}".TrimEnd();

        var rows = standings.Select((e, i) =>
            Row(leaders.Contains(e) ? "*" : " ", (i + 1).ToString(), ShortName(names, e.UserId), e.Points.ToString(), e.Wins.ToString())).ToList();
        return Table(title, Row(" ", "#", "Player", "Pts", "Wins"), rows, leaders.Count > 0 ? "-# * leading the week" : null);
    }

    /// <summary>The all-time standings for <c>/turf all-time</c>: trophies and the ranked and regular win/loss record.</summary>
    public static string AllTimeStandings(IReadOnlyList<AllTimeEntry> entries, IReadOnlyDictionary<ulong, string> names)
    {
        const string title = "🏛️ **All-time standings**";
        if (entries.Count == 0) return $"{title}\nNo round has been revealed yet.";

        string Row(string rank, string name, string trophies, string ranked, string regular) =>
            $"{rank,3}  {name,-NameWidth} {trophies,2}  {ranked,-12}  {regular}".TrimEnd();

        var rows = entries.Select((e, i) =>
            Row((i + 1).ToString(), ShortName(names, e.UserId), e.Trophies.ToString(),
                RecordCell(e.RankedWins, e.RankedLosses), RecordCell(e.RegularWins, e.RegularLosses))).ToList();
        return Table(title, Row("#", "Player", "Tr", "Ranked", "Regular"), rows,
            "-# Tr = weekly trophies · W-L = right-wrong guesses, with the win rate");
    }

    /// <summary>"6W-2L   75%", or "-" for a player who never played that kind.</summary>
    public static string RecordCell(int wins, int losses) =>
        wins + losses == 0 ? "-" : $"{wins}W-{losses}L".PadRight(7) + $"{100 * wins / (wins + losses)}%".PadLeft(5);

    /// <summary>
    /// The name to show for a player: their latest known user name, cut to <see cref="NameWidth"/> characters
    /// with "..." for the rest. Falls back to the id. Backticks and line breaks are removed so they can't break the table.
    /// </summary>
    public static string ShortName(IReadOnlyDictionary<ulong, string> names, ulong userId)
    {
        string name = names.TryGetValue(userId, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : userId.ToString();
        name = name.Replace('`', '\'').Replace('\r', ' ').Replace('\n', ' ');
        var info = new System.Globalization.StringInfo(name);
        return info.LengthInTextElements <= NameWidth ? name : info.SubstringByTextElements(0, NameWidth - 3) + "...";
    }

    /// <summary>
    /// A title, then the rows in a code block (so the columns line up), as many as fit; whoever is left
    /// out is counted. <paramref name="note"/> is small print under the table.
    /// </summary>
    private static string Table(string title, string header, IReadOnlyList<string> rows, string? note)
    {
        var table = new StringBuilder(header);
        int shown = 0;
        // Room for the title, the code fence, the "...and N more" line and the note.
        int budget = ListBudget - title.Length - (note?.Length ?? 0) - 60;
        foreach (var row in rows)
        {
            if (table.Length + 1 + row.Length > budget) break;
            table.Append('\n').Append(row);
            shown++;
        }

        var sb = new StringBuilder(title).Append("\n```\n").Append(table).Append("\n```");
        if (shown < rows.Count) sb.Append($"\n…and {rows.Count - shown} more");
        if (note is not null) sb.Append('\n').Append(note);
        return sb.ToString();
    }

    /// <summary>The message after the first reveal of a new week. Null when nobody scored that week.</summary>
    public static string? WeekSummary(string weekStart, IReadOnlyList<StandingEntry> winners)
    {
        if (winners.Count == 0) return null;
        var top = winners[0];
        string names = string.Join(", ", winners.Select(x => $"<@{x.UserId}>"));
        string verb = winners.Count == 1 ? "wins" : "win";
        return $"🏁 **Week {Weekly.WeekNumber(weekStart)} is over!**\n" +
               $"🏆 {names} {verb} with {Plural(top.Points, "pt")} and {Plural(top.Wins, "win")}.\n" +
               "A new week starts now.";
    }

    /// <summary>
    /// Appends items joined by commas, at most <paramref name="maxListed"/> of them and within
    /// <paramref name="budget"/> characters. Anyone left out is counted: "…and N more".
    /// </summary>
    private static void AppendList(StringBuilder sb, IReadOnlyList<string> items, int maxListed, int budget)
    {
        int listed = 0;
        budget -= 30; // room for "…and N more"
        var names = new StringBuilder();
        foreach (var item in items)
        {
            if (listed >= maxListed) break;
            string part = (listed == 0 ? "" : ", ") + item;
            if (names.Length + part.Length > budget) break;
            names.Append(part);
            listed++;
        }

        sb.Append(names);
        int rest = items.Count - listed;
        if (rest > 0) sb.Append(listed == 0 ? $"{rest} players" : $" …and {rest} more");
    }

    private static string Pct(double share) => TurfGuess.Core.Util.Percent.Share(share);
}
