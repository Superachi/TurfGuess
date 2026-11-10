using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TurfGuess.Bot.Game;

/// <summary>Lets other parts (admin commands) wake the scheduler early.</summary>
public sealed class SchedulerSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Wake()
    {
        try { _semaphore.Release(); }
        catch (SemaphoreFullException) { /* already signalled */ }
    }

    /// <summary>Waits until woken or until <paramref name="timeout"/> passes.</summary>
    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _semaphore.WaitAsync(timeout, ct);
}

/// <summary>Completes once the chat connection is ready; the scheduler waits for it.</summary>
public sealed class ChannelReadySignal
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => _tcs.Task;
    public void SetReady() => _tcs.TrySetResult();
}

/// <summary>
/// Runs <see cref="RoundService.AdvanceAsync"/> whenever something is due: on startup (catch-up),
/// at each reveal time, and when an admin command wakes it. It re-checks at least every few
/// minutes, so a changed system clock can't make it oversleep.
/// </summary>
public sealed class RoundScheduler(
    RoundService rounds,
    SchedulerSignal signal,
    ChannelReadySignal ready,
    TimeProvider clock,
    ILogger<RoundScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan MaxSleep = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ready.Ready.WaitAsync(stoppingToken);
        logger.LogInformation("Scheduler started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            DateTimeOffset next;
            try
            {
                next = await rounds.AdvanceAsync(now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Advancing the round failed; retrying in {Delay}.", RetryDelay);
                next = now + RetryDelay;
            }

            var sleep = next - clock.GetUtcNow();
            if (sleep < TimeSpan.FromSeconds(1)) sleep = TimeSpan.FromSeconds(1);
            if (sleep > MaxSleep) sleep = MaxSleep;

            try { await signal.WaitAsync(sleep, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
