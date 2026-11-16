using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Desktop.Devices.Wireless;
using Remote.Adb.Testing;

namespace Remote.Adb.Desktop.UnitTests.Devices.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class ConnectAddressViewModelTests
{
    private protected WirelessMock Wireless = null!;

    [SetUp]
    public void SetUp()
    {
        Wireless = new WirelessMock();
    }

    private protected ConnectAddressViewModel CreateViewModel() => new(Wireless.Object);

    public class When_ConnectCommand_Is_Executed : ConnectAddressViewModelTests
    {
        [Test]
        public async Task It_shows_the_launch_failure_when_adb_is_missing()
        {
            Wireless.ConnectThrows(new ProcessLaunchException("adb", new InvalidOperationException("not found")));
            var viewModel = CreateViewModel();
            viewModel.Address = "192.168.1.20:5555";

            await viewModel.ConnectCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.ErrorMessage, Is.Not.Null.And.Not.Empty);
                Assert.That(viewModel.IsBusy, Is.False);
            });
        }

        [Test]
        public async Task It_clears_a_previous_error_on_success()
        {
            Wireless.ConnectReturns(WirelessConnectOutcome.ConnectionFailed, "failed to connect");
            var viewModel = CreateViewModel();
            viewModel.Address = "192.168.1.20:5555";
            await viewModel.ConnectCommand.ExecuteAsync(null);
            Wireless.ConnectReturns(WirelessConnectOutcome.Connected, "connected");

            await viewModel.ConnectCommand.ExecuteAsync(null);

            Assert.That(viewModel.ErrorMessage, Is.Null);
        }
    }

    public class When_ConnectToAsync_Is_Called : ConnectAddressViewModelTests
    {
        [Test]
        public async Task It_ignores_a_pick_while_a_connect_is_running()
        {
            var gate = new TaskCompletionSource<WirelessConnectResult>();
            Wireless.Inner
                .Setup(wireless => wireless.ConnectAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(gate.Task);
            var viewModel = CreateViewModel();
            var first = viewModel.ConnectToAsync(new MdnsService("adb-a", MdnsServiceType.Legacy, "192.168.1.20", 5555));

            await viewModel.ConnectToAsync(new MdnsService("adb-b", MdnsServiceType.Connect, "192.168.1.30", 41235));
            var addressWhileBusy = viewModel.Address;
            var rememberWhileBusy = viewModel.Remember;
            gate.SetResult(new WirelessConnectResult(WirelessConnectOutcome.Connected, "connected"));
            await first;

            Wireless.Inner.Verify(wireless => wireless.ConnectAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Multiple(() =>
            {
                Assert.That(addressWhileBusy, Is.EqualTo("192.168.1.20:5555"));
                Assert.That(rememberWhileBusy, Is.True);
            });
        }
    }
}
