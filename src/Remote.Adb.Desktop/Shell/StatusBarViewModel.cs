using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Logging;
using Remote.Adb.Core.Settings;
using Remote.Adb.Core.Tunnel;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.Shell;

/// <summary>
/// The strip along the bottom of the window — the log panel's collapsed form. It shows the tunnel's state, the last
/// command the app ran (only user-initiated work reaches the log store, so background polls never replace it), and
/// how many warnings/errors were logged while the log panel was closed.
/// </summary>
public sealed partial class StatusBarViewModel : ViewModelBase, IDisposable
{
    private const int MaxBadgeCount = 99;

    [ObservableProperty]
    private bool _isLogVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastCommand))]
    private string? _lastCommand;

    [ObservableProperty]
    private bool _lastCommandFailed;

    [ObservableProperty]
    private string? _lastCommandResult;

    private readonly ILogStore _logStore;
    private readonly ISettingsService _settings;
    private readonly ITunnelService _tunnel;
    private readonly IUiDispatcher _uiDispatcher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnseenProblems))]
    [NotifyPropertyChangedFor(nameof(UnseenProblemText))]
    private int _unseenProblemCount;

    public StatusBarViewModel(ILogStore logStore, ITunnelService tunnel, ISettingsService settings, IUiDispatcher uiDispatcher)
    {
        _logStore = logStore;
        _tunnel = tunnel;
        _settings = settings;
        _uiDispatcher = uiDispatcher;

        _logStore.EntryAdded += OnEntryAdded;
        _tunnel.StatusChanged += OnTunnelStatusChanged;

        // Seed from history so a fresh window shows what already ran (e.g. the tunnel auto-connect at startup).
        var lastCommand = _logStore.Snapshot().LastOrDefault(IsCommandOutcome);
        if (lastCommand is not null)
        {
            ShowCommand(lastCommand);
        }
    }

    public bool HasLastCommand => LastCommand is not null;

    public bool HasUnseenProblems => UnseenProblemCount > 0;

    public bool IsTunnelBusy => _tunnel.Status.State is TunnelState.Connecting or TunnelState.Reconnecting;

    public bool IsTunnelConnected => _tunnel.Status.State == TunnelState.Connected;

    public bool IsTunnelFaulted => _tunnel.Status.State == TunnelState.Faulted;

    public string TunnelText => _tunnel.Status.State switch
    {
        TunnelState.Connected => $"Tunnel · {_settings.TunnelHost}",
        TunnelState.Connecting => "Tunnel · connecting…",
        TunnelState.Reconnecting => "Tunnel · reconnecting…",
        TunnelState.Faulted => "Tunnel · error",
        _ => "Tunnel off",
    };

    /// <summary>The tunnel's detail (the forward when connected, the failure when faulted), for a tooltip.</summary>
    public string? TunnelToolTip => _tunnel.Status.Message;

    public string UnseenProblemText => UnseenProblemCount > MaxBadgeCount ? $"{MaxBadgeCount}+" : UnseenProblemCount.ToString();

    public void Dispose()
    {
        _logStore.EntryAdded -= OnEntryAdded;
        _tunnel.StatusChanged -= OnTunnelStatusChanged;
    }

    private static bool IsCommandOutcome(LogEntry entry) =>
        entry.EventId.Id is LogEvents.CommandCompleted or LogEvents.CommandCancelled or LogEvents.CommandLaunchFailed;

    private void OnEntryAdded(object? sender, LogEntry entry) => _uiDispatcher.Post(() => Record(entry));

    private void OnTunnelStatusChanged(object? sender, TunnelStatus status) => _uiDispatcher.Post(RefreshTunnel);

    private void Record(LogEntry entry)
    {
        if (IsCommandOutcome(entry))
        {
            ShowCommand(entry);
        }

        if (entry.Level >= LogLevel.Warning && !IsLogVisible)
        {
            UnseenProblemCount++;
        }
    }

    private void RefreshTunnel()
    {
        OnPropertyChanged(nameof(IsTunnelBusy));
        OnPropertyChanged(nameof(IsTunnelConnected));
        OnPropertyChanged(nameof(IsTunnelFaulted));
        OnPropertyChanged(nameof(TunnelText));
        OnPropertyChanged(nameof(TunnelToolTip));
    }

    /// <summary>Tells the strip whether the log panel is showing; opening it marks every problem as seen.</summary>
    public void SetLogVisible(bool isVisible)
    {
        IsLogVisible = isVisible;
        if (isVisible)
        {
            UnseenProblemCount = 0;
        }
    }

    private void ShowCommand(LogEntry entry)
    {
        var properties = entry.Properties;
        LastCommand = properties?.GetValueOrDefault(LogEvents.CommandProperty) as string ?? entry.Message;

        var elapsed = properties?.GetValueOrDefault(LogEvents.ElapsedMillisecondsProperty) is long milliseconds ? $"{milliseconds} ms" : null;

        switch (entry.EventId.Id)
        {
            case LogEvents.CommandCompleted:
                var exitCode = properties?.GetValueOrDefault(LogEvents.ExitCodeProperty) as int? ?? 0;
                LastCommandFailed = exitCode != 0;
                LastCommandResult = exitCode == 0 ? $"✓ {elapsed}" : $"✗ exit {exitCode} · {elapsed}";
                break;
            case LogEvents.CommandCancelled:
                LastCommandFailed = false;
                LastCommandResult = $"cancelled · {elapsed}";
                break;
            default:
                LastCommandFailed = true;
                LastCommandResult = "✗ failed to start";
                break;
        }
    }
}
