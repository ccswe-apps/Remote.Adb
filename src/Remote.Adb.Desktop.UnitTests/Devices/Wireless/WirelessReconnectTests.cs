using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Tunnel;
using Remote.Adb.Desktop.Devices.Wireless;
using Remote.Adb.Testing;

namespace Remote.Adb.Desktop.UnitTests.Devices.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class WirelessReconnectTests
{
    // A hang guard only: every wait below completes as soon as the awaited call happens.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);

    private protected Mock<IAdbServerService> AdbServer = null!;
    private protected int Calls;
    private protected RecordingLogger<WirelessReconnect> Logger = null!;

    // One pair per ReconnectSavedAsync call: Started[n] completes when call n begins, and call n returns Results[n]'s task.
    private protected List<TaskCompletionSource<int>> Results = null!;
    private protected WirelessReconnect Service = null!;
    private protected List<TaskCompletionSource> Started = null!;
    private protected Mock<ITunnelService> Tunnel = null!;
    private protected WirelessMock Wireless = null!;

    [SetUp]
    public void SetUp()
    {
        Calls = 0;
        Started = [.. Enumerable.Range(0, 8).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))];
        Results = [.. Enumerable.Range(0, 8).Select(_ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously))];
        AdbServer = new Mock<IAdbServerService>();
        Logger = new RecordingLogger<WirelessReconnect> { MinimumLevel = LogLevel.Warning };
        Tunnel = new Mock<ITunnelService>();
        Wireless = new WirelessMock();
        Wireless.Inner
            .Setup(wireless => wireless.ReconnectSavedAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var index = Interlocked.Increment(ref Calls) - 1;
                Started[index].SetResult();
                return Results[index].Task;
            });
        Service = new WirelessReconnect(Wireless.Object, AdbServer.Object, Tunnel.Object, Logger);
    }

    [TearDown]
    public async Task TearDown()
    {
        await Service.StopAsync(CancellationToken.None);
        Service.Dispose();
    }

    // Completes the given reconnect call (zero-based) once it has started.
    private protected async Task CompleteCallAsync(int index)
    {
        await Started[index].Task.WaitAsync(HangGuard);
        Results[index].SetResult(0);
    }

    private protected void RaiseServerRestarted() => AdbServer.Raise(server => server.ServerRestarted += null, AdbServer.Object, EventArgs.Empty);

    private protected Task WaitForCallAsync(int index) => Started[index].Task.WaitAsync(HangGuard);

    public class When_Started : WirelessReconnectTests
    {
        [Test]
        public async Task It_reconnects_saved_devices_at_launch()
        {
            await Service.StartAsync(CancellationToken.None);

            await CompleteCallAsync(0);

            Assert.That(Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task It_reconnects_after_the_adb_server_restarts()
        {
            await Service.StartAsync(CancellationToken.None);
            await CompleteCallAsync(0);

            RaiseServerRestarted();
            await CompleteCallAsync(1);

            Assert.That(Calls, Is.EqualTo(2));
        }

        [Test]
        public async Task It_reconnects_when_the_tunnel_comes_up_but_not_on_other_states()
        {
            await Service.StartAsync(CancellationToken.None);
            await CompleteCallAsync(0);

            Tunnel.Raise(tunnel => tunnel.StatusChanged += null, Tunnel.Object, new TunnelStatus(TunnelState.Connecting, null));
            Tunnel.Raise(tunnel => tunnel.StatusChanged += null, Tunnel.Object, new TunnelStatus(TunnelState.Connected, null));
            await CompleteCallAsync(1);
            await Service.StopAsync(CancellationToken.None);

            Assert.That(Calls, Is.EqualTo(2));
        }

        [Test]
        public async Task It_collapses_requests_made_during_a_reconnect_into_one_follow_up_pass()
        {
            await Service.StartAsync(CancellationToken.None);
            await WaitForCallAsync(0);

            RaiseServerRestarted();
            RaiseServerRestarted();
            RaiseServerRestarted();
            Results[0].SetResult(0);
            await CompleteCallAsync(1);
            await Service.StopAsync(CancellationToken.None);

            Assert.That(Calls, Is.EqualTo(2));
        }

        [Test]
        public async Task It_logs_a_failed_reconnect_and_keeps_listening()
        {
            await Service.StartAsync(CancellationToken.None);
            await WaitForCallAsync(0);

            Results[0].SetException(new InvalidOperationException("adb exploded"));
            RaiseServerRestarted();
            await CompleteCallAsync(1);

            Assert.Multiple(() =>
            {
                Assert.That(Calls, Is.EqualTo(2));
                Assert.That(Logger.Entries.Single().Exception?.Message, Is.EqualTo("adb exploded"));
            });
        }
    }
}
