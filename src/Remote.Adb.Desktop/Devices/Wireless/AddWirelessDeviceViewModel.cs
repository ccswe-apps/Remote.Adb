using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Desktop.Common;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// The "Add wireless device" dialog: a Pair tab (Android 11+ pairing code) and a Connect tab (an address). Closes
/// with <see langword="true"/> once a device is connected. A device that pairs but doesn't connect on its own sends
/// the user to the Connect tab with its IP filled in, since the connect port differs from the pairing port.
/// </summary>
public sealed partial class AddWirelessDeviceViewModel : ViewModelBase, IDialogViewModel
{
    public const int ConnectTab = 1;
    public const int PairTab = 0;

    [ObservableProperty]
    private string? _pairedNotice;

    [ObservableProperty]
    private int _selectedTab = PairTab;

    public AddWirelessDeviceViewModel(IWirelessAdbService wireless)
    {
        Pair = new PairingCodeViewModel(wireless);
        Connect = new ConnectAddressViewModel(wireless);

        Pair.Paired += OnPaired;
        Connect.Connected += () => CloseRequested?.Invoke(true);
    }

    /// <inheritdoc />
    public event Action<bool>? CloseRequested;

    public ConnectAddressViewModel Connect { get; }

    public PairingCodeViewModel Pair { get; }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    private static string HostOf(string address)
    {
        var trimmed = address.Trim();
        var colon = trimmed.LastIndexOf(':');
        return colon > 0 ? trimmed[..colon] : trimmed;
    }

    private void OnPaired(WirelessPairResult result)
    {
        if (result.Connected)
        {
            CloseRequested?.Invoke(true);
            return;
        }

        // Paired, but adb couldn't reach the device's connect service on its own: the connect port is shown on the
        // device's Wireless debugging screen (it isn't the pairing port), so prefill the host and ask for the port.
        var host = HostOf(Pair.PairingAddress);
        Connect.Address = host.Length > 0 ? $"{host}:" : string.Empty;
        PairedNotice = "Paired. Now enter the IP address & port shown under Wireless debugging on the device.";
        SelectedTab = ConnectTab;
    }
}
