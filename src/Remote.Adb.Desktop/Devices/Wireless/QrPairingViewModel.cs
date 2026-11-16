using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// QR-code pairing on the dialog's Pair tab. Shows a fresh <see cref="QrPairingPayload"/>; when the device scans it, it
/// advertises a pairing service named after the payload, which the dialog's shared discovery poll picks up — no second
/// poller — and this pairs with it like a pairing code. The code is regenerated every <see cref="RegenerateInterval"/>,
/// and after a failed attempt.
/// </summary>
public sealed partial class QrPairingViewModel : ViewModelBase
{
    internal static readonly TimeSpan RegenerateInterval = TimeSpan.FromMinutes(2);

    private readonly PairingCodeViewModel _codeForm;
    private readonly WirelessDiscoveryViewModel _discovery;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isPairing;

    private bool _isStarted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QrText))]
    private QrPairingPayload _payload = QrPairingPayload.Create();

    private readonly IDispatcherTimer _regenerateTimer;
    private readonly IWirelessAdbService _wireless;

    public QrPairingViewModel(IWirelessAdbService wireless, WirelessDiscoveryViewModel discovery, PairingCodeViewModel codeForm, ITimerFactory timerFactory)
    {
        _wireless = wireless;
        _discovery = discovery;
        _codeForm = codeForm;
        _regenerateTimer = timerFactory.Create(RegenerateInterval);
        _regenerateTimer.Tick += (_, _) => Regenerate();
    }

    /// <summary>Raised when the device paired, whether or not it then connected.</summary>
    public event Action<WirelessPairResult>? Paired;

    /// <summary>The discovery poll this watches; its <see cref="WirelessDiscoveryViewModel.IsUnavailable"/> means a
    /// scanned code could never be found, so the view shows a hint instead of the code.</summary>
    public WirelessDiscoveryViewModel Discovery => _discovery;

    /// <summary>The text the QR code encodes.</summary>
    public string QrText => Payload.Text;

    private void OnPairingServicesChanged(object? sender, NotifyCollectionChangedEventArgs e) => _ = TryPairAsync();

    private void Regenerate()
    {
        if (!IsPairing)
        {
            Payload = QrPairingPayload.Create();
        }
    }

    /// <summary>Starts watching discovery for the device that scans the code.</summary>
    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        _discovery.PairingServices.CollectionChanged += OnPairingServicesChanged;
        _regenerateTimer.Start();
    }

    /// <summary>Stops watching and regenerating.</summary>
    public void Stop()
    {
        _isStarted = false;
        _discovery.PairingServices.CollectionChanged -= OnPairingServicesChanged;
        _regenerateTimer.Stop();
    }

    private async Task TryPairAsync()
    {
        // One attempt at a time, and never while the code form is pairing — two concurrent pairings would race.
        if (!_isStarted || IsPairing || _codeForm.IsBusy)
        {
            return;
        }

        var payload = Payload;
        var service = _discovery.PairingServices.FirstOrDefault(candidate => candidate.Instance == payload.ServiceName);
        if (service is null)
        {
            return;
        }

        IsPairing = true;
        ErrorMessage = null;

        // Shown in the code form too, and used to prefill the Connect tab if the device pairs but doesn't connect.
        _codeForm.PairingAddress = service.Address;
        try
        {
            // The password is just a pairing code; PairAsync also waits for the device to connect.
            var result = await _wireless.PairAsync(service.Address, payload.Password);
            if (result.Paired)
            {
                Paired?.Invoke(result);
                return;
            }

            ErrorMessage = $"{result.Message} Scan the new code to try again.";
        }
        catch (ProcessLaunchException exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsPairing = false;
        }

        Regenerate();
    }
}
