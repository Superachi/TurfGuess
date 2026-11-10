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
                    logger.LogInformation("Round {Id}: voting closed.", round.Id);
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

        logger.LogInformation("Round {Id}: created ({Left} vs {Right}, reveal at {RevealAt:u}).",
            record.Id, record.LeftName, record.RightName, revealAt);
    }

    private async Task PostAsync(RoundRecord round, DateTimeOffset now, CancellationToken ct)
    {
        ulong? messageId = await channel.FindRoundMessageAsync(round.Id, ct);
        if (messageId is null)
        {
            var (imagePath, _) = images.Ensure(round);
            messageId = await channel.PostRoundAsync(round, imagePath, ct);
            logger.LogInformation("Round {Id}: posted.", round.Id);
        }
        else
        {
            logger.LogInformation("Round {Id}: found the message posted before a restart; adopting it.", round.Id);
        }

        repo.MarkOpen(round.Id, (long)messageId.Value, now.ToUnixTimeSeconds());
    }

    private async Task RevealAsync(RoundRecord round, CancellationToken ct)
    {
        var votes = repo.GetVotes(round.Id);
        await channel.CloseRoundMessageAsync(round, options.Game.ShowVoteCount ? votes.Count : null, ct);

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
            logger.LogInformation("Round {Id}: revealed ({Winner} wins, {Correct}/{Total} guessed right).",
                round.Id, round.TeamName(winner), summary.CorrectGuessers.Count, votes.Count);
        }
        else
        {
            logger.LogInformation("Round {Id}: found the reveal posted before a restart; adopting it.", round.Id);
        }

        repo.MarkDone(round.Id, (long)revealId.Value);
    }
}
