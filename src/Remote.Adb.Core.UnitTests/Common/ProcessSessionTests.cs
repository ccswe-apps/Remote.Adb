using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Core.Common;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Common;

/// <summary>
/// Runs real (trivial) shell commands to exercise <see cref="ProcessSession"/> — it wraps a live
/// <see cref="System.Diagnostics.Process"/>, so like <see cref="ProcessRunnerTests"/> it can't be faked.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class ProcessSessionTests
{
    private protected ProcessRunner Runner = null!;

    [SetUp]
    public void SetUp()
    {
        Runner = new ProcessRunner(new LoggerFake<ProcessRunner>());
    }

    // A portable shell session: cmd on Windows, sh elsewhere.
    private protected IProcessSession StartShell(string windowsScript, string unixScript) =>
        OperatingSystem.IsWindows()
            ? Runner.StartSession("cmd.exe", ["/c", windowsScript])
            : Runner.StartSession("sh", ["-c", unixScript]);

    // A session that keeps running until it's killed.
    private protected IProcessSession StartLongRunning() =>
        StartShell("ping -n 60 127.0.0.1 >nul", "sleep 60");

    public class When_DisposeAsync_Is_Called : ProcessSessionTests
    {
        [Test]
        public async Task It_keeps_the_exit_code_and_error_readable_afterwards()
        {
            var session = StartShell("echo bind failed 1>&2 & exit 3", "echo bind failed >&2; exit 3");
            await session.WaitForExitAsync();

            await session.DisposeAsync();

            Assert.Multiple(() =>
            {
                Assert.That(session.ExitCode, Is.EqualTo(3));
                Assert.That(session.HasExited, Is.True);
                Assert.That(session.StandardError, Does.Contain("bind failed"));
            });
        }

        [Test]
        public async Task It_kills_a_running_process()
        {
            var session = StartLongRunning();
            var exit = session.WaitForExitAsync();

            await session.DisposeAsync();
            await exit.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(session.HasExited, Is.True);
        }

        [Test]
        public async Task It_is_idempotent_and_makes_kill_a_no_op()
        {
            var session = StartLongRunning();

            await session.DisposeAsync();
            await session.DisposeAsync();

            Assert.DoesNotThrow(session.Kill);
        }
    }

    public class When_WaitForExitAsync_Is_Called : ProcessSessionTests
    {
        [Test]
        public async Task It_returns_the_exit_code_and_captures_standard_error()
        {
            await using var session = StartShell("echo oops 1>&2 & exit 2", "echo oops >&2; exit 2");

            var exitCode = await session.WaitForExitAsync();

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(2));
                Assert.That(session.ExitCode, Is.EqualTo(2));
                Assert.That(session.StandardError, Does.Contain("oops"));
            });
        }

        [Test]
        public async Task It_returns_after_disposal_without_throwing()
        {
            var session = StartLongRunning();
            await session.DisposeAsync();

            var exitCode = await session.WaitForExitAsync();

            Assert.That(exitCode, Is.Not.Zero);
        }
    }
}
