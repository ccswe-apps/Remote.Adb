using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remote.Adb.Core.Adb.Wireless;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// The "Pair" tab: pairs an Android 11+ device using the address and six-digit code from its Wireless debugging
/// "Pair device with pairing code" dialog.
/// </summary>
public sealed partial class PairingCodeViewModel : FormViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    private string _code = string.Empty;

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

    // The command itself is disabled while it runs (no concurrent executions), so busy needn't be checked here.
    private bool CanPair() => !string.IsNullOrWhiteSpace(PairingAddress) && !string.IsNullOrWhiteSpace(Code);

    [RelayCommand(CanExecute = nameof(CanPair))]
    private Task PairAsync() => RunGuardedAsync(async () =>
    {
        var result = await _wireless.PairAsync(PairingAddress, Code);
        if (!result.Paired)
        {
            return $"{result.Message} Check the code and that the pairing dialog is still open on the device — it closes (and the code changes) after a short while.";
        }

        Paired?.Invoke(result);
        return null;
    });
}
