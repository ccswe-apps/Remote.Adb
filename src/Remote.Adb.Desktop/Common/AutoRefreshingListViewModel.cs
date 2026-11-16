using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Logging;
using Remote.Adb.Desktop.Common.Notifications;
using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.Common;

/// <summary>
/// Base for a list page that auto-refreshes while live: owns the background re-list (via <see cref="PeriodicRefresher"/>),
/// the load/empty-state flags, and the <see cref="IActivatable"/> lifecycle. Derived pages supply their collection's
/// emptiness, the load+merge step, and the error-toast title.
/// </summary>
public abstract partial class AutoRefreshingListViewModel : ViewModelBase, IActivatable
{
    private bool _hasLoaded;
    private bool _loadFailed;
    private readonly PeriodicRefresher _refresher;

    protected AutoRefreshingListViewModel(ITimerFactory timerFactory, INotificationService notifications, TimeSpan refreshInterval, ILogger logger)
    {
        Notifications = notifications;

        // Background tick: re-list silently (no toast spam on a persistent failure).
        _refresher = new PeriodicRefresher(timerFactory, refreshInterval, () => LoadAndReportAsync(notifyOnError: false), logger)
        {
            SkipTick = () => SkipBackgroundTick,
        };
    }

    /// <summary>Whether the row collection is currently empty.</summary>
    protected abstract bool IsEmpty { get; }

    // The full-page empty state shows only once the first load has settled with nothing to show (not before a
    // load, and not masking a load error) — independent of the transient refresh flag so a tick doesn't flash it.
    public bool IsListEmpty => _hasLoaded && !_loadFailed && IsEmpty;

    /// <summary>The toast title shown when a user-initiated refresh fails.</summary>
    protected abstract string LoadErrorTitle { get; }

    /// <summary>Shared notification sink; also used by derived pages for their own action errors.</summary>
    protected INotificationService Notifications { get; }

    /// <summary>Whether to skip a background tick (e.g. while a row is mid-launch). Never, by default.</summary>
    protected virtual bool SkipBackgroundTick => false;

    private async Task LoadAndReportAsync(bool notifyOnError)
    {
        try
        {
            await LoadAsync();
            _loadFailed = false;
        }
        catch (ProcessLaunchException exception)
        {
            _loadFailed = true;
            if (notifyOnError)
            {
                Notifications.Show(LoadErrorTitle, exception.Message, NotificationSeverity.Error);
            }
        }
        finally
        {
            _hasLoaded = true;
            OnPropertyChanged(nameof(IsListEmpty));
        }
    }

    /// <summary>Loads the latest snapshot and merges it into the row collection.</summary>
    protected abstract Task LoadAsync();

    /// <summary>Re-lists immediately (so the page is fresh on return) and starts the background re-list.</summary>
    public async Task OnActivatedAsync()
    {
        _refresher.Start();

        // Automatic (navigation / focus return), not user-initiated: log its commands as routine background work.
        using var scope = CommandLogScope.Background();
        await RefreshAsync(notifyOnError: true);
    }

    /// <summary>Stops the background re-list when the page is no longer live.</summary>
    public void OnDeactivated()
    {
        _refresher.Stop();
    }

    /// <summary>Re-evaluate <see cref="IsListEmpty"/>; derived pages call this from their collection's change event.</summary>
    protected void RaiseIsListEmptyChanged() => OnPropertyChanged(nameof(IsListEmpty));

    // Shares the refresher's reentrancy guard, so a manual refresh and a tick never overlap; a manual refresh during a
    // tick runs as one follow-up pass rather than being dropped.
    protected Task RefreshAsync(bool notifyOnError) => _refresher.RunAsync(() => LoadAndReportAsync(notifyOnError));

    [RelayCommand]
    private Task RefreshAsync() => RefreshAsync(notifyOnError: true);
}
