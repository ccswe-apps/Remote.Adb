using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Logging;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.Logging;

/// <summary>
/// The docked log panel: mirrors the shared <see cref="ILogStore"/> into a bounded, level-filtered list. Entries
/// arrive on whatever thread logged them; they're queued and applied in batches on the UI thread, with at most one
/// flush posted at a time so a burst of logging can't flood the dispatcher.
/// </summary>
public sealed partial class LogPanelViewModel : ViewModelBase, IDisposable
{
    internal const int MaxEntries = LogStore.DefaultCapacity;

    // Every received entry (unfiltered, capped), so changing the filter or resuming from pause can rebuild the view.
    private readonly List<LogEntry> _all = [];

    [ObservableProperty]
    private bool _autoScroll = true;

    private readonly IClipboardService _clipboard;

    [ObservableProperty]
    private ObservableCollection<LogEntryViewModel> _entries = [];

    // Entries at or below this sequence are already shown (from the initial snapshot) or were cleared; drop them.
    private long _floor;

    private int _flushPending;
    private readonly ConcurrentQueue<LogEntry> _incoming = new();

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private LogLevel _minimumLevel = LogLevel.Information;

    private readonly ILogStore _store;
    private readonly IUiDispatcher _uiDispatcher;

    public LogPanelViewModel(ILogStore store, IUiDispatcher uiDispatcher, IClipboardService clipboard)
    {
        _store = store;
        _uiDispatcher = uiDispatcher;
        _clipboard = clipboard;

        // Subscribe before snapshotting so nothing logged in between is missed; the sequence floor drops anything
        // that arrives as an event but was also in the snapshot.
        _store.EntryAdded += OnEntryAdded;

        var snapshot = _store.Snapshot();
        if (snapshot.Count > 0)
        {
            _floor = snapshot[^1].Sequence;
            _all.AddRange(snapshot);
        }

        Rebuild();
    }

    /// <summary>The levels the filter offers; the store only captures Information and above.</summary>
    public IReadOnlyList<LogLevel> LevelOptions { get; } = [LogLevel.Information, LogLevel.Warning, LogLevel.Error];

    [RelayCommand]
    private void Clear()
    {
        _floor = Math.Max(_floor, _store.Clear());
        _all.Clear();
        Entries.Clear();
    }

    [RelayCommand]
    private Task CopyAllAsync() =>
        _clipboard.SetTextAsync(string.Join(Environment.NewLine, Entries.Select(entry => entry.ToString())));

    public void Dispose()
    {
        _store.EntryAdded -= OnEntryAdded;
    }

    private void Flush()
    {
        // Reset before draining: an entry enqueued from here on schedules another flush rather than being stranded.
        Volatile.Write(ref _flushPending, 0);

        var added = new List<LogEntry>();
        while (_incoming.TryDequeue(out var entry))
        {
            if (entry.Sequence > _floor)
            {
                added.Add(entry);
            }
        }

        if (added.Count == 0)
        {
            return;
        }

        // Concurrent loggers can enqueue slightly out of order; present them in the order they were logged.
        added.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));

        _all.AddRange(added);
        if (_all.Count > MaxEntries)
        {
            _all.RemoveRange(0, _all.Count - MaxEntries);
        }

        if (IsPaused)
        {
            return;
        }

        foreach (var entry in added.Where(Matches))
        {
            Entries.Add(new LogEntryViewModel(entry));
        }

        // Trim the oldest rows in place (usually one or two per flush) rather than resetting the list, which would
        // lose the user's scroll position.
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }

    private bool Matches(LogEntry entry) => entry.Level >= MinimumLevel;

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        _incoming.Enqueue(entry);

        if (Interlocked.Exchange(ref _flushPending, 1) == 0)
        {
            _uiDispatcher.Post(Flush);
        }
    }

    partial void OnIsPausedChanged(bool value)
    {
        if (!value)
        {
            Rebuild();
        }
    }

    partial void OnMinimumLevelChanged(LogLevel value)
    {
        Rebuild();
    }

    // Replace the collection wholesale (one reset) instead of thousands of individual add/remove notifications.
    private void Rebuild()
    {
        Entries = new ObservableCollection<LogEntryViewModel>(_all.Where(Matches).Select(entry => new LogEntryViewModel(entry)));
    }

    [RelayCommand]
    private void ToggleAutoScroll()
    {
        AutoScroll = !AutoScroll;
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
    }
}
