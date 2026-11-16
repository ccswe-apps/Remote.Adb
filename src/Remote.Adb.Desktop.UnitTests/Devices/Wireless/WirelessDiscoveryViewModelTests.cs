using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Desktop.Devices.Wireless;
using Remote.Adb.Desktop.UnitTests.Fakes;

namespace Remote.Adb.Desktop.UnitTests.Devices.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class WirelessDiscoveryViewModelTests
{
    private protected static readonly MdnsService ConnectService = new("adb-R58M-AbCd", MdnsServiceType.Connect, "192.168.1.20", 41235);
    private protected static readonly MdnsService LegacyService = new("adb-legacy", MdnsServiceType.Legacy, "192.168.1.30", 5555);
    private protected static readonly MdnsService PairingService = new("adb-R58M-AbCd", MdnsServiceType.Pairing, "192.168.1.20", 37123);

    private protected FakeTimerFactory TimerFactory = null!;
    private protected Mock<IWirelessAdbService> Wireless = null!;

    [SetUp]
    public void SetUp()
    {
        TimerFactory = new FakeTimerFactory();
        Wireless = new Mock<IWirelessAdbService>();
        Wireless.Setup(wireless => wireless.IsDiscoveryAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Wireless
            .Setup(wireless => wireless.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { PairingService, ConnectService, LegacyService });
    }

    private protected WirelessDiscoveryViewModel CreateViewModel() => new(Wireless.Object, TimerFactory);

    public class When_StartAsync_Is_Called : WirelessDiscoveryViewModelTests
    {
        [Test]
        public async Task It_splits_services_into_pairing_and_connect_lists()
        {
            var viewModel = CreateViewModel();

            await viewModel.StartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.PairingServices, Is.EqualTo(new[] { PairingService }));
                Assert.That(viewModel.ConnectServices, Is.EquivalentTo(new[] { ConnectService, LegacyService }));
                Assert.That(viewModel.IsUnavailable, Is.False);
                Assert.That(TimerFactory.Timer.IsRunning, Is.True);
            });
        }

        [Test]
        public async Task It_marks_discovery_unavailable_and_does_not_poll()
        {
            Wireless.Setup(wireless => wireless.IsDiscoveryAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var viewModel = CreateViewModel();

            await viewModel.StartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsUnavailable, Is.True);
                Assert.That(TimerFactory.Timer.IsRunning, Is.False);
            });
            Wireless.Verify(wireless => wireless.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_starts_only_once()
        {
            var viewModel = CreateViewModel();

            await viewModel.StartAsync();
            await viewModel.StartAsync();

            Wireless.Verify(wireless => wireless.IsDiscoveryAvailableAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public class When_The_Timer_Ticks : WirelessDiscoveryViewModelTests
    {
        [Test]
        public async Task It_drops_services_that_disappeared()
        {
            var viewModel = CreateViewModel();
            await viewModel.StartAsync();
            Wireless.Setup(wireless => wireless.DiscoverAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { ConnectService });

            TimerFactory.Timer.RaiseTick();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.PairingServices, Is.Empty);
                Assert.That(viewModel.ConnectServices, Is.EqualTo(new[] { ConnectService }));
            });
        }
    }

    public class When_Stop_Is_Called : WirelessDiscoveryViewModelTests
    {
        [Test]
        public async Task It_stops_polling()
        {
            var viewModel = CreateViewModel();
            await viewModel.StartAsync();

            viewModel.Stop();

            Assert.That(TimerFactory.Timer.IsRunning, Is.False);
        }
    }
}
