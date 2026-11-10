using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurfGuess.Bot.Data;
using TurfGuess.Bot.Options;

namespace TurfGuess.Bot.Game;

/// <summary>
/// Keeps "N players have voted" on the round message up to date, editing at most once per
/// interval (debounced) to stay within Discord's rate limits. Shows only the total, never the split.
/// </summary>
public sealed class VoteCountService : BackgroundService
{
    private readonly ConcurrentDictionary<long, byte> _dirty = new();
    private readonly RoundRepository _repo;
    private readonly IRoundChannel _channel;
    private readonly BotOptions _options;
    private readonly ChannelReadySignal _ready;
    private readonly ILogger<VoteCountService> _logger;

    public VoteCountService(VoteService votes, RoundRepository repo, IRoundChannel channel, BotOptions options,
        ChannelReadySignal ready, ILogger<VoteCountService> logger)
    {
        _repo = repo;
        _channel = channel;
        _options = options;
        _ready = ready;
        _logger = logger;
        votes.VoteRecorded += roundId => _dirty[roundId] = 0;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Game.ShowVoteCount) return;
        await _ready.Ready.WaitAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.Discord.VoteCountInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var roundId in _dirty.Keys)
            {
                _dirty.TryRemove(roundId, out _);
                try
                {
                    var round = _repo.Get(roundId);
                    if (round is not { Status: RoundStatus.Open }) continue;
                    await _channel.UpdateVoteCountAsync(round, _repo.CountVotes(roundId), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Updating the vote count of round {Id} failed.", roundId);
                }
            }
        }
    }
}
