using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.UnitTests.Fakes;

namespace Remote.Adb.Desktop.UnitTests.Common;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class PeriodicRefresherTests
{
    protected FakeTimerFactory TimerFactory = null!;

    [SetUp]
    public void SetUp()
    {
        TimerFactory = new FakeTimerFactory();
    }

    public class When_The_Timer_Ticks : PeriodicRefresherTests
    {
        [Test]
        public void It_runs_the_refresh()
        {
            var runs = 0;
            _ = new PeriodicRefresher(TimerFactory, TimeSpan.FromSeconds(1), () =>
            {
                runs++;
                return Task.CompletedTask;
            });

            TimerFactory.Timer.RaiseTick();

            Assert.That(runs, Is.EqualTo(1));
        }

        [Test]
        public void It_skips_while_SkipTick_says_so()
        {
            var runs = 0;
            _ = new PeriodicRefresher(TimerFactory, TimeSpan.FromSeconds(1), () =>
            {
                runs++;
                return Task.CompletedTask;
            })
            {
                SkipTick = () => true,
            };

            TimerFactory.Timer.RaiseTick();

            Assert.That(runs, Is.Zero);
        }

        [Test]
        public void It_skips_while_a_refresh_is_in_flight()
        {
            var gate = new TaskCompletionSource();
            var runs = 0;
            var refresher = new PeriodicRefresher(TimerFactory, TimeSpan.FromSeconds(1), () =>
            {
                runs++;
                return gate.Task;
            });

            TimerFactory.Timer.RaiseTick();
            TimerFactory.Timer.RaiseTick();
            var inFlight = refresher.IsRunning;
            gate.SetResult();

            Assert.Multiple(() =>
            {
                Assert.That(runs, Is.EqualTo(1));
                Assert.That(inFlight, Is.True);
                Assert.That(refresher.IsRunning, Is.False);
            });
        }
    }

    public class When_RunAsync_Is_Called : PeriodicRefresherTests
    {
        [Test]
        public async Task It_shares_the_guard_with_the_timer()
        {
            var gate = new TaskCompletionSource();
            var manualRuns = 0;
            var refresher = new PeriodicRefresher(TimerFactory, TimeSpan.FromSeconds(1), () => gate.Task);
            TimerFactory.Timer.RaiseTick();

            await refresher.RunAsync(() =>
            {
                manualRuns++;
                return Task.CompletedTask;
            });
            gate.SetResult();

            Assert.That(manualRuns, Is.Zero);
        }
    }

    public class When_Started_And_Stopped : PeriodicRefresherTests
    {
        [Test]
        public void It_starts_and_stops_the_timer()
        {
            var refresher = new PeriodicRefresher(TimerFactory, TimeSpan.FromSeconds(1), () => Task.CompletedTask);

            refresher.Start();
            var started = TimerFactory.Timer.IsRunning;
            refresher.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(started, Is.True);
                Assert.That(TimerFactory.Timer.IsRunning, Is.False);
            });
        }
    }
}
