using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.UnitTests.Fakes;
using Remote.Adb.Testing;

namespace Remote.Adb.Desktop.UnitTests.Common;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class PeriodicRefresherTests
{
    private protected RecordingLogger<PeriodicRefresher> Logger = null!;
    private protected FakeTimerFactory TimerFactory = null!;

    [SetUp]
    public void SetUp()
    {
        Logger = new RecordingLogger<PeriodicRefresher>();
        TimerFactory = new FakeTimerFactory();
    }

    private protected PeriodicRefresher Create(Func<Task> onTick) => new(TimerFactory, TimeSpan.FromSeconds(1), onTick, Logger);

    public class When_The_Timer_Ticks : PeriodicRefresherTests
    {
        [Test]
        public void It_runs_the_refresh()
        {
            var runs = 0;
            _ = Create(() =>
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
            }, Logger)
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
            var refresher = Create(() =>
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

        [Test]
        public void It_logs_a_refresh_that_throws_and_keeps_ticking()
        {
            var runs = 0;
            _ = Create(() =>
            {
                runs++;
                throw new InvalidOperationException("boom");
            });

            TimerFactory.Timer.RaiseTick();
            TimerFactory.Timer.RaiseTick();

            Assert.Multiple(() =>
            {
                Assert.That(runs, Is.EqualTo(2));
                Assert.That(Logger.Entries, Has.Count.EqualTo(2));
                Assert.That(Logger.Entries.Select(entry => entry.Level), Is.All.EqualTo(LogLevel.Error));
                Assert.That(Logger.Entries[0].Exception?.Message, Is.EqualTo("boom"));
            });
        }
    }

    public class When_RunAsync_Is_Called : PeriodicRefresherTests
    {
        [Test]
        public async Task It_runs_once_as_a_follow_up_pass_when_a_tick_is_in_flight()
        {
            var gate = new TaskCompletionSource();
            var manualRuns = 0;
            var refresher = Create(() => gate.Task);
            TimerFactory.Timer.RaiseTick();

            var manual = refresher.RunAsync(() =>
            {
                manualRuns++;
                return Task.CompletedTask;
            });
            var ranBeforeTickFinished = manualRuns;
            gate.SetResult();
            await manual;

            Assert.Multiple(() =>
            {
                Assert.That(ranBeforeTickFinished, Is.Zero);
                Assert.That(manualRuns, Is.EqualTo(1));
                Assert.That(refresher.IsRunning, Is.False);
            });
        }

        [Test]
        public async Task It_collapses_requests_made_meanwhile_into_one_pass()
        {
            var gate = new TaskCompletionSource();
            var passes = new List<string>();
            var refresher = Create(() => gate.Task);
            TimerFactory.Timer.RaiseTick();

            var first = refresher.RunAsync(() =>
            {
                passes.Add("first");
                return Task.CompletedTask;
            });
            var second = refresher.RunAsync(() =>
            {
                passes.Add("second");
                return Task.CompletedTask;
            });
            gate.SetResult();
            await Task.WhenAll(first, second);

            Assert.That(passes, Is.EqualTo(new[] { "second" }));
        }

        [Test]
        public async Task It_surfaces_the_follow_up_pass_failure_to_its_caller()
        {
            var gate = new TaskCompletionSource();
            var refresher = Create(() => gate.Task);
            TimerFactory.Timer.RaiseTick();

            var manual = refresher.RunAsync(() => throw new InvalidOperationException("boom"));
            gate.SetResult();

            await Assert.ThrowsAsync<InvalidOperationException>(() => manual);
        }
    }

    public class When_Started_And_Stopped : PeriodicRefresherTests
    {
        [Test]
        public void It_starts_and_stops_the_timer()
        {
            var refresher = Create(() => Task.CompletedTask);

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
