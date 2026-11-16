using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Desktop.Common;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// The "Pair" tab: pairs an Android 11+ device using the address and six-digit code from its Wireless debugging
/// "Pair device with pairing code" dialog.
/// </summary>
public sealed partial class PairingCodeViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    private string _code = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    private string _pairingAddress = string.Empty;

    private readonly IWirelessAdbService _wireless;

    public PairingCodeViewModel(IWirelessAdbService wireless)
    {
        _wireless = wireless;
    }

    /// <summary>Raised when pairing succeeds, whether or not the device then connected.</summary>
    public event Action<WirelessPairResult>? Paired;

    private bool CanPair() => !IsBusy && !string.IsNullOrWhiteSpace(PairingAddress) && !string.IsNullOrWhiteSpace(Code);

    [RelayCommand(CanExecute = nameof(CanPair))]
    private async Task PairAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await _wireless.PairAsync(PairingAddress, Code);
            if (result.Paired)
            {
                Paired?.Invoke(result);
                return;
            }

            ErrorMessage = $"{result.Message} Check the code and that the pairing dialog is still open on the device — it closes (and the code changes) after a short while.";
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
}
