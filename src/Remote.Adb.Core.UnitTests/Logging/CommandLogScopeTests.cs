using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Core.Logging;

namespace Remote.Adb.Core.UnitTests.Logging;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class CommandLogScopeTests
{
    public class When_Background_Is_Called : CommandLogScopeTests
    {
        [Test]
        public async Task It_flows_across_awaits()
        {
            using var scope = CommandLogScope.Background();

            await Task.Yield();

            Assert.That(CommandLogScope.IsBackground, Is.True);
        }

        [Test]
        public void It_restores_the_previous_state_on_dispose()
        {
            var scope = CommandLogScope.Background();

            scope.Dispose();

            Assert.That(CommandLogScope.IsBackground, Is.False);
        }

        [Test]
        public async Task It_reaches_work_started_inside_it_after_it_is_disposed()
        {
            var gate = new TaskCompletionSource();
            Task<bool> work;

            using (CommandLogScope.Background())
            {
                work = ObserveAfterAsync(gate.Task);
            }

            gate.SetResult();

            Assert.That(await work, Is.True);
        }

        private static async Task<bool> ObserveAfterAsync(Task gate)
        {
            await gate;
            return CommandLogScope.IsBackground;
        }
    }
}
