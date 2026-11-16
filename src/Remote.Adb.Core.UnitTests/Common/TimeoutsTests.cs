using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Time.Testing;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.UnitTests.Common;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class TimeoutsTests
{
    public class When_TryWithTimeoutAsync_Is_Called : TimeoutsTests
    {
        [Test]
        public async Task It_returns_the_value_when_the_operation_finishes_in_time()
        {
            var result = await Timeouts.TryWithTimeoutAsync(_ => Task.FromResult(42), TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.That(result, Is.EqualTo((true, 42)));
        }

        [Test]
        public async Task It_reports_a_timeout_when_the_clock_passes_the_limit()
        {
            var clock = new FakeTimeProvider();
            var task = Timeouts.TryWithTimeoutAsync(
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return 42;
                },
                TimeSpan.FromSeconds(5),
                CancellationToken.None,
                clock);

            clock.Advance(TimeSpan.FromSeconds(5));
            var result = await task;

            Assert.That(result.Completed, Is.False);
        }

        [Test]
        public async Task It_propagates_cancellation_by_the_caller()
        {
            using var caller = new CancellationTokenSource();
            var task = Timeouts.TryWithTimeoutAsync(
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return 42;
                },
                TimeSpan.FromMinutes(1),
                caller.Token);

            caller.Cancel();

            await Assert.CatchAsync<OperationCanceledException>(async () => await task);
        }
    }
}
