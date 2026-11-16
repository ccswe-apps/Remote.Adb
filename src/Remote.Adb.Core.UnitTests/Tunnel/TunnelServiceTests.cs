using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Settings;
using Remote.Adb.Core.Tunnel;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Tunnel;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class TunnelServiceTests
{
    private protected const string SshPath = "/usr/bin/ssh";

    // How far the fake clock moves per step while driving the service; small next to every interval below.
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

    private protected Mock<IAdbServerService> AdbServer = null!;
    private protected FakeTimeProvider Clock = null!;
    private protected Mock<IExecutableFinder> Finder = null!;
    private protected RecordingLogger<TunnelService> Logger = null!;
    private protected ProcessRunnerMock ProcessRunner = null!;
    private protected TunnelService? Service;
    private protected Mock<ISettingsService> Settings = null!;

    [SetUp]
    public void SetUp()
    {
        AdbServer = new Mock<IAdbServerService>();
        AdbServer.Setup(a => a.EnsureOwnServerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        AdbServer.Setup(a => a.IsRunningAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Clock = new FakeTimeProvider();

        Finder = new Mock<IExecutableFinder>();
        Finder.Setup(f => f.FindOnPath("ssh")).Returns(SshPath);

        Logger = new RecordingLogger<TunnelService>();
        ProcessRunner = new ProcessRunnerMock();

        Settings = new Mock<ISettingsService>();
        Settings.SetupGet(s => s.TunnelHost).Returns("devhost");
        Settings.SetupGet(s => s.TunnelRemotePort).Returns(5037);
        Settings.SetupGet(s => s.TunnelLocalPort).Returns(5037);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (Service is not null)
        {
            await Service.DisposeAsync();
            Service = null;
        }
    }

    private protected TunnelService CreateService()
    {
        Service = new TunnelService(ProcessRunner.Object, AdbServer.Object, Settings.Object, Finder.Object, Clock, Logger)
        {
            AttemptTimeout = TimeSpan.FromSeconds(15),
            HealthCheckInterval = TimeSpan.FromSeconds(5),
            MaxReconnectAttempts = 3,
            ReconnectBackoff = TimeSpan.FromSeconds(2),
            SettleWindow = TimeSpan.FromSeconds(1.5),
        };

        return Service;
    }

    // Moves the fake clock forward in small steps until `task` completes, yielding between steps so continuations
    // scheduled on the thread pool run. The real-time bound is only a hang guard.
    private protected async Task DriveAsync(Task task)
    {
        for (var i = 0; i < 10_000 && !task.IsCompleted; i++)
        {
            Clock.Advance(Step);
            await Task.Yield();
        }

        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private protected async Task<TunnelStatus> DriveUntilAsync(Task<TunnelStatus> waiter)
    {
        await DriveAsync(waiter);
        return await waiter;
    }

    private protected void VerifySessionsStarted(Times times) => ProcessRunner.VerifySessionAny(times);

    // Completes with the first status of `state` from now on (or now, if the service is already in it). Create it
    // before triggering the transition so the event can't be missed.
    private protected static Task<TunnelStatus> WaitForStateAsync(ITunnelService service, TunnelState state)
    {
        var tcs = new TaskCompletionSource<TunnelStatus>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, TunnelStatus status)
        {
            if (status.State == state && tcs.TrySetResult(status))
            {
                service.StatusChanged -= Handler;
            }
        }

        service.StatusChanged += Handler;
        if (service.Status.State == state && tcs.TrySetResult(service.Status))
        {
            service.StatusChanged -= Handler;
        }

        return tcs.Task;
    }

    public class When_ConnectAsync_Is_Called : TunnelServiceTests
    {
        [Test]
        public async Task It_faults_when_no_host_is_configured()
        {
            Settings.SetupGet(s => s.TunnelHost).Returns((string?)null);
            var service = CreateService();

            await service.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Faulted));
                Assert.That(service.Status.Message, Does.Contain("No remote host"));
            });
        }

        [Test]
        public async Task It_faults_when_ssh_is_not_on_path()
        {
            Finder.Setup(f => f.FindOnPath("ssh")).Returns((string?)null);
            var service = CreateService();

            await service.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Faulted));
                Assert.That(service.Status.Message, Does.Contain("ssh"));
            });
        }

        [Test]
        public async Task It_connects_when_the_forward_stays_up()
        {
            ProcessRunner.ReturnsForAny();
            ProcessRunner.StartsSession(() => new FakeProcessSession());
            var service = CreateService();

            await DriveAsync(service.ConnectAsync());

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Connected));
                Assert.That(service.Status.Message, Does.Contain("127.0.0.1:5037"));
            });
            AdbServer.Verify(a => a.EnsureOwnServerAsync(It.IsAny<CancellationToken>()), Times.Once);
            ProcessRunner.VerifySession(
                SshPath,
                a => a.Contains("-N") && a.Contains("-R") && a.Contains("5037:127.0.0.1:5037") && a.Contains("ExitOnForwardFailure=yes") && a.Contains("devhost"),
                Times.Once());
        }

        [Test]
        public async Task It_connects_to_the_host_override_instead_of_the_saved_host()
        {
            ProcessRunner.ReturnsForAny();
            ProcessRunner.StartsSession(() => new FakeProcessSession());
            var service = CreateService();

            await DriveAsync(service.ConnectAsync(" otherhost "));

            ProcessRunner.VerifySession(SshPath, a => a.Contains("otherhost") && !a.Contains("devhost"), Times.Once());
        }

        [Test]
        public async Task It_kills_the_remote_adb_before_binding()
        {
            ProcessRunner.ReturnsForAny();
            ProcessRunner.StartsSession(() => new FakeProcessSession());
            var service = CreateService();

            await DriveAsync(service.ConnectAsync());

            ProcessRunner.VerifyRun(SshPath, a => a.Contains("devhost") && a.Any(part => part.Contains("pkill -x adb")), Times.Once());
        }

        [Test]
        public async Task It_retries_the_bind_race_then_connects()
        {
            ProcessRunner.ReturnsForAny();
            var lost = new FakeProcessSession(immediateExitCode: 255, standardError: "remote port forwarding failed");
            ProcessRunner.StartsSessions(lost, new FakeProcessSession());
            var service = CreateService();

            await DriveAsync(service.ConnectAsync());

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Connected));
                Assert.That(lost.IsDisposed, Is.True);
            });
            VerifySessionsStarted(Times.Exactly(2));
        }

        [Test]
        public async Task It_faults_with_the_exit_code_when_every_bind_attempt_fails_silently()
        {
            ProcessRunner.ReturnsForAny();
            ProcessRunner.StartsSession(() => new FakeProcessSession(immediateExitCode: 255));
            var service = CreateService();

            await DriveAsync(service.ConnectAsync());

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Faulted));
                Assert.That(service.Status.Message, Does.Contain("could not bind after 3 attempts"));
                Assert.That(service.Status.Message, Does.Contain("ssh exited with code 255"));
            });
            VerifySessionsStarted(Times.Exactly(3));
        }

        [Test]
        public async Task It_faults_with_the_ssh_error_when_the_remote_is_unreachable()
        {
            ProcessRunner.ReturnsForAny(exitCode: 255, stderr: "Could not resolve hostname devhost");
            var service = CreateService();

            await DriveAsync(service.ConnectAsync());

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Faulted));
                Assert.That(service.Status.Message, Does.Contain("Could not resolve hostname devhost"));
            });
            VerifySessionsStarted(Times.Never());
        }

        [Test]
        public async Task It_faults_when_the_local_adb_server_is_not_listening()
        {
            AdbServer.Setup(a => a.IsRunningAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var service = CreateService();

            await service.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Faulted));
                Assert.That(service.Status.Message, Does.Contain("not listening"));
            });
            ProcessRunner.VerifyRunAny(Times.Never());
        }

        [Test]
        public async Task It_warns_but_connects_when_the_app_does_not_own_the_adb_server()
        {
            AdbServer.Setup(a => a.EnsureOwnServerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            ProcessRunner.ReturnsForAny();
            ProcessRunner.StartsSession(() => new FakeProcessSession());
            var service = CreateService();

            await DriveAsync(service.ConnectAsync());

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Connected));
                Assert.That(Logger.Entries, Has.Some.Matches<RecordedLog>(log => log is { Level: LogLevel.Warning } && log.Message.Contains("started by the app")));
            });
        }

        [Test]
        public async Task It_times_out_a_stalled_ssh_and_faults_after_exhausting_attempts()
        {
            ProcessRunner.StallsForAny();
            var service = CreateService();

            await DriveAsync(service.ConnectAsync());

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Faulted));
                Assert.That(service.Status.Message, Does.Contain("did not respond within 15s"));
            });
            ProcessRunner.VerifyRunAny(Times.Exactly(3));
        }

        [Test]
        public async Task It_reports_disconnected_when_cancelled_while_settling()
        {
            ProcessRunner.ReturnsForAny();
            var session = new FakeProcessSession();
            ProcessRunner.StartsSession(() => session);
            var service = CreateService();
            using var cancel = new CancellationTokenSource();

            var connect = service.ConnectAsync(cancellationToken: cancel.Token);
            await cancel.CancelAsync();
            await connect;

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Disconnected));
                Assert.That(session.IsDisposed, Is.True);
            });
        }
    }

    public class When_DisconnectAsync_Is_Called : TunnelServiceTests
    {
        [Test]
        public async Task It_kills_the_session_and_reports_disconnected()
        {
            ProcessRunner.ReturnsForAny();
            var session = new FakeProcessSession();
            ProcessRunner.StartsSession(() => session);
            var service = CreateService();
            await DriveAsync(service.ConnectAsync());

            await service.DisconnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Disconnected));
                Assert.That(session.KillCount, Is.EqualTo(1));
                Assert.That(session.IsDisposed, Is.True);
            });
        }

        [Test]
        public async Task It_survives_overlapping_connect_and_disconnect_without_throwing()
        {
            ProcessRunner.ReturnsForAny();
            ProcessRunner.StartsSession(() => new FakeProcessSession());
            var service = CreateService();

            // Overlap connect with a disconnect that cancels the in-flight connect; the connect CTS is shared, so
            // a Cancel()-on-disposed-CTS race would surface here as an exception out of one of the awaited tasks.
            for (var i = 0; i < 25; i++)
            {
                await DriveAsync(Task.WhenAll(service.ConnectAsync(), service.DisconnectAsync()));
            }

            await service.DisconnectAsync();

            Assert.That(service.Status.State, Is.EqualTo(TunnelState.Disconnected));
        }
    }

    public class When_DisposeAsync_Is_Called : TunnelServiceTests
    {
        [Test]
        public async Task It_kills_the_live_session()
        {
            ProcessRunner.ReturnsForAny();
            var session = new FakeProcessSession();
            ProcessRunner.StartsSession(() => session);
            var service = CreateService();
            await DriveAsync(service.ConnectAsync());

            await service.DisposeAsync();

            Assert.Multiple(() =>
            {
                Assert.That(session.KillCount, Is.EqualTo(1));
                Assert.That(session.IsDisposed, Is.True);
            });
        }
    }

    public class When_The_Tunnel_Drops : TunnelServiceTests
    {
        [Test]
        public async Task It_auto_reconnects_after_a_connected_session_exits()
        {
            ProcessRunner.ReturnsForAny();
            var first = new FakeProcessSession();
            ProcessRunner.StartsSessions(first, new FakeProcessSession());
            var service = CreateService();
            await DriveAsync(service.ConnectAsync());

            var reconnecting = WaitForStateAsync(service, TunnelState.Reconnecting);
            first.Exit(1, "connection closed by remote host");
            var dropped = await DriveUntilAsync(reconnecting);
            var reconnected = WaitForStateAsync(service, TunnelState.Connected);
            await DriveUntilAsync(reconnected);

            Assert.Multiple(() =>
            {
                Assert.That(dropped.Message, Does.Contain("connection closed by remote host"));
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Connected));
                Assert.That(first.IsDisposed, Is.True);
            });
            VerifySessionsStarted(Times.Exactly(2));
        }

        [Test]
        public async Task It_reconnects_to_the_host_it_was_connected_to()
        {
            ProcessRunner.ReturnsForAny();
            var first = new FakeProcessSession();
            ProcessRunner.StartsSessions(first, new FakeProcessSession());
            var service = CreateService();
            await DriveAsync(service.ConnectAsync("otherhost"));

            var reconnected = WaitForStateAsync(service, TunnelState.Reconnecting).ContinueWith(_ => WaitForStateAsync(service, TunnelState.Connected)).Unwrap();
            first.Exit(1);
            await DriveUntilAsync(reconnected);

            ProcessRunner.VerifySession(SshPath, a => a.Contains("otherhost"), Times.Exactly(2));
            ProcessRunner.VerifySession(SshPath, a => a.Contains("devhost"), Times.Never());
        }

        [Test]
        public async Task It_reconnects_when_the_health_monitor_sees_the_adb_server_stop()
        {
            ProcessRunner.ReturnsForAny();
            var first = new FakeProcessSession();
            ProcessRunner.StartsSessions(first, new FakeProcessSession());
            var service = CreateService();
            await DriveAsync(service.ConnectAsync());

            AdbServer.Setup(a => a.IsRunningAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var dropped = await DriveUntilAsync(WaitForStateAsync(service, TunnelState.Reconnecting));
            AdbServer.Setup(a => a.IsRunningAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            await DriveUntilAsync(WaitForStateAsync(service, TunnelState.Connected));

            Assert.Multiple(() =>
            {
                Assert.That(dropped.Message, Does.Contain("local adb server stopped"));
                Assert.That(first.IsDisposed, Is.True);
                Assert.That(service.Status.State, Is.EqualTo(TunnelState.Connected));
            });
            VerifySessionsStarted(Times.Exactly(2));
        }

        [Test]
        public async Task It_faults_after_exhausting_reconnect_attempts()
        {
            ProcessRunner.ReturnsForAny();
            var session = new FakeProcessSession();
            ProcessRunner.StartsSession(() => session);
            var service = CreateService();
            await DriveAsync(service.ConnectAsync());

            // The remote becomes unreachable, so every reconnect fails — after the bounded attempts it faults.
            ProcessRunner.ReturnsForAny(exitCode: 255, stderr: "Connection refused");
            var faulted = WaitForStateAsync(service, TunnelState.Faulted);
            session.Exit(1, "connection closed by remote host");
            var status = await DriveUntilAsync(faulted);

            Assert.That(status.Message, Does.Contain("couldn't reconnect after 3 attempts").And.Contain("Connection refused"));
        }

        [Test]
        public async Task It_faults_after_reconnects_fail_when_the_local_adb_server_stays_down()
        {
            ProcessRunner.ReturnsForAny();
            var session = new FakeProcessSession();
            ProcessRunner.StartsSession(() => session);
            var service = CreateService();
            await DriveAsync(service.ConnectAsync());

            // Server dies and stays down: the health monitor drops the tunnel, and every reconnect fails its
            // server-liveness check, so after the bounded attempts it faults rather than looping forever.
            AdbServer.Setup(a => a.IsRunningAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var status = await DriveUntilAsync(WaitForStateAsync(service, TunnelState.Faulted));

            Assert.Multiple(() =>
            {
                Assert.That(status.Message, Does.Contain("reconnect"));
                Assert.That(session.KillCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task It_stops_reconnecting_when_the_user_disconnects()
        {
            ProcessRunner.ReturnsForAny();
            var session = new FakeProcessSession();
            ProcessRunner.StartsSession(() => session);
            var service = CreateService();
            await DriveAsync(service.ConnectAsync());

            // The clock stays put from here, so the supervisor is parked in its first backoff.
            var reconnecting = WaitForStateAsync(service, TunnelState.Reconnecting);
            session.Exit(1, "connection closed by remote host");
            await reconnecting.WaitAsync(TimeSpan.FromSeconds(5));
            await service.DisconnectAsync();
            Clock.Advance(TimeSpan.FromMinutes(1));

            Assert.That(service.Status.State, Is.EqualTo(TunnelState.Disconnected));
            VerifySessionsStarted(Times.Once());
        }
    }
}
