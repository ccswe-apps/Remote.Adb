using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Diagnostics;
using Remote.Adb.Core.Emulators;
using Remote.Adb.Core.Logging;
using Remote.Adb.Core.Settings;
using Remote.Adb.Core.Tunnel;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Common.Notifications;
using Remote.Adb.Desktop.Common.Threading;
using Remote.Adb.Desktop.Devices;
using Remote.Adb.Desktop.Devices.Wireless;
using Remote.Adb.Desktop.Logging;
using Remote.Adb.Desktop.Settings;
using Remote.Adb.Desktop.Shell;
using Remote.Adb.Desktop.Theming;
using Remote.Adb.Desktop.Tunnel;
using Remote.Adb.Desktop.UnitTests.Fakes;

namespace Remote.Adb.Desktop.UnitTests.Shell;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class MainWindowViewModelTests
{
    protected Mock<IDeviceService> DeviceService = null!;
    protected Mock<ISdkDiagnostics> Diagnostics = null!;
    protected Mock<IEmulatorService> EmulatorService = null!;
    protected Mock<INotificationService> Notifications = null!;
    protected Mock<ISettingsService> Settings = null!;
    protected StatusBarViewModel StatusBar = null!;

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

        Diagnostics = new Mock<ISdkDiagnostics>();
        Diagnostics.Setup(diagnostics => diagnostics.Evaluate()).Returns(Array.Empty<ToolDiagnostic>());

        Notifications = new Mock<INotificationService>();

