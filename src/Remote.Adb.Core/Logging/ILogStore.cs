using JetBrains.Annotations;

namespace Remote.Adb.Core.Logging;

/// <summary>
/// An in-memory, bounded buffer of the application's recent log output, fed by <see cref="LogStoreLoggerProvider"/>.
/// Code keeps logging through <c>ILogger&lt;T&gt;</c>; this is the read side for anything that displays the log.
/// </summary>
[PublicAPI]
public interface ILogStore
{
    /// <summary>
    /// Raised after an entry is added, on the thread that logged it. Handlers must not log (that would recurse)
    /// and should hand the entry off quickly, e.g. by queueing it for the UI thread.
    /// </summary>
    event EventHandler<LogEntry>? EntryAdded;

    /// <summary>
    /// Removes every buffered entry and returns the highest <see cref="LogEntry.Sequence"/> assigned so far, so a
    /// reader can discard queued events that predate the clear.
    /// </summary>
    long Clear();

    /// <summary>Returns the buffered entries, oldest first.</summary>
    IReadOnlyList<LogEntry> Snapshot();
}
