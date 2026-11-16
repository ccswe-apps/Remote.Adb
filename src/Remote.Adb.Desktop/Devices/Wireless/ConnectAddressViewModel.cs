using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Desktop.Common;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// The "Connect" tab: connects to a device by <c>host[:port]</c> — a device in legacy <c>adb tcpip</c> mode, or an
/// already-paired Android 11+ device at the address its Wireless debugging screen shows.
/// </summary>
public sealed partial class ConnectAddressViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _address = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isBusy;

    private readonly IWirelessAdbService _wireless;

    public ConnectAddressViewModel(IWirelessAdbService wireless)
    {
        _wireless = wireless;
    }

    /// <summary>Raised once the device is connected.</summary>
    public event Action? Connected;

    private bool CanConnect() => !IsBusy && !string.IsNullOrWhiteSpace(Address);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await _wireless.ConnectAsync(Address);
            if (result.Success)
            {
                Connected?.Invoke();
                return;
            }

            ErrorMessage = Describe(result);
        }
        catch (ProcessLaunchException exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // adb's message, plus the likely fix for the failures people actually hit.
    private static string Describe(WirelessConnectResult result) => result.Outcome switch
    {
        WirelessConnectOutcome.AuthenticationFailed =>
            $"{result.Message}. Accept the \"Allow debugging?\" prompt on the device, or pair it first.",
        WirelessConnectOutcome.ConnectionFailed =>
            $"{result.Message}. Check the address and port, and that the device is on the same network as this computer.",
        _ => result.Message,
    };
}