        Settings = new Mock<ISettingsService>();
        Settings.SetupProperty(settings => settings.LogPanelOpen, false);
        Settings.SetupProperty(settings => settings.LogPanelHeight, 220d);
    }

    [TearDown]
    public void TearDown()
    {
        StatusBar?.Dispose();
    }

    protected MainWindowViewModel CreateViewModel()
    {
        var devices = new DevicesViewModel(
            EmulatorService.Object,
            DeviceService.Object,
            Mock.Of<IAvdConfigStore>(),
            Mock.Of<IAvdCreateDialog>(),
            (_, _) => null!,
            Mock.Of<IAvdProvisioningService>(),
            Mock.Of<IConfirmDialog>(),
            Mock.Of<IWirelessDeviceDialog>(),
            Mock.Of<IWirelessAdbService>(),
            Notifications.Object,
            new FakeTimerFactory());

        var settings = new SettingsViewModel(
            Mock.Of<ISettingsService>(),
            Mock.Of<IAndroidSdk>(),
            Mock.Of<IThemeApplier>(),
            Mock.Of<IDensityApplier>());

        var tunnel = new TunnelViewModel(
            Mock.Of<ITunnelService>(),
            Mock.Of<IAdbServerService>(),
            Mock.Of<ISettingsService>(),
            Mock.Of<IUiDispatcher>(),
            Notifications.Object);

        var logStore = new LogStore();
        var logPanel = new LogPanelViewModel(logStore, new FakeUiDispatcher(), Mock.Of<IClipboardService>());
        var tunnelService = new Mock<ITunnelService>();
        tunnelService.Setup(service => service.Status).Returns(new TunnelStatus(TunnelState.Disconnected, null));
        StatusBar = new StatusBarViewModel(logStore, tunnelService.Object, Settings.Object, new FakeUiDispatcher());

        return new MainWindowViewModel(
            devices,
            tunnel,
            settings,
            logPanel,
            StatusBar,
            Settings.Object,
            Diagnostics.Object,
            Notifications.Object);
    }

    protected void VerifyListCount(Times times) =>
        DeviceService.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), times);

    public class When_The_Log_Panel_Is_Toggled : MainWindowViewModelTests
    {
        [Test]
        public void It_restores_the_persisted_state()
        {
            Settings.Object.LogPanelOpen = true;
            Settings.Object.LogPanelHeight = 300;

            var viewModel = CreateViewModel();

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsLogPanelOpen, Is.True);
                Assert.That(viewModel.LogPanelRowHeight, Is.EqualTo(300));
            });
        }

        [Test]
        public void It_opens_and_persists_the_panel()
        {
            var viewModel = CreateViewModel();

            viewModel.ToggleLogPanelCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsLogPanelOpen, Is.True);
                Assert.That(viewModel.LogPanelRowHeight, Is.EqualTo(220));
                Assert.That(Settings.Object.LogPanelOpen, Is.True);
                Assert.That(StatusBar.IsLogVisible, Is.True);
            });
        }

        [Test]
        public void It_collapses_the_row_when_closed()
        {
            Settings.Object.LogPanelOpen = true;
            var viewModel = CreateViewModel();

            viewModel.ToggleLogPanelCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.LogPanelRowHeight, Is.Zero);
                Assert.That(Settings.Object.LogPanelOpen, Is.False);
                Assert.That(StatusBar.IsLogVisible, Is.False);
            });
        }

        [Test]
        public void It_persists_the_height_only_when_committed()
        {
            Settings.Object.LogPanelOpen = true;
            var viewModel = CreateViewModel();

            viewModel.LogPanelRowHeight = 340;
            var beforeCommit = Settings.Object.LogPanelHeight;
            viewModel.CommitLogPanelHeight();

            Assert.Multiple(() =>
            {
                Assert.That(beforeCommit, Is.EqualTo(220));
                Assert.That(Settings.Object.LogPanelHeight, Is.EqualTo(340));
            });
        }

        [Test]
        public void It_clamps_the_height_to_the_minimum()
        {
            Settings.Object.LogPanelOpen = true;
            var viewModel = CreateViewModel();

            viewModel.LogPanelRowHeight = 10;

            Assert.That(viewModel.LogPanelRowHeight, Is.EqualTo(MainWindowViewModel.MinLogPanelHeight));
        }
    }

    public class When_RaiseStartupDiagnostics_Is_Called : MainWindowViewModelTests
    {
        [Test]
        public void It_shows_one_persistent_toast_per_diagnostic_with_the_mapped_severity()
        {
            Diagnostics
                .Setup(diagnostics => diagnostics.Evaluate())
                .Returns(new[]
                {
                    new ToolDiagnostic("Android SDK", "missing", DiagnosticSeverity.Error),
                    new ToolDiagnostic("JDK", "guessed", DiagnosticSeverity.Warning),
                });
            var viewModel = CreateViewModel();

            viewModel.RaiseStartupDiagnostics();

            Notifications.Verify(
                notifications => notifications.Show("Android SDK", "missing", NotificationSeverity.Error, TimeSpan.Zero, It.IsAny<Action?>()),
                Times.Once);
            Notifications.Verify(
                notifications => notifications.Show("JDK", "guessed", NotificationSeverity.Warning, TimeSpan.Zero, It.IsAny<Action?>()),
                Times.Once);
        }
    }

    public class When_The_Live_Screen_Changes : MainWindowViewModelTests
    {
        [Test]
        public void It_activates_the_devices_screen_on_construction()
        {
            CreateViewModel();

            VerifyListCount(Times.Once());
        }

        [Test]
        public void It_reactivates_the_devices_screen_when_window_focus_returns()
        {
            var viewModel = CreateViewModel();

            viewModel.SetWindowActive(false);
            viewModel.SetWindowActive(true);

            VerifyListCount(Times.Exactly(2));
        }

        [Test]
        public void It_ignores_a_redundant_window_active()
        {
            var viewModel = CreateViewModel();

            viewModel.SetWindowActive(true);

            VerifyListCount(Times.Once());
        }

        [Test]
        public void It_deactivates_on_navigation_away_and_reactivates_on_return()
        {
            var viewModel = CreateViewModel();

            viewModel.SelectedDestination = viewModel.Destinations.First(destination => destination.Label == "Settings");
            VerifyListCount(Times.Once());

            viewModel.SelectedDestination = viewModel.Destinations.First(destination => destination.Label == "Devices");
            VerifyListCount(Times.Exactly(2));
        }
    }
}
