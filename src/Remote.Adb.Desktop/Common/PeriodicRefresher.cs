using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Logging;
using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.Common;

/// <summary>
/// Re-runs a refresh on a timer while started — the shared engine behind every auto-refreshing view (the device list,
/// wireless discovery). A refresh never overlaps itself: a tick while one is still running is skipped, and an explicit
/// <see cref="RunAsync"/> while one is running queues a single follow-up pass (so a manual refresh is never lost to a
/// tick that was already in flight). Timer-driven refreshes run inside <see cref="CommandLogScope.Background"/>, since
/// they're routine polling rather than something the user did; a tick that throws is logged, never lost.
/// </summary>
public sealed class PeriodicRefresher
{
    private bool _isRunning;
    private readonly ILogger _logger;
    private readonly Func<Task> _onTick;
    private Func<Task>? _rerun;
    private TaskCompletionSource? _rerunCompletion;
    private readonly IDispatcherTimer _timer;

    public PeriodicRefresher(ITimerFactory timerFactory, TimeSpan interval, Func<Task> onTick, ILogger logger)
    {
        _onTick = onTick;
        _logger = logger;
        _timer = timerFactory.Create(interval);
        _timer.Tick += OnTimerTick;
    }

    /// <summary>Whether a refresh is in flight.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>When it returns <see langword="true"/>, timer ticks are skipped (e.g. while a row is mid-launch).</summary>
    public Func<bool>? SkipTick { get; init; }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isRunning || SkipTick?.Invoke() == true)
        {
            return;
        }

        // The scope is captured by the refresh's async flow at its first await, so disposing it on return is safe.
        using var scope = CommandLogScope.Background();
        _ = TickAsync();
    }

    /// <summary>
    /// Runs <paramref name="refresh"/>, or — when a refresh is already in flight — queues it to run once that one
    /// finishes (requests that pile up meanwhile collapse into that one pass, the latest winning). Completes when
    /// <paramref name="refresh"/> (or the pass it was folded into) has run, surfacing its exception.
    /// </summary>
    public async Task RunAsync(Func<Task> refresh)
    {
        if (_isRunning)
        {
            _rerun = refresh;
            _rerunCompletion ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _rerunCompletion.Task;
            return;
        }

        _isRunning = true;
        try
        {
            await refresh();
        }
        finally
        {
            await RunQueuedAsync();
            _isRunning = false;
        }
    }

    // Drains the follow-up pass(es) requested while a refresh was running, completing each requester with its outcome.
    private async Task RunQueuedAsync()
    {
        while (_rerun is { } refresh && _rerunCompletion is { } completion)
        {
            _rerun = null;
            _rerunCompletion = null;
            try
            {
                await refresh();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }
    }

    /// <summary>Starts the periodic ticks.</summary>
    public void Start() => _timer.Start();

    /// <summary>Stops the periodic ticks (an in-flight refresh finishes).</summary>
    public void Stop() => _timer.Stop();

    private async Task TickAsync()
    {
        try
        {
            await RunAsync(_onTick);
        }
        catch (Exception exception)
        {
            // A background tick has no caller to surface to; log it so the failure isn't silent, and keep ticking.
            _logger.LogError(exception, "Background refresh failed.");
        }
    }
}
