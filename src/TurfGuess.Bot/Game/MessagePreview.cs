using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Options;
using TurfGuess.Core;
using TurfGuess.Core.Grid;
using TurfGuess.Core.Render;

namespace TurfGuess.Bot.Game;

/// <summary>
/// Prints every message the bot sends for one sample round, built from a real generated round
/// and the same <see cref="MessageText"/> the bot uses. No Discord connection or token needed.
/// Run with: <c>dotnet run --project src/TurfGuess.Bot -- --preview-messages [--seed N] [--raw]</c>.
/// </summary>
public static partial class MessagePreview
{
    private const long SampleRoundId = 12;

    // Ranked rounds and regular battles are counted separately, so the two sample numbers differ.
    private const long SampleRankedNumber = 5;
    private const long SampleBattleNumber = 3;

    /// <param name="raw">Show the exact markup sent to Discord instead of resolving mentions and timestamps.</param>
    public static string Render(BotOptions options, ulong seed, DateTimeOffset now, bool raw)
    {
        var generated = RoundGenerator.Generate(seed, options.Generator);
        var style = ColorPairs.Pick(seed, options.EffectiveColorPairs, options.Game.Name);
        var revealAt = RoundSchedule.RevealAt(now, options.Game.ParsedRoundTime, options.Game.ParsedTimeZone, false, TimeSpan.Zero);

        var round = new RoundRecord
        {
            Id = SampleRoundId, Number = SampleRankedNumber,
            Status = RoundStatus.Open,
            Seed = (long)seed,
            RevealAt = revealAt.ToUnixTimeSeconds(),
            LeftName = style.Left.Name, LeftColor = style.Left.Color, LeftEmoji = style.Left.Emoji,
            RightName = style.Right.Name, RightColor = style.Right.Color, RightEmoji = style.Right.Emoji,
            Winner = SideCodec.Format(generated.Score.Winner),
            WinnerShare = generated.Score.WinnerShare,
            ShareLeft = generated.Score.CoverageLeft,
            ShareRight = generated.Score.CoverageRight,
            Unpainted = generated.Score.UnpaintedShare,
        };

        // Sample votes: 11 players, 7 of them right. Players 1, 3 and 9 are betting.
        var winner = round.WinnerSide;
        int points = MessageText.PointsFor(round, options.Generator.Difficulty);
        var ids = Enumerable.Range(1, 11).Select(i => 100_000_000_000_000_000UL + (ulong)i).ToList();
        var bettors = new[] { 0, 2, 8 };
        var voters = ids.Select((id, i) => new Voter(id, bettors.Contains(i))).ToList();
        PlayerOutcome Outcome(int i, bool correct) =>
            new(ids[i], bettors.Contains(i), Weekly.RawDelta(points, correct, bettors.Contains(i)));
        var winners = Enumerable.Range(0, 7).Select(i => Outcome(i, true)).ToList();
        var losers = Enumerable.Range(7, 4).Select(i => Outcome(i, false)).ToList();
        var standings = new List<StandingEntry>
        {
            new(ids[0], 14, 5), new(ids[1], 11, 4), new(ids[2], 11, 3), new(ids[3], 9, 5),
        };
        var summary = new RevealSummary(winner == Side.Left ? 7 : 4, winner == Side.Left ? 4 : 7, winners, losers, standings);

        string Show(string text) => raw ? text : Humanize(text, options.Game.ParsedTimeZone, now);
        string buttons(bool disabled) =>
            $"[ {round.LeftEmoji} {round.LeftName} ]  [ {round.RightEmoji} {round.RightName} ]  [ {MessageText.BetButtonEmoji} {MessageText.BetButtonLabel} ]" +
            (disabled ? "  (disabled)" : "");

        var sb = new StringBuilder();
        sb.AppendLine($"Sample round: seed {seed}, {round.LeftName} vs {round.RightName}, {round.TeamName(winner)} wins with " +
                      $"{(round.WinnerShare * 100).ToString("0.00", CultureInfo.InvariantCulture)}% of the painted floor.");
        sb.AppendLine(raw ? "Showing the raw markup sent to Discord." : "Mentions and timestamps are shown as Discord would display them (run with --raw for the markup).");
        sb.AppendLine();

        Section(sb, "1. Round message (posted at the round time)");
        Embed(sb, MessageText.RoundTitle(options.Game.Name, round),
            Show(MessageText.RoundDescription(round, options.Game.ShowVoteCount ? [] : null, closed: false, difficulty: options.Generator.Difficulty)),
            $"round_{round.Id}.png", MessageText.RoundFooter(round));
        sb.AppendLine($"Buttons: {buttons(false)}");
        sb.AppendLine();

        Section(sb, "2. Vote confirmation (only the voter sees this)");
        sb.AppendLine(Show(MessageText.VoteAccepted(round, Side.Left)));
        sb.AppendLine(Show(MessageText.VoteAccepted(round, Side.Left, bet: true, difficulty: options.Generator.Difficulty)));
        sb.AppendLine();

        Section(sb, "2b. Pressing the Bet button (only the player sees this)");
        sb.AppendLine(Show(MessageText.BetToggled(round, bet: true, hasVoted: true, options.Generator.Difficulty)));
        sb.AppendLine(Show(MessageText.BetToggled(round, bet: true, hasVoted: false, options.Generator.Difficulty)));
        sb.AppendLine(Show(MessageText.BetToggled(round, bet: false, hasVoted: true, options.Generator.Difficulty)));
        sb.AppendLine();

        Section(sb, "3. Round message, live voter list (edited at most every " +
                    $"{options.Discord.VoteCountInterval.TotalSeconds:0} s)");
        sb.AppendLine(Show(MessageText.RoundDescription(round, voters, closed: false, difficulty: options.Generator.Difficulty)));
        sb.AppendLine();

        Section(sb, "4. Clicking after voting closed (only the clicker sees this)");
        sb.AppendLine(MessageText.VotingClosed);
        sb.AppendLine();

        Section(sb, "5. Round message after the reveal");
        Embed(sb, MessageText.RoundTitle(options.Game.Name, round),
            Show(MessageText.RoundDescription(round, options.Game.ShowVoteCount ? voters : null, closed: true, difficulty: options.Generator.Difficulty)),
            $"round_{round.Id}.png", MessageText.RoundFooter(round));
        sb.AppendLine($"Buttons: {buttons(true)}");
        sb.AppendLine();

        Section(sb, $"6. Reveal (embed in {round.TeamName(winner)}'s color {(winner == Side.Left ? round.LeftColor : round.RightColor)}, no pings)");
        Embed(sb, null, Show(MessageText.Reveal(round, summary, options.Game.MaxListedWinners)),
            $"reveal_{round.Id}.png", MessageText.RevealFooter(round));
        sb.AppendLine();

        Section(sb, "7. Weekly summary (posted after the first reveal of a new week, no pings)");
        sb.AppendLine(Show(MessageText.WeekSummary("2026-11-02", [standings[0]]) ?? ""));
        sb.AppendLine();

        // The same sample round, played as a regular battle: 15 minutes, not scored, no Bet button.
        var battle = new RoundRecord
        {
            Id = SampleRoundId + 1, Number = SampleBattleNumber, Status = RoundStatus.Open, Kind = RoundKind.Regular, StartedBy = (long)ids[0],
            Seed = round.Seed, RevealAt = now.AddMinutes(15).ToUnixTimeSeconds(),
            LeftName = round.LeftName, LeftColor = round.LeftColor, LeftEmoji = round.LeftEmoji,
            RightName = round.RightName, RightColor = round.RightColor, RightEmoji = round.RightEmoji,
            Winner = round.Winner, WinnerShare = round.WinnerShare, ShareLeft = round.ShareLeft,
            ShareRight = round.ShareRight, Unpainted = round.Unpainted,
        };
        var publicBattle = new RoundRecord
        {
            Id = battle.Id, Number = battle.Number, Status = battle.Status, Kind = battle.Kind, StartedBy = battle.StartedBy, ShowVotes = true,
            RevealAt = battle.RevealAt, LeftName = battle.LeftName, LeftEmoji = battle.LeftEmoji, RightName = battle.RightName,
            RightEmoji = battle.RightEmoji, Winner = battle.Winner, WinnerShare = battle.WinnerShare,
        };
        var battleVoters = ids.Select((id, i) => new Voter(id, false, i < 6 ? Side.Left : Side.Right)).ToList();
        string battleButtons = $"[ {round.LeftEmoji} {round.LeftName} ]  [ {round.RightEmoji} {round.RightName} ]";
        var battleWinners = winners.Select(o => o with { Bet = false, Delta = 0 }).ToList();
        var battleLosers = losers.Select(o => o with { Bet = false, Delta = 0 }).ToList();
        var battleSummary = new RevealSummary(summary.LeftVotes, summary.RightVotes, battleWinners, battleLosers, []);

        Section(sb, "8. /turf battle (only the caller sees these replies)");
        sb.AppendLine(Show(MessageText.BattleStarted(battle, started: true)));
        sb.AppendLine(Show(MessageText.BattleStarted(battle, started: false)));
        sb.AppendLine();

        Section(sb, "9. Regular Battle message (15 minutes, not scored, no Bet button)");
        Embed(sb, MessageText.RoundTitle(options.Game.Name, battle),
            Show(MessageText.RoundDescription(battle, options.Game.ShowVoteCount ? voters.Select(v => v with { Bet = false }).ToList() : null,
                closed: false, difficulty: options.Generator.Difficulty)),
            $"round_{battle.Id}.png", MessageText.RoundFooter(battle));
        sb.AppendLine($"Buttons: {battleButtons}");
        sb.AppendLine();

        Section(sb, "9b. Regular Battle with show-votes (who voted for which team)");
        Embed(sb, MessageText.RoundTitle(options.Game.Name, publicBattle),
            Show(MessageText.RoundDescription(publicBattle, battleVoters, closed: false, difficulty: options.Generator.Difficulty)),
            $"round_{battle.Id}.png", MessageText.RoundFooter(publicBattle));
        sb.AppendLine($"Buttons: {battleButtons}");
        sb.AppendLine(Show(MessageText.VoteAccepted(publicBattle, Side.Left)));
        sb.AppendLine();

        Section(sb, "10. Regular Battle reveal (no points, no weekly standings, no pings)");
        Embed(sb, null, Show(MessageText.Reveal(battle, battleSummary, options.Game.MaxListedWinners)),
            $"reveal_{battle.Id}.png", MessageText.RevealFooter(battle));
        sb.AppendLine();

        Section(sb, "11. /turf battle-reveal (only the caller sees these replies)");
        sb.AppendLine(MessageText.BattleRevealing);
        sb.AppendLine(MessageText.BattleRevealNotAllowed);
        sb.AppendLine(MessageText.NoBattleRunning);

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title)
    {
        sb.AppendLine(title);
        sb.AppendLine(new string('─', Math.Min(title.Length, 72)));
    }

