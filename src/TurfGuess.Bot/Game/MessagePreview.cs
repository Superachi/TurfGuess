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

    /// <param name="raw">Show the exact markup sent to Discord instead of resolving mentions and timestamps.</param>
    public static string Render(BotOptions options, ulong seed, DateTimeOffset now, bool raw)
    {
        var generated = RoundGenerator.Generate(seed, options.Generator);
        var style = ColorPairs.Pick(seed, options.EffectiveColorPairs, options.Game.Name);
        var revealAt = RoundSchedule.RevealAt(now, options.Game.ParsedRoundTime, options.Game.ParsedTimeZone, false, TimeSpan.Zero);

        var round = new RoundRecord
        {
            Id = SampleRoundId,
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

        // Sample votes: 11 players, 7 of them right.
        var winner = round.WinnerSide;
        var voters = Enumerable.Range(1, 11).Select(i => 100_000_000_000_000_000UL + (ulong)i).ToList();
        var correct = voters.Take(7).ToList();
        var summary = winner == Side.Left
            ? new RevealSummary(LeftVotes: 7, RightVotes: 4, correct)
            : new RevealSummary(LeftVotes: 4, RightVotes: 7, correct);

        string Show(string text) => raw ? text : Humanize(text, options.Game.ParsedTimeZone, now);
        string buttons(bool disabled) =>
            $"[ {round.LeftEmoji} {round.LeftName} ]  [ {round.RightEmoji} {round.RightName} ]" + (disabled ? "  (disabled)" : "");

        var sb = new StringBuilder();
        sb.AppendLine($"Sample round: seed {seed}, {round.LeftName} vs {round.RightName}, {round.TeamName(winner)} wins with " +
                      $"{(round.WinnerShare * 100).ToString("0.00", CultureInfo.InvariantCulture)}% of the painted floor.");
        sb.AppendLine(raw ? "Showing the raw markup sent to Discord." : "Mentions and timestamps are shown as Discord would display them (run with --raw for the markup).");
        sb.AppendLine();

        Section(sb, "1. Round message (posted at the round time)");
        Embed(sb, MessageText.RoundTitle(options.Game.Name, round.Id),
            Show(MessageText.RoundDescription(round, options.Game.ShowVoteCount ? [] : null, closed: false, difficulty: options.Generator.Difficulty)),
            $"round_{round.Id}.png", MessageText.RoundFooter(round.Id));
        sb.AppendLine($"Buttons: {buttons(false)}");
        sb.AppendLine();

        Section(sb, "2. Vote confirmation (only the voter sees this)");
        sb.AppendLine(Show(MessageText.VoteAccepted(round, Side.Left)));
        sb.AppendLine();

        Section(sb, "3. Round message, live voter list (edited at most every " +
                    $"{options.Discord.VoteCountInterval.TotalSeconds:0} s)");
        sb.AppendLine(Show(MessageText.RoundDescription(round, voters, closed: false, difficulty: options.Generator.Difficulty)));
        sb.AppendLine();

        Section(sb, "4. Clicking after voting closed (only the clicker sees this)");
        sb.AppendLine(MessageText.VotingClosed);
        sb.AppendLine();

        Section(sb, "5. Round message after the reveal");
        Embed(sb, MessageText.RoundTitle(options.Game.Name, round.Id),
            Show(MessageText.RoundDescription(round, options.Game.ShowVoteCount ? voters : null, closed: true, difficulty: options.Generator.Difficulty)),
            $"round_{round.Id}.png", MessageText.RoundFooter(round.Id));
        sb.AppendLine($"Buttons: {buttons(true)}");
        sb.AppendLine();

        Section(sb, $"6. Reveal (embed in {round.TeamName(winner)}'s color {(winner == Side.Left ? round.LeftColor : round.RightColor)}, no pings)");
        Embed(sb, null, Show(MessageText.Reveal(round, summary, options.Game.MaxListedWinners)),
            $"reveal_{round.Id}.png", MessageText.RevealFooter(round.Id));

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
