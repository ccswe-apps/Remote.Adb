using Remote.Adb.Core.Logging;
using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.Common;

/// <summary>
/// Re-runs a refresh on a timer while started — the shared engine behind every auto-refreshing view (the device list,
/// wireless discovery). A refresh never overlaps itself: a tick (or an explicit <see cref="RunAsync"/>) while one is
/// still running is skipped. Timer-driven refreshes run inside <see cref="CommandLogScope.Background"/>, since they're
/// routine polling rather than something the user did.
/// </summary>
public sealed class PeriodicRefresher
{
    private bool _isRunning;
    private readonly Func<Task> _onTick;
    private readonly IDispatcherTimer _timer;

    public PeriodicRefresher(ITimerFactory timerFactory, TimeSpan interval, Func<Task> onTick)
    {
        _onTick = onTick;
        _timer = timerFactory.Create(interval);
        _timer.Tick += OnTimerTick;
    }

    /// <summary>Whether a refresh is in flight.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>When it returns <see langword="true"/>, timer ticks are skipped (e.g. while a row is mid-launch).</summary>
    public Func<bool>? SkipTick { get; init; }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (SkipTick?.Invoke() == true)
        {
            return;
        }

        // The scope is captured by the refresh's async flow at its first await, so disposing it on return is safe.
        using var scope = CommandLogScope.Background();
        _ = RunAsync(_onTick);
    }

    /// <summary>
    /// Runs <paramref name="refresh"/> unless a refresh is already in flight (then returns without running it), so manual
    /// and timer-driven refreshes share one reentrancy guard.
    /// </summary>
    public async Task RunAsync(Func<Task> refresh)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        try
        {
            await refresh();
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <summary>Starts the periodic ticks.</summary>
    public void Start() => _timer.Start();

    /// <summary>Stops the periodic ticks (an in-flight refresh finishes).</summary>
    public void Stop() => _timer.Stop();
}
