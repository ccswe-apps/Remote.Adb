using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Desktop.Devices.Wireless;
using Remote.Adb.Desktop.UnitTests.Fakes;

namespace Remote.Adb.Desktop.UnitTests.Devices.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class QrPairingViewModelTests
{
    private protected PairingCodeViewModel CodeForm = null!;
    private protected WirelessDiscoveryViewModel Discovery = null!;
    private protected List<WirelessPairResult> Paired = null!;
    private protected FakeTimerFactory TimerFactory = null!;
    private protected Mock<IWirelessAdbService> Wireless = null!;

    [SetUp]
    public void SetUp()
    {
        TimerFactory = new FakeTimerFactory();
        Wireless = new Mock<IWirelessAdbService>();
        Wireless.Setup(wireless => wireless.IsDiscoveryAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Wireless.Setup(wireless => wireless.DiscoverAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<MdnsService>());
        Discovery = new WirelessDiscoveryViewModel(Wireless.Object, TimerFactory);
        CodeForm = new PairingCodeViewModel(Wireless.Object);
        Paired = [];
    }

    // The regenerate timer is the second one created (discovery's poller is the first).
    private protected FakeDispatcherTimer RegenerateTimer => TimerFactory.Timers[1];

    private protected QrPairingViewModel CreateViewModel()
    {
        var viewModel = new QrPairingViewModel(Wireless.Object, Discovery, CodeForm, TimerFactory);
        viewModel.Paired += Paired.Add;
        return viewModel;
    }

    // The device scanned the code: discovery now reports a pairing service named after it.
    private protected async Task DeviceScansAsync(QrPairingViewModel viewModel)
    {
        Wireless
            .Setup(wireless => wireless.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new MdnsService(viewModel.Payload.ServiceName, MdnsServiceType.Pairing, "192.168.1.20", 37123) });
        await Discovery.StartAsync();
    }

    public class When_The_Device_Scans_The_Code : QrPairingViewModelTests
    {
        [Test]
        public async Task It_pairs_with_the_scanned_password()
        {
            Wireless
                .Setup(wireless => wireless.PairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessPairResult(true, true, "Paired and connected.", "adb-x"));
            var viewModel = CreateViewModel();
            viewModel.Start();
            var password = viewModel.Payload.Password;

            await DeviceScansAsync(viewModel);

            Wireless.Verify(wireless => wireless.PairAsync("192.168.1.20:37123", password, It.IsAny<CancellationToken>()), Times.Once);
            Assert.Multiple(() =>
            {
                Assert.That(Paired, Has.Count.EqualTo(1));
                Assert.That(CodeForm.PairingAddress, Is.EqualTo("192.168.1.20:37123"));
            });
        }

        [Test]
        public async Task It_ignores_other_devices_waiting_to_pair()
        {
            Wireless
                .Setup(wireless => wireless.DiscoverAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { new MdnsService("someone-else", MdnsServiceType.Pairing, "192.168.1.30", 37000) });
            var viewModel = CreateViewModel();
            viewModel.Start();

            await Discovery.StartAsync();

            Wireless.Verify(wireless => wireless.PairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_waits_while_the_code_form_is_pairing()
        {
            var viewModel = CreateViewModel();
            viewModel.Start();
            CodeForm.IsBusy = true;

            await DeviceScansAsync(viewModel);

            Wireless.Verify(wireless => wireless.PairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_shows_the_error_and_a_new_code_when_pairing_fails()
        {
            Wireless
                .Setup(wireless => wireless.PairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessPairResult(false, false, "error: protocol fault"));
            var viewModel = CreateViewModel();
            viewModel.Start();
            var original = viewModel.Payload;

            await DeviceScansAsync(viewModel);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.ErrorMessage, Does.StartWith("error: protocol fault"));
                Assert.That(viewModel.Payload, Is.Not.EqualTo(original));
                Assert.That(viewModel.IsPairing, Is.False);
                Assert.That(Paired, Is.Empty);
            });
        }

        [Test]
        public async Task It_does_nothing_once_stopped()
        {
            var viewModel = CreateViewModel();
            viewModel.Start();
            viewModel.Stop();

            await DeviceScansAsync(viewModel);

            Wireless.Verify(wireless => wireless.PairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    public class When_The_Regenerate_Timer_Ticks : QrPairingViewModelTests
    {
        [Test]
        public void It_shows_a_new_code()
        {
            var viewModel = CreateViewModel();
            viewModel.Start();
            var original = viewModel.QrText;

            RegenerateTimer.RaiseTick();

            Assert.That(viewModel.QrText, Is.Not.EqualTo(original));
        }

        [Test]
        public void It_runs_only_while_started()
        {
            var viewModel = CreateViewModel();

            viewModel.Start();
            var running = RegenerateTimer.IsRunning;
            viewModel.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(running, Is.True);
                Assert.That(RegenerateTimer.IsRunning, Is.False);
            });
        }
    }
}
