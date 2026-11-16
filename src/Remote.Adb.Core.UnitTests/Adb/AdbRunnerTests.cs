using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Common;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Adb;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class AdbRunnerTests
{
    private protected const string AdbPath = "/sdk/platform-tools/adb";
    private protected const string DaemonStarted = "* daemon not running; starting now at tcp:5037\n* daemon started successfully\n";
    private protected const string MismatchError = "adb server version (40) doesn't match this client (41); killing...\nerror: cannot connect to daemon\n";

    private protected static readonly ProcessResult Mismatch = new(1, string.Empty, MismatchError);
    private protected static readonly ProcessResult Ok = new(0, "List of devices attached\n", string.Empty);

    private protected Mock<IProcessRunner> ProcessRunner = null!;
    private protected Mock<IAdbServerProcesses> ServerProcesses = null!;

    private protected AdbRunner CreateRunner()
    {
        var sdk = new Mock<IAndroidSdk>();
        sdk.SetupGet(s => s.AdbPath).Returns(AdbPath);

        return new AdbRunner(ProcessRunner.Object, sdk.Object, ServerProcesses.Object, new LoggerFake<AdbRunner>());
    }

    private protected void ProcessReturns(params ProcessResult[] results)
    {
        var sequence = ProcessRunner.SetupSequence(runner => runner.RunAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<string?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()));

        foreach (var result in results)
        {
            sequence = sequence.ReturnsAsync(result);
        }
    }

    [SetUp]
    public void SetUp()
    {
        ProcessRunner = new Mock<IProcessRunner>();
        ServerProcesses = new Mock<IAdbServerProcesses>();
        ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private protected void VerifyKills(Times times) =>
        ServerProcesses.Verify(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), times);

    private protected void VerifyRuns(Times times) =>
        ProcessRunner.Verify(
            runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            times);

    public class When_IsRejectedServerReplacement_Is_Called : AdbRunnerTests
    {
        [TestCase(1, MismatchError, ExpectedResult = true)]
        [TestCase(1, "error: rejected by remote server\n", ExpectedResult = true)]
        [TestCase(0, MismatchError, ExpectedResult = false)]
        [TestCase(1, "error: device offline\n", ExpectedResult = false)]
        public bool It_detects_a_server_that_refused_to_be_replaced(int exitCode, string standardError) =>
            AdbRunner.IsRejectedServerReplacement(new ProcessResult(exitCode, string.Empty, standardError));
    }

    public class When_ParsePort_Is_Called : AdbRunnerTests
    {
        [TestCase(null, ExpectedResult = 5037)]
        [TestCase("", ExpectedResult = 5037)]
        [TestCase("garbage", ExpectedResult = 5037)]
        [TestCase("0", ExpectedResult = 5037)]
        [TestCase("-1", ExpectedResult = 5037)]
        [TestCase("65536", ExpectedResult = 5037)]
        [TestCase("5038", ExpectedResult = 5038)]
        [TestCase("65535", ExpectedResult = 65535)]
        public int It_parses_a_valid_port_and_falls_back_to_the_default(string? value) => AdbRunner.ParsePort(value);
    }

    public class When_RunAsync_Is_Called : AdbRunnerTests
    {
        [Test]
        public async Task It_does_not_raise_ServerStarted_when_adb_did_not_start_the_daemon()
        {
            ProcessReturns(Ok);
            var runner = CreateRunner();
            var started = 0;
            runner.ServerStarted += (_, _) => started++;

            await runner.RunAsync(["devices"]);

            Assert.That(started, Is.Zero);
        }

        [Test]
        public async Task It_does_not_retry_a_failure_that_is_not_a_rejected_replacement()
        {
            ProcessReturns(new ProcessResult(1, string.Empty, "error: device offline\n"));

            var result = await CreateRunner().RunAsync(["devices"]);

            Assert.That(result.ExitCode, Is.EqualTo(1));
            VerifyRuns(Times.Once());
            VerifyKills(Times.Never());
        }

        [Test]
        public async Task It_does_not_retry_or_raise_ServerReplaced_when_the_stale_server_cannot_be_killed()
        {
            ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            ProcessReturns(Mismatch);
            var runner = CreateRunner();
            var replaced = 0;
            runner.ServerReplaced += (_, _) => replaced++;

            var result = await runner.RunAsync(["devices"]);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(1));
                Assert.That(replaced, Is.Zero);
            });
            VerifyRuns(Times.Once());
        }

        [Test]
        public async Task It_kills_a_server_that_refused_to_be_replaced_retries_once_and_raises_ServerReplaced()
        {
            ProcessReturns(Mismatch, Ok);
            var runner = CreateRunner();
            var replaced = 0;
            runner.ServerReplaced += (_, _) => replaced++;

            var result = await runner.RunAsync(["devices"]);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True);
                Assert.That(replaced, Is.EqualTo(1));
            });
            ServerProcesses.Verify(processes => processes.KillAsync(runner.ServerPort, It.IsAny<CancellationToken>()), Times.Once);
            VerifyRuns(Times.Exactly(2));
        }

        [Test]
        public async Task It_kills_the_stale_server_once_when_concurrent_commands_hit_it()
        {
            ProcessReturns(Mismatch, Mismatch, Ok, Ok);
            var kill = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(kill.Task);
            var runner = CreateRunner();
            var replaced = 0;
            runner.ServerReplaced += (_, _) => replaced++;

            var first = runner.RunAsync(["devices"]);
            var second = runner.RunAsync(["get-state"]);
            kill.SetResult(true);
            var results = await Task.WhenAll(first, second);

            Assert.Multiple(() =>
            {
                Assert.That(results.All(result => result.Success), Is.True);
                Assert.That(replaced, Is.EqualTo(1));
            });
            VerifyKills(Times.Once());
            VerifyRuns(Times.Exactly(4));
        }

        [Test]
        public async Task It_passes_the_adb_path_arguments_stdin_and_environment_through_and_spares_the_server_on_cancel()
        {
            ProcessReturns(new ProcessResult(0, string.Empty, string.Empty));

            await CreateRunner().RunAsync(["pair", "192.168.1.20:37123"], "123456\n");

            ProcessRunner.Verify(
                runner => runner.RunAsync(
                    AdbPath,
                    It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "pair", "192.168.1.20:37123" })),
                    "123456\n",
                    It.Is<IReadOnlyDictionary<string, string>?>(environment =>
                        environment != null && environment.Count == 1 && environment["ADB_REJECT_KILL_SERVER"] == "1"),
                    false,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task It_raises_ServerStarted_when_adb_started_the_daemon()
        {
            ProcessReturns(new ProcessResult(0, string.Empty, DaemonStarted));
            var runner = CreateRunner();
            var started = 0;
            runner.ServerStarted += (_, _) => started++;

            await runner.RunAsync(["start-server"]);

            Assert.That(started, Is.EqualTo(1));
        }

        [Test]
        public async Task It_returns_the_retry_failure_without_retrying_again()
        {
            ProcessReturns(Mismatch, Mismatch);

            var result = await CreateRunner().RunAsync(["devices"]);

            Assert.That(result.ExitCode, Is.EqualTo(1));
            VerifyKills(Times.Once());
            VerifyRuns(Times.Exactly(2));
        }
    }
}
