using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Options;
using TurfGuess.Core;
using TurfGuess.Core.Grid;
using TurfGuess.Core.Render;

namespace TurfGuess.Bot.Game;

/// <summary>Where new round seeds come from. Tests use fixed seeds.</summary>
public interface ISeedSource
{
    ulong NextSeed();
}

public sealed class RandomSeedSource : ISeedSource
{
    // Kept below 2^63 so the seed fits SQLite's signed INTEGER.
    public ulong NextSeed() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) & long.MaxValue;
}

/// <summary>
/// The round lifecycle (DESIGN.md §2): Pending → Open → Revealed → Done.
/// <see cref="AdvanceAsync"/> moves the current round forward one step at a time until it's
/// Open and waiting for its reveal time. Every step is safe to repeat, so the same code handles
/// normal operation, catch-up after downtime and recovery after a crash.
/// </summary>
public sealed class RoundService(
    RoundRepository repo,
    IRoundChannel channel,
    RoundImages images,
    BotOptions options,
    ISeedSource seeds,
    ILogger<RoundService> logger)
{
    private const int MaxStepsPerAdvance = 12;

    /// <summary>Advances the game as far as it can at <paramref name="now"/>. Returns when to call again.</summary>
    public async Task<DateTimeOffset> AdvanceAsync(DateTimeOffset now, CancellationToken ct)
    {
        for (int step = 0; step < MaxStepsPerAdvance; step++)
        {
            ct.ThrowIfCancellationRequested();
            var round = repo.GetActive();

            if (round is null)
            {
                CreateRound(now);
                continue;
            }

            switch (round.Status)
            {
                case RoundStatus.Pending:
                    await PostAsync(round, now, ct);
                    break;

                case RoundStatus.Open:
                    if (now < round.RevealAtTime) return round.RevealAtTime;
                    repo.MarkRevealed(round.Id);
                    logger.LogInformation("Round {RoundId}: voting closed with {Votes} votes.", round.Id, repo.CountVotes(round.Id));
                    break;

                case RoundStatus.Revealed:
                    await RevealAsync(round, ct);
                    break;
            }
        }

        throw new InvalidOperationException($"The current round didn't settle after {MaxStepsPerAdvance} steps.");
    }

    /// <summary>Moves the open round's reveal to now. Returns false when no round is open.</summary>
    public bool RevealNow(DateTimeOffset now)
    {
        var round = repo.GetActive();
        return round is { Status: RoundStatus.Open } && repo.SetRevealAt(round.Id, now.ToUnixTimeSeconds());
    }

    private void CreateRound(DateTimeOffset now)
    {
        ulong seed = seeds.NextSeed();
        var generated = RoundGenerator.Generate(seed, options.Generator);
        var style = ColorPairs.Pick(seed, options.EffectiveColorPairs, options.Game.Name);

        bool isFirst = !repo.AnyRounds();
        var revealAt = RoundSchedule.RevealAt(now, options.Game.ParsedRoundTime, options.Game.ParsedTimeZone,
            isFirst, options.Game.MinRoundLength);

        var record = new RoundRecord
        {
            ChannelId = (long)options.Discord.ChannelId,
            Status = RoundStatus.Pending,
            Seed = (long)seed,
            GeneratorVersion = generated.GeneratorVersion,
            CreatedAt = now.ToUnixTimeSeconds(),
            RevealAt = revealAt.ToUnixTimeSeconds(),
            LeftName = style.Left.Name,
            LeftColor = style.Left.Color,
            LeftEmoji = style.Left.Emoji,
            RightName = style.Right.Name,
            RightColor = style.Right.Color,
            RightEmoji = style.Right.Emoji,
            Winner = SideCodec.Format(generated.Score.Winner),
            WinnerShare = generated.Score.WinnerShare,
            ShareLeft = generated.Score.CoverageLeft,
            ShareRight = generated.Score.CoverageRight,
            Unpainted = generated.Score.UnpaintedShare,
        };

        // Row first, images second: if we crash in between, Ensure() regenerates them from the seed.
        record.Id = repo.Insert(record);
        var (imagePath, revealPath) = images.Save(record, generated);
        repo.SetImagePaths(record.Id, imagePath, revealPath);

        // The answer is logged on purpose: logs are for the operator, not for players.
        logger.LogInformation(
            "Round {RoundId}: created, {Left} vs {Right}, seed {Seed}. Answer: {Winner} wins with {WinnerShare} of the painted floor " +
            "(coverage {Left} {ShareLeft}, {Right} {ShareRight}, {Unpainted} unpainted). Reveal at {RevealAt}.",
            record.Id, record.LeftName, record.RightName, seed, record.TeamName(record.WinnerSide), Pct(record.WinnerShare, 2),
            record.LeftName, Pct(record.ShareLeft), record.RightName, Pct(record.ShareRight), Pct(record.Unpainted), LocalTime(revealAt));
    }

    private string LocalTime(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, options.Game.ParsedTimeZone)
            .ToString("yyyy-MM-dd HH:mm zzz", System.Globalization.CultureInfo.InvariantCulture);

    private static string Pct(double share, int decimals = 1) =>
        (share * 100).ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture) + "%";

    private async Task PostAsync(RoundRecord round, DateTimeOffset now, CancellationToken ct)
    {
        ulong? messageId = await channel.FindRoundMessageAsync(round.Id, ct);
        if (messageId is null)
        {
            var (imagePath, _) = images.Ensure(round);
            messageId = await channel.PostRoundAsync(round, imagePath, ct);
            logger.LogInformation(
                "Round {RoundId}: posted in channel {ChannelId} (message {MessageId}), {Left} vs {Right}. Voting is open until {RevealAt}.",
                round.Id, options.Discord.ChannelId, messageId, round.LeftName, round.RightName, LocalTime(round.RevealAtTime));
        }
        else
        {
            logger.LogInformation("Round {RoundId}: found message {MessageId} posted before a restart; adopting it.", round.Id, messageId);
        }

        repo.MarkOpen(round.Id, (long)messageId.Value, now.ToUnixTimeSeconds());
    }

    private async Task RevealAsync(RoundRecord round, CancellationToken ct)
    {
        var votes = repo.GetVotes(round.Id);
        await channel.CloseRoundMessageAsync(round, options.Game.ShowVoteCount ? repo.GetVoterIds(round.Id) : null, ct);

        ulong? revealId = await channel.FindRevealMessageAsync(round.Id, ct);
        if (revealId is null)
        {
            var winner = round.WinnerSide;
            var summary = new RevealSummary(
                LeftVotes: votes.Count(v => v.ChoiceSide == Side.Left),
                RightVotes: votes.Count(v => v.ChoiceSide == Side.Right),
                CorrectGuessers: votes.Where(v => v.ChoiceSide == winner).Select(v => (ulong)v.UserId).ToList());

            var (_, revealPath) = images.Ensure(round);
            revealId = await channel.PostRevealAsync(round, summary, revealPath, ct);
            LogRevealStats(round, votes, revealId.Value);
        }
        else
        {
            logger.LogInformation("Round {RoundId}: found reveal message {MessageId} posted before a restart; adopting it.", round.Id, revealId);
            LogRevealStats(round, votes, revealId.Value);
        }

        repo.MarkDone(round.Id, (long)revealId.Value);
    }

    private const int MaxNamesInLog = 50;

    /// <summary>One log entry with the full results of a round.</summary>
    private void LogRevealStats(RoundRecord round, IReadOnlyList<VoteRecord> votes, ulong revealMessageId)
    {
        var winner = round.WinnerSide;
        var loser = winner.Opposite();
        var correct = votes.Where(v => v.ChoiceSide == winner).ToList();
        int winnerVotes = correct.Count;
        int loserVotes = votes.Count - winnerVotes;
        string correctPct = votes.Count == 0 ? "-" : Pct(correct.Count / (double)votes.Count, 0);

        var names = correct.Take(MaxNamesInLog).Select(v => v.UserName.Length > 0 ? v.UserName : v.UserId.ToString()).ToList();
        string guessers = names.Count == 0 ? "nobody" : string.Join(", ", names);
        if (correct.Count > MaxNamesInLog) guessers += $" and {correct.Count - MaxNamesInLog} more";

        logger.LogInformation(
            "Round {RoundId}: revealed (message {MessageId}). {Winner} wins with {WinnerShare} of the painted floor. " +
            "Coverage: {Left} {ShareLeft}, {Right} {ShareRight}, {Unpainted} unpainted. " +
            "Votes: {TotalVotes} ({Winner} {WinnerVotes}, {Loser} {LoserVotes}). " +
            "Correct: {CorrectCount}/{TotalVotes} ({CorrectPct}): {CorrectGuessers}.",
            round.Id, revealMessageId, round.TeamName(winner), Pct(round.WinnerShare, 2),
            round.LeftName, Pct(round.ShareLeft), round.RightName, Pct(round.ShareRight), Pct(round.Unpainted),
            votes.Count, round.TeamName(winner), winnerVotes, round.TeamName(loser), loserVotes,
            correct.Count, votes.Count, correctPct, guessers);
    }
}
