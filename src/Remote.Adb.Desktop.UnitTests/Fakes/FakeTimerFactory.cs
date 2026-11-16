using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.UnitTests.Fakes;

/// <summary>Hands out a fresh <see cref="FakeDispatcherTimer"/> per <see cref="Create"/> call, which the test can drive.</summary>
public sealed class FakeTimerFactory : ITimerFactory
{
    /// <summary>The first timer created — the only one for a view model that creates a single timer.</summary>
    public FakeDispatcherTimer Timer => Timers[0];

    /// <summary>Every timer created, in creation order.</summary>
    public List<FakeDispatcherTimer> Timers { get; } = [];

    public IDispatcherTimer Create(TimeSpan interval)
    {
        var timer = new FakeDispatcherTimer(interval);
        Timers.Add(timer);
        return timer;
    }

    /// <summary>The single timer created with <paramref name="interval"/> — independent of creation order.</summary>
    public FakeDispatcherTimer WithInterval(TimeSpan interval) => Timers.Single(timer => timer.Interval == interval);
}
