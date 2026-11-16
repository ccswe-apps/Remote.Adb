using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Moq;
using NUnit.Framework;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.UnitTests.Fakes;

namespace Remote.Adb.Core.UnitTests.Adb;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class AdbServerServiceTests
{
    protected AdbRunnerMock Adb = null!;
    protected TcpListener? Listener;
    protected Mock<IAdbServerProcesses> ServerProcesses = null!;

    protected AdbServerService CreateService() => new(Adb.Object, ServerProcesses.Object, new LoggerFake<AdbServerService>());

    // Reserves a free loopback port and points the runner's ServerPort at it; `listening` decides whether the port is
    // left bound (a running server) or released again (nothing listening).
    protected void ServerListening(bool listening)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Adb.Inner.SetupGet(runner => runner.ServerPort).Returns(port);

        if (listening)
        {
            Listener = listener;
        }
        else
        {
            listener.Stop();
            listener.Dispose();
        }
    }

    [SetUp]
    public void SetUp()
    {
        Adb = new AdbRunnerMock();
        Adb.Returns(["start-server"], string.Empty);
        ServerProcesses = new Mock<IAdbServerProcesses>();
        ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    [TearDown]
    public void TearDown()
    {
        Listener?.Stop();
        Listener?.Dispose();
        Listener = null;
    }

    public class When_EnsureOwnServerAsync_Is_Called : AdbServerServiceTests
    {
        [Test]
        public async Task It_just_starts_when_the_app_started_the_running_server()
        {
            ServerListening(true);
            Adb.Inner.SetupGet(runner => runner.StartedServer).Returns(true);

            var result = await CreateService().EnsureOwnServerAsync();

            Assert.That(result, Is.True);
            Adb.Verify(["start-server"], Times.Once());
            ServerProcesses.Verify(processes => processes.KillAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_just_starts_when_nothing_is_running()
        {
            ServerListening(false);

            await CreateService().EnsureOwnServerAsync();

            Adb.Verify(["start-server"], Times.Once());
            ServerProcesses.Verify(processes => processes.KillAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_restarts_a_running_server_the_app_did_not_start()
        {
            ServerListening(true);
            var service = CreateService();
            var restarted = false;
            service.ServerRestarted += (_, _) => restarted = true;

            var result = await service.EnsureOwnServerAsync();

            Assert.That(result, Is.True);
            Assert.That(restarted, Is.True);
            ServerProcesses.Verify(processes => processes.KillAsync(It.IsAny<CancellationToken>()), Times.Once);
            Adb.Verify(["start-server"], Times.Once());
        }
    }

    public class When_IsRunningAsync_Is_Called : AdbServerServiceTests
    {
        [Test]
        public async Task It_reflects_whether_a_listener_is_bound_to_the_port()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var service = CreateService();

            var whileListening = await service.IsRunningAsync(port);
            listener.Stop();
            listener.Dispose();
            var afterStopped = await service.IsRunningAsync(port);

            Assert.That(whileListening, Is.True);
            Assert.That(afterStopped, Is.False);
        }

        [Test]
        public async Task It_returns_false_for_an_out_of_range_port()
        {
            var service = CreateService();

            Assert.That(await service.IsRunningAsync(70000), Is.False);
        }
    }

    public class When_KillAsync_Is_Called : AdbServerServiceTests
    {
        [Test]
        public async Task It_does_not_forget_the_server_when_the_kill_fails()
        {
            ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await CreateService().KillAsync();

            Assert.That(result, Is.False);
            Adb.Inner.Verify(runner => runner.ForgetServer(), Times.Never);
        }

        [Test]
        public async Task It_kills_the_server_process_and_forgets_the_server()
        {
            var result = await CreateService().KillAsync();

            Assert.That(result, Is.True);
            ServerProcesses.Verify(processes => processes.KillAsync(It.IsAny<CancellationToken>()), Times.Once);
            Adb.Inner.Verify(runner => runner.ForgetServer(), Times.Once);
        }

        [Test]
        public async Task It_never_runs_adb_kill_server()
        {
            await CreateService().KillAsync();

            Adb.Verify(["kill-server"], Times.Never());
            Adb.VerifyAny(Times.Never());
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

            Assert.That(result, Is.True);
            Assert.That(restarted, Is.True);
            ServerProcesses.Verify(processes => processes.KillAsync(It.IsAny<CancellationToken>()), Times.Once);
            Adb.Verify(["start-server"], Times.Once());
            Adb.Verify(["kill-server"], Times.Never());
        }

        [Test]
        public async Task It_returns_false_without_starting_or_raising_when_the_kill_fails()
        {
            ServerListening(true);
            ServerProcesses.Setup(processes => processes.KillAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var service = CreateService();
            var restarted = false;
            service.ServerRestarted += (_, _) => restarted = true;

            var result = await service.RestartAsync();

            Assert.That(result, Is.False);
            Assert.That(restarted, Is.False);
            Adb.Verify(["start-server"], Times.Never());
        }

        [Test]
        public async Task It_returns_false_without_raising_when_the_server_does_not_come_back()
        {
            ServerListening(false);
            var service = CreateService();
            var restarted = false;
            service.ServerRestarted += (_, _) => restarted = true;

            var result = await service.RestartAsync();

            Assert.That(result, Is.False);
            Assert.That(restarted, Is.False);
        }
    }

    public class When_StartAsync_Is_Called : AdbServerServiceTests
    {
        [Test]
        public async Task It_returns_false_when_start_server_fails()
        {
            ServerListening(true);
            Adb.Returns(["start-server"], string.Empty, exitCode: 1, stderr: "error: could not install *smartsocket* listener");

            var result = await CreateService().StartAsync();

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task It_returns_false_when_nothing_listens_afterwards()
        {
            ServerListening(false);

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
}
