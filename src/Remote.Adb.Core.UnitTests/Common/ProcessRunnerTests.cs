using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Logging;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Common;

/// <summary>Runs real (trivial) shell commands to exercise <see cref="ProcessRunner"/>'s command logging.</summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class ProcessRunnerTests
{
    private protected RecordingLogger<ProcessRunner> Logger = null!;
    private protected ProcessRunner Runner = null!;

    [SetUp]
    public void SetUp()
    {
        Logger = new RecordingLogger<ProcessRunner>();
        Runner = new ProcessRunner(Logger);
    }

    private protected IEnumerable<RecordedLog> Logged(int eventId) => Logger.Entries.Where(entry => entry.EventId.Id == eventId);

    // A portable shell invocation: cmd on Windows, sh elsewhere.
    private protected Task<ProcessResult> RunShellAsync(string script, CancellationToken cancellationToken = default) =>
        OperatingSystem.IsWindows()
            ? Runner.RunAsync("cmd.exe", ["/c", script], cancellationToken: cancellationToken)
            : Runner.RunAsync("sh", ["-c", script], cancellationToken: cancellationToken);

    public class When_RunAsync_Is_Called : ProcessRunnerTests
    {
        [Test]
        public async Task It_logs_the_completion_with_exit_code_and_duration_at_information()
        {
            await RunShellAsync("exit 0");

            var completed = Logged(LogEvents.CommandCompleted).Single();
            Assert.Multiple(() =>
            {
                Assert.That(completed.Level, Is.EqualTo(LogLevel.Information));
                Assert.That(completed.Message, Does.Match(@"exited with code 0 in \d+ ms"));
            });
        }

        [Test]
        public async Task It_logs_a_non_zero_exit_as_a_warning()
        {
            var result = await RunShellAsync("exit 3");

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(3));
                Assert.That(Logged(LogEvents.CommandCompleted).Single().Level, Is.EqualTo(LogLevel.Warning));
            });
        }

        [Test]
        public async Task It_logs_the_output()
        {
            await RunShellAsync("echo hello");

            Assert.That(Logged(LogEvents.CommandOutput).Single().Message, Does.Contain("stdout").And.Contain("hello"));
        }

        [Test]
        public async Task It_logs_at_debug_without_output_inside_a_background_scope()
        {
            using (CommandLogScope.Background())
            {
                await RunShellAsync("echo hello & exit 3");
            }

            Assert.Multiple(() =>
            {
                Assert.That(Logged(LogEvents.CommandCompleted).Single().Level, Is.EqualTo(LogLevel.Debug));
                Assert.That(Logged(LogEvents.CommandOutput), Is.Empty);
            });
        }

        [Test]
        public async Task It_logs_a_cancellation()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var script = OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1 > nul" : "sleep 30";

            await Assert.CatchAsync<OperationCanceledException>(() => RunShellAsync(script, cancellation.Token));

            Assert.That(Logged(LogEvents.CommandCancelled).Single().Level, Is.EqualTo(LogLevel.Information));
        }

        [Test]
        public async Task It_logs_a_launch_failure()
        {
            await Assert.ThrowsAsync<ProcessLaunchException>(() => Runner.RunAsync("definitely-not-a-real-tool-4f1c", []));

            Assert.That(Logged(LogEvents.CommandLaunchFailed).Single().Level, Is.EqualTo(LogLevel.Warning));
        }
    }

    public class When_FormatCommand_Is_Called : ProcessRunnerTests
    {
        [Test]
        public void It_uses_the_tool_name_and_quotes_arguments_with_spaces()
        {
            var command = ProcessRunner.FormatCommand(Path.Combine("sdk", "platform-tools", "adb.exe"), ["shell", "echo hi"]);

            Assert.That(command, Is.EqualTo("adb shell \"echo hi\""));
        }
    }
}
