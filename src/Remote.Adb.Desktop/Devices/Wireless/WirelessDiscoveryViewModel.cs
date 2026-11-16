using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Logging;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// The "Add wireless device" dialog's one mDNS poller: while the dialog is open it re-reads the wireless-debugging
/// services the local adb server can see, split into devices waiting to pair (their pairing dialog is open) and
/// devices ready to connect. Both tabs — and QR pairing — read from it, so there's never more than one poll running.
/// </summary>
public sealed partial class WirelessDiscoveryViewModel : ViewModelBase
{
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private bool _isStarted;

    [ObservableProperty]
    private bool _isUnavailable;

    private readonly PeriodicRefresher _refresher;
    private readonly IWirelessAdbService _wireless;

    public WirelessDiscoveryViewModel(IWirelessAdbService wireless, ITimerFactory timerFactory, ILogger logger)
    {
        _wireless = wireless;
        _refresher = new PeriodicRefresher(timerFactory, RefreshInterval, RefreshAsync, logger);
    }

    /// <summary>Paired (or legacy <c>adb tcpip</c>) devices that can be connected.</summary>
    public ObservableCollection<MdnsService> ConnectServices { get; } = [];

    /// <summary>Devices whose "Pair device with pairing code" / QR dialog is open.</summary>
    public ObservableCollection<MdnsService> PairingServices { get; } = [];

    private static string KeyOf(MdnsService service) => $"{service.Type}|{service.Instance}|{service.Address}";

    private static void Merge(ObservableCollection<MdnsService> rows, IReadOnlyList<MdnsService> services) =>
        rows.MergeBy(services, KeyOf, KeyOf, service => service, (_, _) => { }, service => service.Instance);

    private async Task RefreshAsync()
    {
        IReadOnlyList<MdnsService> services;
        try
        {
            services = await _wireless.DiscoverAsync();
        }
        catch (ProcessLaunchException)
        {
            // adb itself is missing; the Devices page reports that. Keep the last list.
            return;
        }

        Merge(PairingServices, services.Where(service => service.Type == MdnsServiceType.Pairing).ToList());
        Merge(ConnectServices, services.Where(service => service.Type is MdnsServiceType.Connect or MdnsServiceType.Legacy).ToList());
    }

    /// <summary>
    /// Checks that discovery works, then lists services immediately and every <see cref="RefreshInterval"/> until
    /// <see cref="Stop"/>. When it doesn't work, <see cref="IsUnavailable"/> is set and nothing polls.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;

        // Opening the dialog is automatic polling, not a user action: keep these checks out of the default log level.
        using var scope = CommandLogScope.Background();

        bool available;
        try
        {
            available = await _wireless.IsDiscoveryAvailableAsync();
        }
        catch (ProcessLaunchException)
        {
            available = false;
        }

        IsUnavailable = !available;
        if (!available || !_isStarted)
        {
            return;
        }

        _refresher.Start();
        await _refresher.RunAsync(RefreshAsync);
    }

    /// <summary>Stops polling.</summary>
    public void Stop()
    {
        _isStarted = false;
        _refresher.Stop();
    }
}
