using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Remote.Adb.Desktop.Logging;

/// <summary>
/// Hosts the log list. Following new entries is view-only behavior, so it lives here: when
/// <see cref="LogPanelViewModel.AutoScroll"/> is on, the list scrolls to the newest entry as entries arrive.
/// </summary>
public partial class LogPanelView : UserControl
{
    private INotifyCollectionChanged? _entries;
    private LogPanelViewModel? _viewModel;

    public LogPanelView()
    {
        InitializeComponent();

        // Opening the panel (it lays out from zero height) or resizing it changes the list's size; keep following.
        EntryList.SizeChanged += (_, _) => ScrollToEnd();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as LogPanelViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        TrackEntries();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            ScrollToEnd();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogPanelViewModel.Entries))
        {
            TrackEntries();
            ScrollToEnd();
        }
        else if (e.PropertyName == nameof(LogPanelViewModel.AutoScroll))
        {
            ScrollToEnd();
        }
    }

    // Deferred to after layout so the new item has been realized before scrolling to it.
    private void ScrollToEnd()
    {
        if (_viewModel is not { AutoScroll: true })
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var count = _viewModel?.Entries.Count ?? 0;
            if (count > 0)
            {
                EntryList.ScrollIntoView(count - 1);
            }
        }, DispatcherPriority.Background);
    }

    private void TrackEntries()
    {
        if (_entries is not null)
        {
            _entries.CollectionChanged -= OnEntriesChanged;
        }

        _entries = _viewModel?.Entries;

        if (_entries is not null)
        {
            _entries.CollectionChanged += OnEntriesChanged;
        }
    }
}
