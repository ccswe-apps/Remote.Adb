using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Emulators;
using Remote.Adb.Core.Logging;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Common.Notifications;
using Remote.Adb.Desktop.Common.Threading;
using Remote.Adb.Desktop.Devices.Wireless;

namespace Remote.Adb.Desktop.Devices;

/// <summary>
/// The unified device page: lists virtual devices (AVDs, running or stopped) and physical/wireless devices in
/// one auto-refreshing list. Virtual rows carry start/stop/delete/details; physical rows show a connection indicator,
/// and wireless ones can be disconnected.
/// </summary>
/// <remarks>
/// Row commands take the row as their parameter and allow concurrent executions — starting one AVD must not disable
/// Start on every other row for the minutes its launch takes. Each row guards itself (<see cref="DeviceRowViewModel.IsStarting"/>,
/// <see cref="DeviceRowViewModel.IsBusy"/>) against a second action on the same row.
/// </remarks>
public partial class DevicesViewModel : AutoRefreshingListViewModel
{
    private const string EmulatorSerialPrefix = "emulator-";

    // Background re-list cadence while the page is live, so external start/stop/create or a device plugged/
    // unplugged shows up without a manual refresh. Listing shells out to adb/emulator, so keep it modest.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly IAvdConfigStore _configStore;
    private readonly IConfirmDialog _confirmDialog;
    private readonly IAvdCreateDialog _createDialog;
    private readonly AvdDetailsViewModelFactory _detailsFactory;
    private readonly IDeviceService _deviceService;
    private readonly IEmulatorService _emulatorService;
    private readonly IAvdProvisioningService _provisioning;

    [ObservableProperty]
    private AvdDetailsViewModel? _selectedDetail;

    private readonly TimeProvider _timeProvider;
    private readonly IWirelessAdbService _wireless;
    private readonly IWirelessDeviceDialog _wirelessDialog;

