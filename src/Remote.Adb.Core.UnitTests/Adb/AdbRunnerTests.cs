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
    private protected const string MismatchError = "adb server version (40) doesn't match this client (41); killing...\nerror: cannot connect to daemon\n";

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
        ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private protected void VerifyRuns(Times times) =>
        ProcessRunner.Verify(
            runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()),
            times);

    public class When_ForgetServer_Is_Called : AdbRunnerTests
    {
        [Test]
        public async Task It_clears_StartedServer()
        {
            ProcessReturns(new ProcessResult(0, string.Empty, "* daemon not running; starting now at tcp:5037\n* daemon started successfully\n"));
            var runner = CreateRunner();
            await runner.RunAsync(["start-server"]);

            runner.ForgetServer();

            Assert.That(runner.StartedServer, Is.False);
        }
    }

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
        public async Task It_does_not_retry_a_failure_that_is_not_a_rejected_replacement()
        {
            ProcessReturns(new ProcessResult(1, string.Empty, "error: device offline\n"));

            var result = await CreateRunner().RunAsync(["devices"]);

            Assert.That(result.ExitCode, Is.EqualTo(1));
            VerifyRuns(Times.Once());
            ServerProcesses.Verify(processes => processes.KillAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_does_not_retry_when_the_stale_server_cannot_be_killed()
        {
            ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            ProcessReturns(new ProcessResult(1, string.Empty, MismatchError));

            var result = await CreateRunner().RunAsync(["devices"]);

            Assert.That(result.ExitCode, Is.EqualTo(1));
            VerifyRuns(Times.Once());
        }

        [Test]
        public async Task It_kills_a_server_that_refused_to_be_replaced_and_retries_once()
        {
            ProcessReturns(
                new ProcessResult(1, string.Empty, MismatchError),
                new ProcessResult(0, "List of devices attached\n", string.Empty));

            var result = await CreateRunner().RunAsync(["devices"]);

            Assert.That(result.Success, Is.True);
            ServerProcesses.Verify(processes => processes.KillAsync(It.IsAny<CancellationToken>()), Times.Once);
            VerifyRuns(Times.Exactly(2));
        }

        [Test]
        public async Task It_leaves_StartedServer_false_when_adb_did_not_start_the_daemon()
        {
            ProcessReturns(new ProcessResult(0, "List of devices attached\n", string.Empty));
            var runner = CreateRunner();

            await runner.RunAsync(["devices"]);

            Assert.That(runner.StartedServer, Is.False);
        }

        [Test]
        public async Task It_passes_the_adb_path_arguments_stdin_and_environment_through()
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
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task It_sets_StartedServer_when_adb_started_the_daemon()
        {
            ProcessReturns(new ProcessResult(0, string.Empty, "* daemon not running; starting now at tcp:5037\n* daemon started successfully\n"));
            var runner = CreateRunner();

            await runner.RunAsync(["start-server"]);

            Assert.That(runner.StartedServer, Is.True);
        }
    }
}
