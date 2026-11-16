using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Logging;

/// <summary>
/// <see cref="ILogStore"/> backed by a lock-guarded ring buffer: once full, each new entry evicts the oldest.
/// </summary>
internal sealed class LogStore : ILogStore
{
    public const int DefaultCapacity = 2000;

    private readonly LogEntry[] _buffer;
    private int _count;
    private readonly Lock _gate = new();
    private long _lastSequence;
    private int _start;

    public LogStore() : this(DefaultCapacity)
    {
    }

    internal LogStore(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _buffer = new LogEntry[capacity];
    }

    public event EventHandler<LogEntry>? EntryAdded;

    /// <summary>Appends an entry, assigning its sequence and timestamp, then raises <see cref="EntryAdded"/>.</summary>
    public void Add(LogLevel level, string category, EventId eventId, string message, string? exception, IReadOnlyDictionary<string, object?>? properties = null)
    {
        LogEntry entry;

        lock (_gate)
        {
            entry = new LogEntry(++_lastSequence, DateTimeOffset.Now, level, category, eventId, message, exception, properties);

            if (_count < _buffer.Length)
            {
                _buffer[(_start + _count) % _buffer.Length] = entry;
                _count++;
            }
            else
            {
                _buffer[_start] = entry;
                _start = (_start + 1) % _buffer.Length;
            }
        }

        // Raised outside the lock so a slow handler can't block other loggers.
        EntryAdded?.Invoke(this, entry);
    }

    /// <inheritdoc />
    public long Clear()
    {
        lock (_gate)
        {
            Array.Clear(_buffer);
            _count = 0;
            _start = 0;
            return _lastSequence;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            var entries = new LogEntry[_count];
            for (var index = 0; index < _count; index++)
            {
                entries[index] = _buffer[(_start + index) % _buffer.Length];
            }

            return entries;
        }
    }
}