    public DevicesViewModel(
        IEmulatorService emulatorService,
        IDeviceService deviceService,
        IAvdConfigStore configStore,
        IAvdCreateDialog createDialog,
        AvdDetailsViewModelFactory detailsFactory,
        IAvdProvisioningService provisioning,
        IConfirmDialog confirmDialog,
        IWirelessDeviceDialog wirelessDialog,
        IWirelessAdbService wireless,
        INotificationService notifications,
        ITimerFactory timerFactory,
        TimeProvider timeProvider,
        ILogger<DevicesViewModel> logger)
        : base(timerFactory, notifications, RefreshInterval, logger)
    {
        _emulatorService = emulatorService;
        _deviceService = deviceService;
        _configStore = configStore;
        _createDialog = createDialog;
        _detailsFactory = detailsFactory;
        _provisioning = provisioning;
        _confirmDialog = confirmDialog;
        _wirelessDialog = wirelessDialog;
        _wireless = wireless;
        _timeProvider = timeProvider;

        Devices.CollectionChanged += (_, _) => RaiseIsListEmptyChanged();
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = [];

    protected override bool IsEmpty => Devices.Count == 0;

    protected override string LoadErrorTitle => "Couldn't list devices";

    /// <summary>How often a just-launched AVD is re-listed while waiting for it to register with adb.</summary>
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    // Don't fight an in-flight start poll: skip the background tick while an AVD is mid-launch.
    protected override bool SkipBackgroundTick => Devices.Any(device => device is { IsVirtual: true, IsStarting: true });

    /// <summary>How long a launch is waited on before the row leaves the "starting" state with a warning.</summary>
    internal TimeSpan StartTimeout { get; init; } = TimeSpan.FromMinutes(3);

    // Opens the "Add wireless device" dialog; if a device was connected, refresh so it shows up in the list.
    [RelayCommand]
    private async Task AddWirelessAsync()
    {
        if (await _wirelessDialog.ShowAsync())
        {
            await RefreshAsync(notifyOnError: true);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ColdBootAsync(DeviceRowViewModel? device) => LaunchAsync(device, coldBoot: true);

    // Opens the create wizard; if an AVD was created, refresh so it shows up in the list.
    [RelayCommand]
    private async Task CreateAsync()
    {
        if (await _createDialog.ShowAsync())
        {
            await RefreshAsync(notifyOnError: true);
        }
    }

    // Confirms, then deletes the AVD via avdmanager and refreshes the list. The row's menu item is disabled
    // while the AVD is running; this re-checks for safety (deleting a running AVD's files would fail).
    [RelayCommand]
    private async Task DeleteAsync(DeviceRowViewModel? device)
    {
        if (device is null || !device.IsVirtual || device.IsRunning)
        {
            return;
        }

        var confirmed = await _confirmDialog.ConfirmAsync(
            "Delete emulator?",
            $"Delete '{device.DisplayName}'? This permanently removes the AVD and its data.",
            "Delete");

        if (!confirmed)
        {
            return;
        }

        await RunRowActionAsync("Delete failed", async () =>
        {
            if (!await _provisioning.DeleteAsync(device.Name))
            {
                NotifyError("Delete failed", $"Could not delete '{device.DisplayName}'.");
                return;
            }

            SelectedDetail = null;
        });
    }

    // Disconnects a wireless device (which also stops it being reconnected automatically) and refreshes the list.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DisconnectAsync(DeviceRowViewModel? device)
    {
        if (device?.Serial is not { } serial || !device.IsWireless || device.IsBusy)
        {
            return;
        }

        await RunBusyRowActionAsync(device, "Couldn't disconnect", async () =>
        {
            var result = await _wireless.DisconnectAsync(serial);
            if (!result.Success)
            {
                NotifyError("Couldn't disconnect", result.Message);
            }
        });
    }

    // Switches an online USB device to Wi-Fi debugging (tcpip, then connect — remembered for reconnects), then refreshes so
    // the new network row appears. Reports the partial case (switched, but unreachable) as a warning with what to check.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task EnableWirelessAsync(DeviceRowViewModel? device)
    {
        if (device?.Serial is not { } serial || !device.IsUsb || device.IsBusy)
        {
            return;
        }

        await RunBusyRowActionAsync(device, "Couldn't enable Wi-Fi debugging", async () =>
        {
            var result = await _wireless.EnableWirelessAsync(serial);
            switch (result.Outcome)
            {
                case WirelessEnableOutcome.Connected:
                    Notifications.Show("Wi-Fi debugging on", $"{device.DisplayName} is connected at {result.Address}. You can unplug it.", NotificationSeverity.Success);
                    break;
                case WirelessEnableOutcome.ConnectFailed:
                    Notifications.Show(
                        "Couldn't reach the device over Wi-Fi",
                        $"{device.DisplayName} switched to Wi-Fi debugging at {result.Address}, but this computer couldn't connect ({result.Message}). Check it's on the same network, or plug it back in.",
                        NotificationSeverity.Warning);
                    break;
                default:
                    NotifyError("Couldn't enable Wi-Fi debugging", result.Message);
                    break;
            }
        });
    }

    // Shared launch flow for the normal start and cold-boot commands: guard, flip the row into the transient
    // "starting" state, kick off the emulator, then poll until adb reports it running (or warn when it never does).
    private async Task LaunchAsync(DeviceRowViewModel? device, bool coldBoot)
    {
        if (device is null || !device.IsVirtual || device.IsRunning || device.IsStarting)
        {
            return;
        }

        device.IsStarting = true;
        try
        {
            await RunRowActionAsync("Couldn't start emulator", async () =>
            {
                await _emulatorService.StartAsync(device.Name, coldBoot);
                if (!await WaitUntilRunningAsync(device))
                {
                    Notifications.Show(
                        "Emulator didn't come up",
                        $"{device.DisplayName} still isn't running after {StartTimeout.TotalMinutes:0} minutes. It may still be booting — refresh to check, or see the log.",
                        NotificationSeverity.Warning);
                }
            }, refresh: false);
        }
        finally
        {
            device.IsStarting = false;
        }
    }

    // Lists AVDs and adb devices concurrently, drops the running emulators from the adb list (they're already
    // represented by AVD rows), then reconciles each source into its own rows and reorders the whole list.
    protected override async Task LoadAsync()
    {
        var avdTask = _emulatorService.ListAsync();
        var deviceTask = _deviceService.ListAsync();
        await Task.WhenAll(avdTask, deviceTask);

        MergeVirtual(avdTask.Result);
        MergePhysical(deviceTask.Result);
        Devices.SortBy(device => device.SortKey);
    }

    private void MergePhysical(IReadOnlyList<AdbDevice> devices)
    {
        var physical = devices.Where(device => !device.Serial.StartsWith(EmulatorSerialPrefix, StringComparison.Ordinal)).ToList();

        Devices.MergeByKind(
            physical,
            DeviceRowViewModel.KeyFor,
            row => row.IdentityKey,
            row => row.IsPhysical,
            device => new DeviceRowViewModel(device, DisconnectCommand, EnableWirelessCommand),
            (row, device) => row.Update(device));
    }

    // Reconciles the virtual rows with the latest AVD snapshot in place, so selection and the transient
    // "starting" state survive a refresh instead of being thrown away. Leaves physical rows untouched.
    private void MergeVirtual(IReadOnlyList<AndroidVirtualDevice> devices) =>
        Devices.MergeByKind(
            devices,
            DeviceRowViewModel.KeyFor,
            row => row.IdentityKey,
            row => row.IsVirtual,
            device => new DeviceRowViewModel(device, DeleteCommand, ColdBootCommand),
            (row, device) => row.Update(device));

    // Transient failures surface as an auto-dismissing error toast (not a persistent inline label).
    private void NotifyError(string title, string message) =>
        Notifications.Show(title, message, NotificationSeverity.Error);

    // A row action that marks its row busy while it runs, so the row's menu item is disabled meanwhile.
    private async Task RunBusyRowActionAsync(DeviceRowViewModel device, string errorTitle, Func<Task> action)
    {
        device.IsBusy = true;
        try
        {
            await RunRowActionAsync(errorTitle, action);
        }
        finally
        {
            device.IsBusy = false;
        }
    }

    // The shared shape of every row action: run it, refresh the list so the result shows (unless the action keeps the
    // list current itself), and turn a missing tool into an error toast titled for the action.
    private async Task RunRowActionAsync(string errorTitle, Func<Task> action, bool refresh = true)
    {
        try
        {
            await action();
            if (refresh)
            {
                await RefreshAsync(notifyOnError: true);
            }
        }
        catch (ProcessLaunchException exception)
        {
            NotifyError(errorTitle, exception.Message);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task StartAsync(DeviceRowViewModel? device) => LaunchAsync(device, coldBoot: false);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task StopAsync(DeviceRowViewModel? device)
    {
        if (device?.Serial is not { } serial || !device.IsVirtual || device.IsBusy)
        {
            return;
        }

        await RunBusyRowActionAsync(device, "Couldn't stop emulator", () => _emulatorService.StopAsync(serial));
    }

    // Opens the read-only details pane for an AVD row, loading its full config.ini via the store. The detail
    // view model gets a Back callback that clears SelectedDetail, returning the screen to the list.
    [RelayCommand]
    private void ViewDetails(DeviceRowViewModel? device)
    {
        if (device is null || !device.IsVirtual)
        {
            return;
        }

        var configuration = _configStore.Read(device.Name);
        if (configuration is null)
        {
            NotifyError("Details unavailable", $"No configuration found for {device.DisplayName}.");
            return;
        }

        SelectedDetail = _detailsFactory(configuration, () => SelectedDetail = null);
    }

    // Polls adb until the just-launched AVD registers as running (flipping its row out of the "starting" state),
    // returning false if it hasn't after StartTimeout. Merges only the virtual rows so physical rows are preserved.
    private async Task<bool> WaitUntilRunningAsync(DeviceRowViewModel device)
    {
        var deadline = _timeProvider.GetUtcNow() + StartTimeout;

        // Polling for the launch to settle is routine; keep its re-lists out of the Information-level log.
        using var scope = CommandLogScope.Background();

        while (_timeProvider.GetUtcNow() < deadline)
        {
            await Task.Delay(PollInterval, _timeProvider);

            var devices = await _emulatorService.ListAsync();
            MergeVirtual(devices);
            Devices.SortBy(row => row.SortKey);

            if (devices.Any(candidate => candidate.Name == device.Name && candidate.IsRunning))
            {
                return true;
            }
        }

        return false;
    }
}
