using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Time.Testing;
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
using Remote.Adb.Testing;

namespace Remote.Adb.Desktop.UnitTests.Devices;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class DevicesViewModelTests
{
    private protected FakeTimeProvider Clock = null!;
    private protected Mock<IAvdConfigStore> ConfigStore = null!;
    private protected Mock<IConfirmDialog> ConfirmDialog = null!;
    private protected Mock<IDeviceService> DeviceService = null!;
    private protected Mock<IEmulatorService> EmulatorService = null!;
    private protected Mock<INotificationService> Notifications = null!;
    private protected Mock<IAvdProvisioningService> Provisioning = null!;
    private protected FakeTimerFactory TimerFactory = null!;
    private protected WirelessMock Wireless = null!;
    private protected Mock<IWirelessDeviceDialog> WirelessDialog = null!;

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

        Clock = new FakeTimeProvider();
        ConfigStore = new Mock<IAvdConfigStore>();
        ConfirmDialog = new Mock<IConfirmDialog>();
        Notifications = new Mock<INotificationService>();
        Provisioning = new Mock<IAvdProvisioningService>();
        TimerFactory = new FakeTimerFactory();
        Wireless = new WirelessMock();
        WirelessDialog = new Mock<IWirelessDeviceDialog>();
    }

    private protected static AdbDevice Physical(string serial, string? model = null, string state = "device") =>
        new(serial, state, model, null, null, null);

    private protected static AndroidVirtualDevice Virtual(string name, bool running = false) =>
        new(name, name, null, running, running ? "emulator-5554" : null);

    private protected DevicesViewModel CreateViewModel() => new(
        EmulatorService.Object,
        DeviceService.Object,
        ConfigStore.Object,
        Mock.Of<IAvdCreateDialog>(),
        (configuration, back) => new AvdDetailsViewModel(configuration, ConfigStore.Object, back),
        Provisioning.Object,
        ConfirmDialog.Object,
        WirelessDialog.Object,
        Wireless.Object,
        Notifications.Object,
        TimerFactory,
        Clock,
        new LoggerFake<DevicesViewModel>());

    // Lists the given AVDs on every call from now on.
    private protected void ListVirtual(params AndroidVirtualDevice[] devices) =>
        EmulatorService
            .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(devices);

    // A view model already showing the given AVDs.
    private protected async Task<DevicesViewModel> CreateLoadedAsync(params AndroidVirtualDevice[] devices)
    {
        ListVirtual(devices);
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }

    private protected DeviceRowViewModel Row(DevicesViewModel viewModel, string name) =>
        viewModel.Devices.Single(row => row.Name == name);

    private protected void VerifyToast(string title, NotificationSeverity severity) =>
        Notifications.Verify(
            notifications => notifications.Show(title, It.IsAny<string>(), severity, It.IsAny<TimeSpan?>(), It.IsAny<Action?>()),
            Times.Once);

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
            Wireless.Inner
                .Setup(wireless => wireless.DisconnectAsync("192.168.1.20:5555", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WirelessResult(true, "disconnected 192.168.1.20:5555"));
            var viewModel = CreateViewModel();
            await viewModel.OnActivatedAsync();

            await viewModel.DisconnectCommand.ExecuteAsync(viewModel.Devices.Single());

            Wireless.Inner.Verify(wireless => wireless.DisconnectAsync("192.168.1.20:5555", It.IsAny<CancellationToken>()), Times.Once);
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

            Wireless.Inner.Verify(wireless => wireless.DisconnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_shows_an_error_when_adb_refuses()
        {
            DeviceService
                .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { Physical("192.168.1.20:5555", "Pixel") });
            Wireless.Inner
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
            Wireless.Inner
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
            Wireless.Inner
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
            Wireless.Inner
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

            Wireless.Inner.Verify(wireless => wireless.EnableWirelessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

    public class When_StartCommand_Is_Executed : DevicesViewModelTests
    {
        [Test]
        public async Task It_launches_the_avd_and_polls_until_it_is_running()
        {
            var viewModel = await CreateLoadedAsync(Virtual("A"));
            var row = Row(viewModel, "A");

            var start = viewModel.StartCommand.ExecuteAsync(row);
            var startingWhileLaunching = row.IsStarting;
            ListVirtual(Virtual("A", running: true));
            Clock.Advance(viewModel.PollInterval);
            await start;

            EmulatorService.Verify(service => service.StartAsync("A", false, It.IsAny<CancellationToken>()), Times.Once);
            Assert.Multiple(() =>
            {
                Assert.That(startingWhileLaunching, Is.True);
                Assert.That(row.IsStarting, Is.False);
                Assert.That(row.IsRunning, Is.True);
            });
        }

        [Test]
        public async Task It_warns_when_the_avd_never_comes_up()
        {
            var viewModel = await CreateLoadedAsync(Virtual("A"));
            var row = Row(viewModel, "A");

            var start = viewModel.StartCommand.ExecuteAsync(row);
            Clock.Advance(viewModel.StartTimeout);
            await start;

            VerifyToast("Emulator didn't come up", NotificationSeverity.Warning);
            Assert.That(row.IsStarting, Is.False);
        }

        [Test]
        public async Task It_shows_an_error_when_the_emulator_cannot_be_launched()
        {
            EmulatorService
                .Setup(service => service.StartAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProcessLaunchException("emulator", new InvalidOperationException()));
            var viewModel = await CreateLoadedAsync(Virtual("A"));
            var row = Row(viewModel, "A");

            await viewModel.StartCommand.ExecuteAsync(row);

            VerifyToast("Couldn't start emulator", NotificationSeverity.Error);
            Assert.That(row.IsStarting, Is.False);
        }

        [Test]
        public async Task It_lets_another_avd_start_while_one_is_launching()
        {
            var viewModel = await CreateLoadedAsync(Virtual("A"), Virtual("B"));

            var startA = viewModel.StartCommand.ExecuteAsync(Row(viewModel, "A"));
            var canStartB = viewModel.StartCommand.CanExecute(Row(viewModel, "B"));
            var startB = viewModel.StartCommand.ExecuteAsync(Row(viewModel, "B"));
            ListVirtual(Virtual("A", running: true), Virtual("B", running: true));
            Clock.Advance(viewModel.PollInterval);
            await Task.WhenAll(startA, startB);

            Assert.That(canStartB, Is.True);
            EmulatorService.Verify(service => service.StartAsync("A", false, It.IsAny<CancellationToken>()), Times.Once);
            EmulatorService.Verify(service => service.StartAsync("B", false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task It_ignores_a_row_that_is_already_starting()
        {
            var viewModel = await CreateLoadedAsync(Virtual("A"));
            var row = Row(viewModel, "A");

            var first = viewModel.StartCommand.ExecuteAsync(row);
            await viewModel.StartCommand.ExecuteAsync(row);
            Clock.Advance(viewModel.StartTimeout);
            await first;

            EmulatorService.Verify(service => service.StartAsync("A", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public class When_ColdBootCommand_Is_Executed : DevicesViewModelTests
    {
        [Test]
        public async Task It_launches_the_avd_with_a_cold_boot()
        {
            var viewModel = await CreateLoadedAsync(Virtual("A"));

            var start = viewModel.ColdBootCommand.ExecuteAsync(Row(viewModel, "A"));
            ListVirtual(Virtual("A", running: true));
            Clock.Advance(viewModel.PollInterval);
            await start;

            EmulatorService.Verify(service => service.StartAsync("A", true, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public class When_StopCommand_Is_Executed : DevicesViewModelTests
    {
        [Test]
        public async Task It_stops_the_emulator_by_serial_and_refreshes()
        {
            EmulatorService
                .Setup(service => service.StopAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AvdOperationResult(true, null));
            var viewModel = await CreateLoadedAsync(Virtual("A", running: true));

            await viewModel.StopCommand.ExecuteAsync(Row(viewModel, "A"));

            EmulatorService.Verify(service => service.StopAsync("emulator-5554", It.IsAny<CancellationToken>()), Times.Once);
            EmulatorService.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public async Task It_shows_adbs_error_when_the_stop_fails()
        {
            EmulatorService
                .Setup(service => service.StopAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AvdOperationResult(false, "error: device offline"));
            var viewModel = await CreateLoadedAsync(Virtual("A", running: true));

            await viewModel.StopCommand.ExecuteAsync(Row(viewModel, "A"));

            Notifications.Verify(
                notifications => notifications.Show("Couldn't stop emulator", "error: device offline", NotificationSeverity.Error, It.IsAny<TimeSpan?>(), It.IsAny<Action?>()),
                Times.Once);
        }

        [Test]
        public async Task It_shows_an_error_when_the_stop_cannot_be_launched()
        {
            EmulatorService
                .Setup(service => service.StopAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProcessLaunchException("adb", new InvalidOperationException()));
            var viewModel = await CreateLoadedAsync(Virtual("A", running: true));
            var row = Row(viewModel, "A");

            await viewModel.StopCommand.ExecuteAsync(row);

            VerifyToast("Couldn't stop emulator", NotificationSeverity.Error);
            Assert.That(row.IsBusy, Is.False);
        }
    }

    public class When_DeleteCommand_Is_Executed : DevicesViewModelTests
    {
        [Test]
        public async Task It_does_nothing_when_the_user_cancels()
        {
            ConfirmDialog.Setup(dialog => dialog.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            var viewModel = await CreateLoadedAsync(Virtual("A"));

            await viewModel.DeleteCommand.ExecuteAsync(Row(viewModel, "A"));

            Provisioning.Verify(provisioning => provisioning.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_deletes_the_avd_and_refreshes_once_confirmed()
        {
            ConfirmDialog.Setup(dialog => dialog.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            Provisioning.Setup(provisioning => provisioning.DeleteAsync("A", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var viewModel = await CreateLoadedAsync(Virtual("A"));
            ListVirtual();

            await viewModel.DeleteCommand.ExecuteAsync(Row(viewModel, "A"));

            Assert.That(viewModel.Devices, Is.Empty);
        }

        [Test]
        public async Task It_shows_an_error_when_the_delete_fails()
        {
            ConfirmDialog.Setup(dialog => dialog.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            Provisioning.Setup(provisioning => provisioning.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var viewModel = await CreateLoadedAsync(Virtual("A"));

            await viewModel.DeleteCommand.ExecuteAsync(Row(viewModel, "A"));

            VerifyToast("Delete failed", NotificationSeverity.Error);
        }

        [Test]
        public async Task It_refuses_to_delete_a_running_avd()
        {
            var viewModel = await CreateLoadedAsync(Virtual("A", running: true));

            await viewModel.DeleteCommand.ExecuteAsync(Row(viewModel, "A"));

            ConfirmDialog.Verify(dialog => dialog.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }

    public class When_ViewDetailsCommand_Is_Executed : DevicesViewModelTests
    {
        [Test]
        public async Task It_opens_the_details_pane_and_back_closes_it()
        {
            ConfigStore.Setup(store => store.Read("A")).Returns(new AvdConfiguration("A", IniParser.Parse("abi.type=x86_64\n"), null));
            var viewModel = await CreateLoadedAsync(Virtual("A"));

            viewModel.ViewDetailsCommand.Execute(Row(viewModel, "A"));
            var opened = viewModel.SelectedDetail;
            opened?.BackCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(opened, Is.Not.Null);
                Assert.That(viewModel.SelectedDetail, Is.Null);
            });
        }

        [Test]
        public async Task It_shows_an_error_when_the_avd_has_no_configuration()
        {
            var viewModel = await CreateLoadedAsync(Virtual("A"));

            viewModel.ViewDetailsCommand.Execute(Row(viewModel, "A"));

            VerifyToast("Details unavailable", NotificationSeverity.Error);
            Assert.That(viewModel.SelectedDetail, Is.Null);
        }
    }

    public class When_A_Manual_Refresh_Overlaps_A_Tick : DevicesViewModelTests
    {
        [Test]
        public async Task It_runs_one_follow_up_pass_instead_of_dropping_it()
        {
            var gate = new TaskCompletionSource<IReadOnlyList<AdbDevice>>();
            DeviceService
                .SetupSequence(service => service.ListAsync(It.IsAny<CancellationToken>()))
                .Returns(gate.Task)
                .ReturnsAsync(new[] { Physical("s1", "Phone") });
            var viewModel = CreateViewModel();

            TimerFactory.Timer.RaiseTick();
            var refresh = viewModel.RefreshCommand.ExecuteAsync(null);
            gate.SetResult([]);
            await refresh;

            Assert.That(viewModel.Devices.Select(row => row.DisplayName), Is.EqualTo(new[] { "Phone" }));
        }
    }
}
