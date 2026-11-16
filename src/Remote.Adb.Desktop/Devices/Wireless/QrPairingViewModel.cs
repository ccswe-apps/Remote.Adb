using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Desktop.Common.Diagnostics;
using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// QR-code pairing on the dialog's Pair tab. Shows a fresh <see cref="QrPairingPayload"/>; when the device scans it, it
/// advertises a pairing service named after the payload, which the dialog's shared discovery poll picks up — no second
/// poller — and this pairs with it like a pairing code. The code is regenerated every <see cref="RegenerateInterval"/>,
/// and after a failed attempt. <see cref="FormViewModelBase.IsBusy"/> is set while a pairing attempt runs.
/// </summary>
public sealed partial class QrPairingViewModel : FormViewModelBase
{
    internal static readonly TimeSpan RegenerateInterval = TimeSpan.FromMinutes(2);

    private readonly PairingCodeViewModel _codeForm;
    private bool _isStarted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QrText))]
    private QrPairingPayload _payload = QrPairingPayload.Create();

    private readonly IDispatcherTimer _regenerateTimer;
    private readonly IUnhandledExceptionReporter _reporter;
    private readonly IWirelessAdbService _wireless;

    public QrPairingViewModel(
        IWirelessAdbService wireless,
        WirelessDiscoveryViewModel discovery,
        PairingCodeViewModel codeForm,
        ITimerFactory timerFactory,
        IUnhandledExceptionReporter reporter)
    {
        _wireless = wireless;
        Discovery = discovery;
        _codeForm = codeForm;
        _reporter = reporter;
        _regenerateTimer = timerFactory.Create(RegenerateInterval);
        _regenerateTimer.Tick += (_, _) => Regenerate();
    }

    /// <summary>Raised when the device paired, whether or not it then connected.</summary>
    public event Action<WirelessPairResult>? Paired;

    /// <summary>The discovery poll this watches; its <see cref="WirelessDiscoveryViewModel.IsUnavailable"/> means a
    /// scanned code could never be found, so the view shows a hint instead of the code.</summary>
    public WirelessDiscoveryViewModel Discovery { get; }

    /// <summary>The text the QR code encodes.</summary>
    public string QrText => Payload.Text;

    private void OnPairingServicesChanged(object? sender, NotifyCollectionChangedEventArgs e) => TryPairAsync().Forget(_reporter);

    private void Regenerate()
    {
        if (!IsBusy)
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
        Discovery.PairingServices.CollectionChanged += OnPairingServicesChanged;
        _regenerateTimer.Start();
    }

    /// <summary>Stops watching and regenerating.</summary>
    public void Stop()
    {
        _isStarted = false;
        Discovery.PairingServices.CollectionChanged -= OnPairingServicesChanged;
        _regenerateTimer.Stop();
    }

    private async Task TryPairAsync()
    {
        // One attempt at a time, and never while the code form is pairing — two concurrent pairings would race.
        if (!_isStarted || IsBusy || _codeForm.IsBusy)
        {
            return;
        }

        var payload = Payload;
        var service = Discovery.PairingServices.FirstOrDefault(candidate => candidate.Instance == payload.ServiceName);
        if (service is null)
        {
            return;
        }

        // Shown in the code form too, and used to prefill the Connect tab if the device pairs but doesn't connect.
        _codeForm.PairingAddress = service.Address;

        var paired = await RunGuardedAsync(async () =>
        {
            // The password is just a pairing code; PairAsync also waits for the device to connect.
            var result = await _wireless.PairAsync(service.Address, payload.Password);
            if (!result.Paired)
            {
                return $"{result.Message} Scan the new code to try again.";
            }

            Paired?.Invoke(result);
            return null;
        });

        if (!paired)
        {
            Regenerate();
        }
    }
}