    private static void Embed(StringBuilder sb, string? title, string description, string image, string footer)
    {
        if (title is not null) sb.AppendLine($"│ **{title}**");
        foreach (var line in description.Split('\n')) sb.AppendLine($"│ {line}");
        sb.AppendLine($"│ [image: {image}]");
        sb.AppendLine($"│ {footer}");
    }

    /// <summary>Resolves &lt;t:unix:style&gt; timestamps and &lt;@id&gt; mentions roughly as a Discord client shows them.</summary>
    internal static string Humanize(string text, TimeZoneInfo zone, DateTimeOffset now)
    {
        var playerNumbers = new Dictionary<string, int>();
        text = MentionPattern().Replace(text, m =>
        {
            if (!playerNumbers.TryGetValue(m.Groups[1].Value, out int n)) playerNumbers[m.Groups[1].Value] = n = playerNumbers.Count + 1;
            return $"@Player{n}";
        });

        return TimestampPattern().Replace(text, m =>
        {
            var at = DateTimeOffset.FromUnixTimeSeconds(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            var local = TimeZoneInfo.ConvertTime(at, zone);
            return m.Groups[2].Value switch
            {
                "R" => Relative(at - now),
                "f" => local.ToString("d MMMM yyyy HH:mm", CultureInfo.InvariantCulture),
                _ => local.ToString("dddd d MMMM yyyy HH:mm", CultureInfo.InvariantCulture),
            };
        });
    }

    private static string Relative(TimeSpan d)
    {
        if (d < TimeSpan.Zero) return "just now";
        if (d.TotalHours >= 1) return $"in {Math.Round(d.TotalHours)} hours";
        return $"in {Math.Max(1, Math.Round(d.TotalMinutes))} minutes";
    }

    [GeneratedRegex(@"<@(\d+)>")]
    private static partial Regex MentionPattern();

    [GeneratedRegex(@"<t:(\d+):([a-zA-Z])>")]
    private static partial Regex TimestampPattern();
}
