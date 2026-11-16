using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Emulators;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Common.Notifications;
using Remote.Adb.Desktop.Devices;
using Remote.Adb.Desktop.Devices.Wireless;
using Remote.Adb.Desktop.UnitTests.Fakes;

namespace Remote.Adb.Desktop.UnitTests.Devices;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class DevicesViewModelTests
{
    protected Mock<IDeviceService> DeviceService = null!;
    protected Mock<IEmulatorService> EmulatorService = null!;
    protected Mock<INotificationService> Notifications = null!;
    protected FakeTimerFactory TimerFactory = null!;
    protected Mock<IWirelessAdbService> Wireless = null!;
    protected Mock<IWirelessDeviceDialog> WirelessDialog = null!;

    [SetUp]
    public void SetUp()
    {
        EmulatorService = new Mock<IEmulatorService>();
        EmulatorService
            .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AndroidVirtualDevice>());

        DeviceService = new Mock<IDeviceService>();
        DeviceService
            .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AdbDevice>());

        Notifications = new Mock<INotificationService>();
        TimerFactory = new FakeTimerFactory();
        Wireless = new Mock<IWirelessAdbService>();
        WirelessDialog = new Mock<IWirelessDeviceDialog>();
    }

    protected static AdbDevice Physical(string serial, string? model = null, string state = "device") =>
        new(serial, state, model, null, null, null);

    protected static AndroidVirtualDevice Virtual(string name, bool running = false) =>
        new(name, name, null, running, running ? "emulator-5554" : null);

    protected DevicesViewModel CreateViewModel() => new(
        EmulatorService.Object,
        DeviceService.Object,
        Mock.Of<IAvdConfigStore>(),
        Mock.Of<IAvdCreateDialog>(),
        (_, _) => null!,
        Mock.Of<IAvdProvisioningService>(),
        Mock.Of<IConfirmDialog>(),
        WirelessDialog.Object,
        Wireless.Object,
        Notifications.Object,
        TimerFactory);

    public class When_Activated : DevicesViewModelTests
    {
        [Test]
        public async Task It_lists_both_sources_and_starts_the_refresh_timer()
        {
            var viewModel = CreateViewModel();

            await viewModel.OnActivatedAsync();

            EmulatorService.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
            DeviceService.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(TimerFactory.Timer.IsRunning, Is.True);
        }

        [Test]
        public void It_stops_the_timer_on_deactivate()
        {
            var viewModel = CreateViewModel();

            viewModel.OnDeactivated();

            Assert.That(TimerFactory.Timer.StopCount, Is.EqualTo(1));
        }
    }

    public class When_RefreshCommand_Is_Executed : DevicesViewModelTests
    {
        [Test]
        public async Task It_merges_virtual_and_physical_devices_into_one_list()
        {
            EmulatorService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Virtual("Mango") });
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("s1", "Zebra"), Physical("s2", "alpha") });
            var viewModel = CreateViewModel();

            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.That(viewModel.Devices.Select(row => row.DisplayName), Is.EqualTo(new[] { "alpha", "Mango", "Zebra" }));
        }

        [Test]
        public async Task It_excludes_running_emulators_from_the_physical_list()
        {
            EmulatorService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Virtual("Pixel", running: true) });
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("emulator-5554"), Physical("ABC123", "Phone") });
            var viewModel = CreateViewModel();

            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.That(viewModel.Devices.Select(row => row.IdentityKey), Does.Not.Contain("dev:emulator-5554"));
            Assert.That(viewModel.Devices.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task It_orders_virtual_rows_by_display_name_case_insensitively()
        {
            EmulatorService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Virtual("Zebra"), Virtual("alpha"), Virtual("Mango") });
            var viewModel = CreateViewModel();

            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.That(viewModel.Devices.Select(row => row.DisplayName), Is.EqualTo(new[] { "alpha", "Mango", "Zebra" }));
        }

        [Test]
        public async Task It_reconciles_virtual_rows_in_place_preserving_identity_and_starting_state()
        {
            EmulatorService
                .SetupSequence(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Virtual("A"), Virtual("B") })
                .ReturnsAsync(new[] { Virtual("B"), Virtual("C") });
            var viewModel = CreateViewModel();

            await viewModel.RefreshCommand.ExecuteAsync(null);
            var bRow = viewModel.Devices.Single(row => row.Name == "B");
            bRow.IsStarting = true;

            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.That(viewModel.Devices.Select(row => row.Name), Is.EqualTo(new[] { "B", "C" }));
            Assert.That(viewModel.Devices.Single(row => row.Name == "B"), Is.SameAs(bRow));
            Assert.That(bRow.IsStarting, Is.True);
        }

        [Test]
        public async Task It_updates_physical_rows_in_place_across_refreshes()
        {
            DeviceService
                .SetupSequence(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("s1", "Pixel", state: "offline") })
                .ReturnsAsync(new[] { Physical("s1", "Pixel", state: "device") });
            var viewModel = CreateViewModel();

            await viewModel.RefreshCommand.ExecuteAsync(null);
            var row = viewModel.Devices.Single();
            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.That(viewModel.Devices.Single(), Is.SameAs(row));
            Assert.That(row.IsOnline, Is.True);
        }

        [Test]
        public async Task It_does_not_drop_physical_rows_when_the_avd_set_changes()
        {
            EmulatorService
                .SetupSequence(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Virtual("A") })
                .ReturnsAsync(Array.Empty<AndroidVirtualDevice>());
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("s1", "Phone") });
            var viewModel = CreateViewModel();

            await viewModel.RefreshCommand.ExecuteAsync(null);
            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.That(viewModel.Devices.Select(row => row.IdentityKey), Is.EqualTo(new[] { "dev:s1" }));
        }

        [Test]
        public async Task It_shows_an_error_toast_when_the_list_fails()
        {
            EmulatorService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProcessLaunchException("emulator", new InvalidOperationException()));
            var viewModel = CreateViewModel();

            await viewModel.RefreshCommand.ExecuteAsync(null);

            Notifications.Verify(
                notifications => notifications.Show(
                    "Couldn't list devices",
                    It.IsAny<string>(),
                    NotificationSeverity.Error,
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<Action?>()),
                Times.Once);
        }
    }

    public class When_AddWirelessCommand_Is_Executed : DevicesViewModelTests
    {
        [Test]
        public async Task It_refreshes_when_a_device_was_connected()
        {
            WirelessDialog.Setup(dialog => dialog.ShowAsync()).ReturnsAsync(true);
            var viewModel = CreateViewModel();

            await viewModel.AddWirelessCommand.ExecuteAsync(null);

            DeviceService.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task It_does_not_refresh_when_cancelled()
        {
            WirelessDialog.Setup(dialog => dialog.ShowAsync()).ReturnsAsync(false);
            var viewModel = CreateViewModel();

            await viewModel.AddWirelessCommand.ExecuteAsync(null);

            DeviceService.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    public class When_DisconnectCommand_Is_Executed : DevicesViewModelTests
    {
        [Test]
        public async Task It_disconnects_a_wireless_row_by_serial()
        {
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("192.168.1.20:5555", "Pixel") });
            Wireless
                .Setup(wireless => wireless.DisconnectAsync("192.168.1.20:5555", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessResult(true, "disconnected 192.168.1.20:5555"));
            var viewModel = CreateViewModel();
            await viewModel.OnActivatedAsync();

            await viewModel.DisconnectCommand.ExecuteAsync(viewModel.Devices.Single());

            Wireless.Verify(wireless => wireless.DisconnectAsync("192.168.1.20:5555", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task It_ignores_a_usb_row()
        {
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("R58M123ABC", "Galaxy") });
            var viewModel = CreateViewModel();
            await viewModel.OnActivatedAsync();

            await viewModel.DisconnectCommand.ExecuteAsync(viewModel.Devices.Single());

            Wireless.Verify(wireless => wireless.DisconnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_shows_an_error_when_adb_refuses()
        {
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("192.168.1.20:5555", "Pixel") });
            Wireless
                .Setup(wireless => wireless.DisconnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessResult(false, "error: no such device"));
            var viewModel = CreateViewModel();
            await viewModel.OnActivatedAsync();

            await viewModel.DisconnectCommand.ExecuteAsync(viewModel.Devices.Single());

            Notifications.Verify(
                notifications => notifications.Show("Couldn't disconnect", "error: no such device", NotificationSeverity.Error, It.IsAny<TimeSpan?>(), It.IsAny<Action?>()),
                Times.Once);
        }
    }

    public class When_Rows_Are_Built : DevicesViewModelTests
    {
        [Test]
        public async Task It_offers_an_overflow_for_virtual_wireless_and_online_usb_rows_only()
        {
            EmulatorService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Virtual("Pixel_8") });
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[]
                {
                    Physical("192.168.1.20:5555", "Wireless"),
                    Physical("R58M123ABC", "Usb"),
                    Physical("R58M999XYZ", "Unauthorized", state: "unauthorized"),
                });
            var viewModel = CreateViewModel();

            await viewModel.OnActivatedAsync();

            var overflow = viewModel.Devices.ToDictionary(row => row.DisplayName, row => row.HasOverflow);
            Assert.That(overflow, Is.EquivalentTo(new Dictionary<string, bool>
            {
                ["Pixel_8"] = true,
                ["Wireless"] = true,
                ["Usb"] = true,
                ["Unauthorized"] = false,
            }));
        }
    }

    public class When_EnableWirelessCommand_Is_Executed : DevicesViewModelTests
    {
        protected async Task<DevicesViewModel> CreateWithUsbDeviceAsync()
        {
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("R58M123ABC", "Galaxy") });
            var viewModel = CreateViewModel();
            await viewModel.OnActivatedAsync();
            return viewModel;
        }

        [Test]
        public async Task It_switches_the_device_and_confirms_it_can_be_unplugged()
        {
            Wireless
                .Setup(wireless => wireless.EnableWirelessAsync("R58M123ABC", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessEnableResult(WirelessEnableOutcome.Connected, "192.168.1.20:5555", "connected to 192.168.1.20:5555"));
            var viewModel = await CreateWithUsbDeviceAsync();
            var row = viewModel.Devices.Single();

            await viewModel.EnableWirelessCommand.ExecuteAsync(row);

            Notifications.Verify(
                notifications => notifications.Show("Wi-Fi debugging on", It.Is<string>(message => message.Contains("192.168.1.20:5555")), NotificationSeverity.Success, It.IsAny<TimeSpan?>(), It.IsAny<Action?>()),
                Times.Once);
            Assert.That(row.IsBusy, Is.False);
        }

        [Test]
        public async Task It_warns_when_the_device_switched_but_could_not_be_reached()
        {
            Wireless
                .Setup(wireless => wireless.EnableWirelessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessEnableResult(WirelessEnableOutcome.ConnectFailed, "192.168.1.20:5555", "failed to connect"));
            var viewModel = await CreateWithUsbDeviceAsync();

            await viewModel.EnableWirelessCommand.ExecuteAsync(viewModel.Devices.Single());

            Notifications.Verify(
                notifications => notifications.Show(It.IsAny<string>(), It.IsAny<string>(), NotificationSeverity.Warning, It.IsAny<TimeSpan?>(), It.IsAny<Action?>()),
                Times.Once);
        }

        [Test]
        public async Task It_shows_an_error_when_the_device_is_not_on_wifi()
        {
            Wireless
                .Setup(wireless => wireless.EnableWirelessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessEnableResult(WirelessEnableOutcome.NoWifiAddress, null, "The device has no Wi-Fi address."));
            var viewModel = await CreateWithUsbDeviceAsync();

            await viewModel.EnableWirelessCommand.ExecuteAsync(viewModel.Devices.Single());

            Notifications.Verify(
                notifications => notifications.Show("Couldn't enable Wi-Fi debugging", "The device has no Wi-Fi address.", NotificationSeverity.Error, It.IsAny<TimeSpan?>(), It.IsAny<Action?>()),
                Times.Once);
        }

        [Test]
        public async Task It_ignores_a_wireless_row()
        {
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("192.168.1.20:5555", "Pixel") });
            var viewModel = CreateViewModel();
            await viewModel.OnActivatedAsync();

            await viewModel.EnableWirelessCommand.ExecuteAsync(viewModel.Devices.Single());

            Wireless.Verify(wireless => wireless.EnableWirelessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    public class When_IsListEmpty_Is_Read : DevicesViewModelTests
    {
        [Test]
        public void It_is_false_before_the_first_load()
        {
            Assert.That(CreateViewModel().IsListEmpty, Is.False);
        }

        [Test]
        public async Task It_is_true_after_a_successful_empty_load()
        {
            var viewModel = CreateViewModel();

            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.That(viewModel.IsListEmpty, Is.True);
        }
    }

    public class When_The_Refresh_Timer_Ticks : DevicesViewModelTests
    {
        [Test]
        public void It_refreshes()
        {
            var viewModel = CreateViewModel();

            TimerFactory.Timer.RaiseTick();

            DeviceService.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task It_skips_while_a_virtual_row_is_starting()
        {
            EmulatorService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Virtual("A") });
            var viewModel = CreateViewModel();
            await viewModel.RefreshCommand.ExecuteAsync(null);
            viewModel.Devices.Single().IsStarting = true;

            TimerFactory.Timer.RaiseTick();

            EmulatorService.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void It_does_not_toast_on_a_silent_background_failure()
        {
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProcessLaunchException("adb", new InvalidOperationException()));
            var viewModel = CreateViewModel();

            TimerFactory.Timer.RaiseTick();

            Notifications.Verify(
                notifications => notifications.Show(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<NotificationSeverity>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<Action?>()),
                Times.Never);
        }
    }
}
