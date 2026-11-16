using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// The "Add wireless device" dialog: a Pair tab (Android 11+ QR code or pairing code) and a Connect tab (an address), each with the
/// matching devices discovered on the network. Closes with <see langword="true"/> once a device is connected. A device
/// that pairs but doesn't connect on its own sends the user to the Connect tab with its IP filled in, since the connect
/// port differs from the pairing port. Discovery polls only while the dialog is open (<see cref="OnOpened"/> /
/// <see cref="OnClosed"/>).
/// </summary>
public sealed partial class AddWirelessDeviceViewModel : ViewModelBase, IDialogViewModel
{
    public const int ConnectTab = 1;
    public const int PairTab = 0;

    private bool _isClosing;

    [ObservableProperty]
    private string? _pairedNotice;

    [ObservableProperty]
    private int _selectedTab = PairTab;

    public AddWirelessDeviceViewModel(IWirelessAdbService wireless, ITimerFactory timerFactory)
    {
        Pair = new PairingCodeViewModel(wireless);
        Connect = new ConnectAddressViewModel(wireless);
        Discovery = new WirelessDiscoveryViewModel(wireless, timerFactory);
        Qr = new QrPairingViewModel(wireless, Discovery, Pair, timerFactory);

        Pair.Paired += OnPaired;
        Qr.Paired += OnPaired;
        Connect.Connected += () => Close(true);
    }

    /// <inheritdoc />
    public event Action<bool>? CloseRequested;

    public ConnectAddressViewModel Connect { get; }

    public WirelessDiscoveryViewModel Discovery { get; }

    public PairingCodeViewModel Pair { get; }

    public QrPairingViewModel Qr { get; }

    [RelayCommand]
    private void Cancel() => Close(false);

    // QR and code pairing can both finish; ask the host to close at most once.
    private void Close(bool result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        CloseRequested?.Invoke(result);
    }

    // A discovered connect (or legacy tcpip) service: connect to it straight away, through the Connect tab's own flow so
    // the busy state and error text are shared.
    [RelayCommand]
    private Task ConnectToServiceAsync(MdnsService? service) => service is null ? Task.CompletedTask : Connect.ConnectToAsync(service);

    private static string HostOf(string address)
    {
        var trimmed = address.Trim();
        var colon = trimmed.LastIndexOf(':');
        return colon > 0 ? trimmed[..colon] : trimmed;
    }

    /// <inheritdoc />
    public void OnClosed()
    {
        Qr.Stop();
        Discovery.Stop();
    }

    /// <inheritdoc />
    public void OnOpened()
    {
        Qr.Start();
        _ = Discovery.StartAsync();
    }

    private void OnPaired(WirelessPairResult result)
    {
        if (result.Connected)
        {
            Close(true);
            return;
        }

        // Paired, but adb couldn't reach the device's connect service on its own: the connect port is shown on the
        // device's Wireless debugging screen (it isn't the pairing port), so prefill the host and ask for the port.
        var host = HostOf(Pair.PairingAddress);
        Connect.Address = host.Length > 0 ? $"{host}:" : string.Empty;
        PairedNotice = "Paired. Now enter the IP address & port shown under Wireless debugging on the device.";
        SelectedTab = ConnectTab;
    }

    // A discovered device waiting to pair: fill in its pairing address, leaving only the code to type.
    [RelayCommand]
    private void SelectPairingService(MdnsService? service)
    {
        if (service is not null)
        {
            Pair.PairingAddress = service.Address;
        }
    }
}
