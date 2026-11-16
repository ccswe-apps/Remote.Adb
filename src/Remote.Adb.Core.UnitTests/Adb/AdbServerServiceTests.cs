using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Common;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Adb;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class AdbServerServiceTests
{
    private protected const int OwnServerProcessId = 100;
    private protected const int Port = 5037;

    private protected AdbRunnerMock Adb = null!;
    private protected Mock<IListeningProcessFinder> ListeningProcesses = null!;
    private protected Mock<IPortProbe> PortProbe = null!;
    private protected Mock<IAdbServerProcesses> ServerProcesses = null!;

    private protected AdbServerService CreateService() =>
        new(Adb.Object, ServerProcesses.Object, ListeningProcesses.Object, PortProbe.Object, new LoggerFake<AdbServerService>());

    // The process listening on the server port, as the finder reports it.
    private protected void ListenerIs(int? processId) =>
        ListeningProcesses.Setup(finder => finder.FindAsync(Port, It.IsAny<CancellationToken>())).ReturnsAsync(processId);

    private protected void ServerListening(bool listening) =>
        PortProbe.Setup(probe => probe.IsListeningAsync(Port, It.IsAny<CancellationToken>())).ReturnsAsync(listening);

    [SetUp]
    public void SetUp()
    {
        Adb = new AdbRunnerMock();
        Adb.Inner.SetupGet(runner => runner.ServerPort).Returns(Port);
        Adb.Returns(["start-server"], string.Empty);
        ListeningProcesses = new Mock<IListeningProcessFinder>();
        PortProbe = new Mock<IPortProbe>();
        ServerProcesses = new Mock<IAdbServerProcesses>();
        ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    // Makes `adb start-server` report that it started the daemon, as the runner signals with ServerStarted.
    private protected void StartServerStartsTheDaemon() =>
        Adb.Inner
            .Setup(runner => runner.RunAsync(It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "start-server" })), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => Adb.Inner.Raise(runner => runner.ServerStarted += null, EventArgs.Empty))
            .ReturnsAsync(new ProcessResult(0, string.Empty, "* daemon started successfully\n"));

    private protected void VerifyKills(Times times) =>
        ServerProcesses.Verify(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), times);

    public class When_EnsureOwnServerAsync_Is_Called : AdbServerServiceTests
    {
        [Test]
        public async Task It_just_starts_when_nothing_is_running()
        {
            ServerListening(false);

            await CreateService().EnsureOwnServerAsync();

            Adb.Verify(["start-server"], Times.Once());
            VerifyKills(Times.Never());
        }

        [Test]
        public async Task It_just_starts_when_the_running_server_is_the_one_the_app_started()
        {
            ServerListening(true);
            ListenerIs(OwnServerProcessId);
            var service = CreateService();
            Adb.Inner.Raise(runner => runner.ServerStarted += null, EventArgs.Empty);

            var result = await service.EnsureOwnServerAsync();

            Assert.That(result, Is.True);
            Adb.Verify(["start-server"], Times.Once());
            VerifyKills(Times.Never());
        }

        [Test]
        public async Task It_replaces_a_server_that_took_over_after_the_app_started_one()
        {
            ServerListening(true);
            ListenerIs(OwnServerProcessId);
            StartServerStartsTheDaemon();
            var service = CreateService();
            await service.StartAsync();
            ListenerIs(200);

            await service.EnsureOwnServerAsync();

            VerifyKills(Times.Once());
        }

        [Test]
        public async Task It_restarts_a_running_server_the_app_did_not_start()
        {
            ServerListening(true);
            ListenerIs(OwnServerProcessId);
            var service = CreateService();
            var restarted = false;
            service.ServerRestarted += (_, _) => restarted = true;

            var result = await service.EnsureOwnServerAsync();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(restarted, Is.True);
            });
            ServerProcesses.Verify(processes => processes.KillAsync(Port, It.IsAny<CancellationToken>()), Times.Once);
            Adb.Verify(["start-server"], Times.Once());
        }
    }

    public class When_IsRunningAsync_Is_Called : AdbServerServiceTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task It_reports_whether_the_port_probe_finds_a_listener(bool listening)
        {
            PortProbe.Setup(probe => probe.IsListeningAsync(6000, It.IsAny<CancellationToken>())).ReturnsAsync(listening);

            var result = await CreateService().IsRunningAsync(6000);

            Assert.That(result, Is.EqualTo(listening));
        }
    }

    public class When_KillAsync_Is_Called : AdbServerServiceTests
    {
        [Test]
        public async Task It_kills_the_server_on_the_server_port()
        {
            var result = await CreateService().KillAsync();

            Assert.That(result, Is.True);
            ServerProcesses.Verify(processes => processes.KillAsync(Port, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task It_never_runs_adb_kill_server()
        {
            await CreateService().KillAsync();

            Adb.Verify(["kill-server"], Times.Never());
            Adb.VerifyAny(Times.Never());
        }

        [Test]
        public async Task It_reports_a_failed_kill()
        {
            ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await CreateService().KillAsync();

            Assert.That(result, Is.False);
        }
    }

    public class When_RestartAsync_Is_Called : AdbServerServiceTests
    {
        [Test]
        public async Task It_kills_then_starts_raises_ServerRestarted_and_returns_true()
        {
            ServerListening(true);
            var service = CreateService();
            var restarted = false;
            service.ServerRestarted += (_, _) => restarted = true;

            var result = await service.RestartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(restarted, Is.True);
            });
            VerifyKills(Times.Once());
            Adb.Verify(["start-server"], Times.Once());
            Adb.Verify(["kill-server"], Times.Never());
        }

        [Test]
        public async Task It_returns_false_without_raising_when_the_server_does_not_come_back()
        {
            ServerListening(false);
            var service = CreateService();
            var restarted = false;
            service.ServerRestarted += (_, _) => restarted = true;

            var result = await service.RestartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.False);
                Assert.That(restarted, Is.False);
            });
        }

        [Test]
        public async Task It_returns_false_without_starting_or_raising_when_the_kill_fails()
        {
            ServerListening(true);
            ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var service = CreateService();
            var restarted = false;
            service.ServerRestarted += (_, _) => restarted = true;

            var result = await service.RestartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.False);
                Assert.That(restarted, Is.False);
            });
            Adb.Verify(["start-server"], Times.Never());
        }
    }

    public class When_StartAsync_Is_Called : AdbServerServiceTests
    {
        [Test]
        public async Task It_pins_a_server_it_started_to_the_listening_process()
        {
            ServerListening(true);
            ListenerIs(OwnServerProcessId);
            StartServerStartsTheDaemon();
            var service = CreateService();

            await service.StartAsync();
            Adb.Returns(["start-server"], string.Empty);
            await service.EnsureOwnServerAsync();

            ListeningProcesses.Verify(finder => finder.FindAsync(Port, It.IsAny<CancellationToken>()), Times.Exactly(2));
            VerifyKills(Times.Never());
        }

        [Test]
        public async Task It_returns_false_when_nothing_listens_afterwards()
        {
            ServerListening(false);

            var result = await CreateService().StartAsync();

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task It_returns_false_when_start_server_fails()
        {
            ServerListening(true);
            Adb.Returns(["start-server"], string.Empty, exitCode: 1, stderr: "error: could not install *smartsocket* listener");

            var result = await CreateService().StartAsync();

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task It_runs_adb_start_server_and_returns_true_when_the_server_listens()
        {
            ServerListening(true);

            var result = await CreateService().StartAsync();

            Assert.That(result, Is.True);
            Adb.Verify(["start-server"], Times.Once());
        }
    }

    public class When_The_Runner_Replaces_A_Stale_Server : AdbServerServiceTests
    {
        [Test]
        public void It_raises_ServerRestarted()
        {
            var service = CreateService();
            var restarted = false;
            service.ServerRestarted += (_, _) => restarted = true;

            Adb.Inner.Raise(runner => runner.ServerReplaced += null, EventArgs.Empty);

            Assert.That(restarted, Is.True);
        }
    }
}
