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
    ScoreRepository scores,
    SubscriptionRepository subscriptions,
    IPlayerNotifier notifier,
    IRoundChannel channel,
    RoundImages images,
    BotOptions options,
    ISeedSource seeds,
    ILogger<RoundService> logger)
{
    private const int MaxStepsPerAdvance = 12;

    // The message a battle reset writes the new battle over; only set while the gate is held.
    private ulong? _replaceMessageId;

    // Advancing and resetting never run at the same time.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Advances the game as far as it can at <paramref name="now"/>: the ranked round, then the regular
    /// battle if one is running. Returns when to call again.
    /// </summary>
    public async Task<DateTimeOffset> AdvanceAsync(DateTimeOffset now, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var ranked = await AdvanceCoreAsync(RoundKind.Ranked, now, ct);
            var battle = await AdvanceCoreAsync(RoundKind.Regular, now, ct);
            return ranked < battle ? ranked : battle;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Starts a regular battle (🔫) and posts it. Only one can run at a time: when one is already
    /// going, nothing is created and that battle is returned with Started = false.
    /// </summary>
    public async Task<(RoundRecord Round, bool Started)> StartBattleAsync(ulong userId, bool showVotes, DateTimeOffset now, CancellationToken ct, ulong? replaceMessageId = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var running = repo.GetActive(RoundKind.Regular);
            if (running is not null) return (running, false);

            var created = CreateRound(now, RoundKind.Regular, (long)userId, showVotes);
            logger.LogInformation("Round {RoundId}: regular battle started by {UserId}{Public}.",
                created.Id, userId, showVotes ? " with public votes" : "");

            _replaceMessageId = replaceMessageId;
            try { await AdvanceCoreAsync(RoundKind.Regular, now, ct); } // posts it
            finally { _replaceMessageId = null; }
            return (repo.Get(created.Id) ?? created, true);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Cancels the current round (admin reset): its message is disabled, it is never revealed or scored,
    /// and the next <see cref="AdvanceAsync"/> starts a new round. Weekly scores are untouched.
    /// Returns the cancelled round, or null when there was none.
    /// </summary>
    public async Task<RoundRecord?> ResetRoundAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var round = repo.GetActive();
            if (round is null) return null;

            repo.Abandon(round.Id);
            logger.LogInformation("Round {RoundId}: cancelled by an admin (was {Status}, {Votes} votes).", round.Id, round.Status, repo.CountVotes(round.Id));
            try { await channel.CancelRoundMessageAsync(round, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Round {RoundId}: couldn't update the cancelled round's message.", round.Id);
            }
            return round;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Moves one kind of round forward. A ranked round is created when none exists; a battle only on request.</summary>
    private async Task<DateTimeOffset> AdvanceCoreAsync(RoundKind kind, DateTimeOffset now, CancellationToken ct)
    {
        for (int step = 0; step < MaxStepsPerAdvance; step++)
        {
            ct.ThrowIfCancellationRequested();
            var round = repo.GetActive(kind);

            if (round is null)
            {
                if (kind == RoundKind.Regular) return DateTimeOffset.MaxValue;
                CreateRound(now, RoundKind.Ranked);
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
                    if (kind == RoundKind.Regular) await RevealBattleAsync(round, ct);
                    else await RevealAsync(round, ct);
                    break;
            }
        }

        throw new InvalidOperationException($"The current {kind} round didn't settle after {MaxStepsPerAdvance} steps.");
    }

    /// <summary>Moves the open round's reveal to now. Returns false when no round of that kind is open.</summary>
    public bool RevealNow(DateTimeOffset now, RoundKind kind = RoundKind.Ranked)
    {
        var round = repo.GetActive(kind);
        return round is { Status: RoundStatus.Open } && repo.SetRevealAt(round.Id, now.ToUnixTimeSeconds());
    }

    private RoundRecord CreateRound(DateTimeOffset now, RoundKind kind, long startedBy = 0, bool showVotes = false)
    {
        ulong seed = seeds.NextSeed();
        var generated = RoundGenerator.Generate(seed, options.Generator, regular: kind == RoundKind.Regular);
        var style = ColorPairs.Pick(seed, options.EffectiveColorPairs, options.Game.Name);

        var revealAt = kind == RoundKind.Regular
            ? now + options.Game.BattleLength
            : RoundSchedule.RevealAt(now, options.Game.ParsedRoundTime, options.Game.ParsedTimeZone,
                !repo.AnyRounds(), options.Game.MinRoundLength);

        var record = new RoundRecord
        {
            ChannelId = (long)options.Discord.ChannelId,
            Status = RoundStatus.Pending,
            Kind = kind,
            ShowVotes = showVotes,
            StartedBy = startedBy,
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
        return record;
    }

    private string LocalTime(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, options.Game.ParsedTimeZone)
            .ToString("yyyy-MM-dd HH:mm zzz", System.Globalization.CultureInfo.InvariantCulture);

    private static string Pct(double share, int decimals = 1) =>
        decimals == 1
            ? TurfGuess.Core.Util.Percent.Share(share)
            : (share * 100).ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture) + "%";

    private async Task PostAsync(RoundRecord round, DateTimeOffset now, CancellationToken ct)
    {
        ulong? messageId = await channel.FindRoundMessageAsync(round, ct);
        if (messageId is null)
        {
            var (imagePath, _) = images.Ensure(round);
            var replace = round.Kind == RoundKind.Regular ? _replaceMessageId : null;
            try { messageId = await channel.PostRoundAsync(round, imagePath, ct, replace); }
            catch (Exception ex) when (replace is not null && ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Round {RoundId}: couldn't reuse message {MessageId}; posting a new one.", round.Id, replace);
                messageId = await channel.PostRoundAsync(round, imagePath, ct);
            }
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
        await channel.CloseRoundMessageAsync(round, options.Game.ShowVoteCount ? repo.GetVoters(round.Id) : null, ct);

        ulong? revealId = await channel.FindRevealMessageAsync(round, ct);
        if (revealId is null)
        {
            var summary = ScoreRound(round, votes);

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
        await NotifySubscribersAsync(round, votes, revealId.Value, ct);
        await PostFinishedWeeksAsync(round, ct);
    }

    /// <summary>
    /// Closes and reveals a regular battle. Nothing is scored and no weekly summary is posted:
    /// the reveal only says who guessed right and wrong.
    /// </summary>
    private async Task RevealBattleAsync(RoundRecord round, CancellationToken ct)
    {
        var votes = repo.GetVotes(round.Id);
        bool listVoters = options.Game.ShowVoteCount || round.ShowVotes;
        await channel.CloseRoundMessageAsync(round, listVoters ? repo.GetVotersFor(round) : null, ct);

        ulong? revealId = await channel.FindRevealMessageAsync(round, ct);
        if (revealId is null)
        {
            var winner = round.WinnerSide;
            var ordered = votes.OrderBy(v => v.UserName, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.UserId).ToList();
            PlayerOutcome Outcome(VoteRecord v) => new((ulong)v.UserId, false, 0);
            var summary = new RevealSummary(
                LeftVotes: votes.Count(v => v.ChoiceSide == Side.Left),
                RightVotes: votes.Count(v => v.ChoiceSide == Side.Right),
                Winners: ordered.Where(v => v.ChoiceSide == winner).Select(Outcome).ToList(),
                Losers: ordered.Where(v => v.ChoiceSide != winner).Select(Outcome).ToList(),
                Standings: []);

            var (_, revealPath) = images.Ensure(round);
            revealId = await channel.PostRevealAsync(round, summary, revealPath, ct);
        }
        else
        {
            logger.LogInformation("Round {RoundId}: found reveal message {MessageId} posted before a restart; adopting it.", round.Id, revealId);
        }

        LogRevealStats(round, votes, revealId.Value);
        repo.MarkDone(round.Id, (long)revealId.Value);
        await NotifySubscribersAsync(round, votes, revealId.Value, ct);
    }

    /// <summary>
    /// Sends each subscribed voter a private message with their result. Runs once the round is Done, so a
    /// repeated step never sends twice; a crash in between loses the pings rather than doubling them. A
    /// player who can't be reached is logged and skipped; it never holds up the game.
    /// </summary>
    private async Task NotifySubscribersAsync(RoundRecord round, IReadOnlyList<VoteRecord> votes, ulong revealMessageId, CancellationToken ct)
    {
        var subscribed = subscriptions.SubscribedAmong(votes.Select(v => v.UserId).ToList());
        if (subscribed.Count == 0) return;

        var results = round.Kind == RoundKind.Ranked ? scores.GetResults(round.Id).ToDictionary(r => r.UserId) : [];
        int sent = 0;
        foreach (var vote in votes.Where(v => subscribed.Contains(v.UserId)))
        {
            ct.ThrowIfCancellationRequested();
            results.TryGetValue(vote.UserId, out var result);
            bool correct = vote.ChoiceSide == round.WinnerSide;
            var message = MessageText.RevealPing(round, correct, result is { Bet: not 0 },
                result is null ? null : (int)result.Delta, revealMessageId);
            try
            {
                await notifier.SendDirectAsync((ulong)vote.UserId, message, ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Round {RoundId}: couldn't send the result to subscriber {UserId}: {Reason}", round.Id, vote.UserId, ex.Message);
            }
        }
        logger.LogInformation("Round {RoundId}: sent the result to {Sent} of {Subscribers} subscribers.", round.Id, sent, subscribed.Count);
    }

    /// <summary>The week a round counts for: the week of its reveal time, in the game's time zone.</summary>
    private string WeekOf(RoundRecord round) => Weekly.WeekStart(round.RevealAtTime, options.Game.ParsedTimeZone);

    /// <summary>
    /// Works out each voter's points for the round and stores them (once; repeating is harmless),
    /// then builds the reveal numbers. Voters are listed by name, a bet only counts for a voter.
    /// </summary>
    private RevealSummary ScoreRound(RoundRecord round, IReadOnlyList<VoteRecord> votes)
    {
        var winner = round.WinnerSide;
        var bettors = repo.GetBettorIds(round.Id);
        var ordered = votes.OrderBy(v => v.UserName, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.UserId).ToList();
        string week = WeekOf(round);

        var results = scores.ScoreRound(round.Id, week, MessageText.PointsFor(round, options.Generator.Difficulty),
            ordered.Select(v => new ScoringVote(v.UserId, v.ChoiceSide == winner, bettors.Contains(v.UserId))).ToList())
            .ToDictionary(r => r.UserId);

        var outcomes = ordered.Select(v => (Vote: v, Result: results[v.UserId]))
            .Select(x => (x.Result.Correct != 0, new PlayerOutcome((ulong)x.Vote.UserId, x.Result.Bet != 0, (int)x.Result.Delta)))
            .ToList();

        return new RevealSummary(
            LeftVotes: votes.Count(v => v.ChoiceSide == Side.Left),
            RightVotes: votes.Count(v => v.ChoiceSide == Side.Right),
            Winners: outcomes.Where(o => o.Item1).Select(o => o.Item2).ToList(),
            Losers: outcomes.Where(o => !o.Item1).Select(o => o.Item2).ToList(),
            Standings: scores.Standings(week));
    }

    /// <summary>
    /// After the first reveal of a new week, posts the summary of each earlier week that has scores
    /// and none yet, and records its winners. A failure is logged and retried at the next reveal;
    /// it never holds up the game.
    /// </summary>
    private async Task PostFinishedWeeksAsync(RoundRecord round, CancellationToken ct)
    {
        foreach (var week in scores.UnclosedWeeksBefore(WeekOf(round)))
        {
            try
            {
                var winners = Weekly.WeekWinners(scores.Standings(week));
                string? text = MessageText.WeekSummary(week, winners);
                if (text is not null) await channel.PostWeekSummaryAsync(text, ct);
                scores.CloseWeek(week, winners);
                logger.LogInformation("Week of {Week}: finished, winners: {Winners}.", week,
                    winners.Count == 0 ? "nobody scored" : string.Join(", ", winners.Select(w => $"{w.UserId} ({w.Points} pts, {w.Wins} wins)")));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Posting the summary of the week of {Week} failed; it will be retried after the next reveal.", week);
                return;
            }
        }
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
