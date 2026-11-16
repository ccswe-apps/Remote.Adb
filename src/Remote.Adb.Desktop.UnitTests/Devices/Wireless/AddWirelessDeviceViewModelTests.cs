using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Desktop.Devices.Wireless;

namespace Remote.Adb.Desktop.UnitTests.Devices.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class AddWirelessDeviceViewModelTests
{
    protected bool? Closed;
    protected Mock<IWirelessAdbService> Wireless = null!;

    [SetUp]
    public void SetUp()
    {
        Closed = null;
        Wireless = new Mock<IWirelessAdbService>();
    }

    protected AddWirelessDeviceViewModel CreateViewModel()
    {
        var viewModel = new AddWirelessDeviceViewModel(Wireless.Object);
        viewModel.CloseRequested += result => Closed = result;
        return viewModel;
    }

    public class When_Pairing : AddWirelessDeviceViewModelTests
    {
        [Test]
        public void It_cannot_pair_until_both_fields_are_filled()
        {
            var viewModel = CreateViewModel();

            viewModel.Pair.PairingAddress = "192.168.1.20:37123";

            Assert.That(viewModel.Pair.PairCommand.CanExecute(null), Is.False);
            viewModel.Pair.Code = "123456";
            Assert.That(viewModel.Pair.PairCommand.CanExecute(null), Is.True);
        }

        [Test]
        public async Task It_closes_with_success_when_the_device_connected()
        {
            Wireless
                .Setup(wireless => wireless.PairAsync("192.168.1.20:37123", "123456", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessPairResult(true, true, "Paired and connected.", "adb-x"));
            var viewModel = CreateViewModel();
            viewModel.Pair.PairingAddress = "192.168.1.20:37123";
            viewModel.Pair.Code = "123456";

            await viewModel.Pair.PairCommand.ExecuteAsync(null);

            Assert.That(Closed, Is.True);
        }

        [Test]
        public async Task It_moves_to_connect_with_the_host_prefilled_when_paired_but_not_connected()
        {
            Wireless
                .Setup(wireless => wireless.PairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessPairResult(true, false, "Paired, but…", "adb-x"));
            var viewModel = CreateViewModel();
            viewModel.Pair.PairingAddress = "192.168.1.20:37123";
            viewModel.Pair.Code = "123456";

            await viewModel.Pair.PairCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(Closed, Is.Null);
                Assert.That(viewModel.SelectedTab, Is.EqualTo(AddWirelessDeviceViewModel.ConnectTab));
                Assert.That(viewModel.Connect.Address, Is.EqualTo("192.168.1.20:"));
                Assert.That(viewModel.PairedNotice, Is.Not.Null);
            });
        }

        [Test]
        public async Task It_shows_the_error_and_stays_open_when_pairing_fails()
        {
            Wireless
                .Setup(wireless => wireless.PairAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessPairResult(false, false, "error: protocol fault"));
            var viewModel = CreateViewModel();
            viewModel.Pair.PairingAddress = "192.168.1.20:37123";
            viewModel.Pair.Code = "000000";

            await viewModel.Pair.PairCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(Closed, Is.Null);
                Assert.That(viewModel.Pair.ErrorMessage, Does.StartWith("error: protocol fault"));
                Assert.That(viewModel.Pair.IsBusy, Is.False);
            });
        }
    }

    public class When_Connecting : AddWirelessDeviceViewModelTests
    {
        [Test]
        public async Task It_closes_with_success_once_connected()
        {
            Wireless
                .Setup(wireless => wireless.ConnectAsync("192.168.1.20:5555", true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessConnectResult(WirelessConnectOutcome.Connected, "connected to 192.168.1.20:5555"));
            var viewModel = CreateViewModel();
            viewModel.Connect.Address = "192.168.1.20:5555";

            await viewModel.Connect.ConnectCommand.ExecuteAsync(null);

            Assert.That(Closed, Is.True);
        }

        [Test]
        public async Task It_explains_an_authentication_failure()
        {
            Wireless
                .Setup(wireless => wireless.ConnectAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessConnectResult(WirelessConnectOutcome.AuthenticationFailed, "failed to authenticate to 192.168.1.20:5555"));
            var viewModel = CreateViewModel();
            viewModel.Connect.Address = "192.168.1.20:5555";

            await viewModel.Connect.ConnectCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(Closed, Is.Null);
                Assert.That(viewModel.Connect.ErrorMessage, Does.Contain("Allow debugging"));
            });
        }
    }

    public class When_CancelCommand_Is_Executed : AddWirelessDeviceViewModelTests
    {
        [Test]
        public void It_closes_without_success()
        {
            var viewModel = CreateViewModel();

            viewModel.CancelCommand.Execute(null);

            Assert.That(Closed, Is.False);
        }
    }
}
