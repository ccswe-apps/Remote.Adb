using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Tunnel;
using Remote.Adb.Desktop.Devices.Wireless;

namespace Remote.Adb.Desktop.UnitTests.Devices.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class WirelessReconnectTests
{
    private protected Mock<IAdbServerService> AdbServer = null!;
    private protected int Reconnects;
    private protected Mock<ITunnelService> Tunnel = null!;
    private protected Mock<IWirelessAdbService> Wireless = null!;

    [SetUp]
    public void SetUp()
    {
        Reconnects = 0;
        AdbServer = new Mock<IAdbServerService>();
        Tunnel = new Mock<ITunnelService>();
        Wireless = new Mock<IWirelessAdbService>();
        Wireless
            .Setup(wireless => wireless.ReconnectSavedAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref Reconnects))
            .ReturnsAsync(0);
    }

    private protected async Task<WirelessReconnect> StartAsync()
    {
        var service = new WirelessReconnect(Wireless.Object, AdbServer.Object, Tunnel.Object, NullLogger<WirelessReconnect>.Instance);
        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(1);
        return service;
    }

    // The service reconnects on its own background loop; wait (bounded) for it to catch up.
    private protected async Task WaitForAsync(int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Volatile.Read(ref Reconnects) < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    public class When_Started : WirelessReconnectTests
    {
        [Test]
        public async Task It_reconnects_saved_devices_at_launch()
        {
            using var service = await StartAsync();

            Assert.That(Reconnects, Is.EqualTo(1));
            await service.StopAsync(CancellationToken.None);
        }

        [Test]
        public async Task It_reconnects_after_the_adb_server_restarts()
        {
            using var service = await StartAsync();

            AdbServer.Raise(server => server.ServerRestarted += null, AdbServer.Object, EventArgs.Empty);
            await WaitForAsync(2);

            Assert.That(Reconnects, Is.EqualTo(2));
            await service.StopAsync(CancellationToken.None);
        }

        [Test]
        public async Task It_reconnects_when_the_tunnel_comes_up_but_not_on_other_states()
        {
            using var service = await StartAsync();

            Tunnel.Raise(tunnel => tunnel.StatusChanged += null, Tunnel.Object, new TunnelStatus(TunnelState.Connecting, null));
            Tunnel.Raise(tunnel => tunnel.StatusChanged += null, Tunnel.Object, new TunnelStatus(TunnelState.Connected, null));
            await WaitForAsync(2);
            await Task.Delay(50);

            Assert.That(Reconnects, Is.EqualTo(2));
            await service.StopAsync(CancellationToken.None);
        }
    }
}
