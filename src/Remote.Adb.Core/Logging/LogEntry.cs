using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Logging;

/// <summary>
/// One captured log message. <see cref="Sequence"/> is assigned by the <see cref="ILogStore"/> and increases
/// monotonically, so a reader can de-duplicate a snapshot against entries it also received as events.
/// <see cref="Properties"/> holds the structured values of a templated message (e.g. a command's <c>ExitCode</c>).
/// </summary>
[PublicAPI]
public sealed record LogEntry(
    long Sequence,
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    string? Exception,
    IReadOnlyDictionary<string, object?>? Properties = null)
{
    /// <summary>Whether this entry records an external command the app ran (see <see cref="LogEvents"/>).</summary>
    public bool IsCommand => LogEvents.IsCommand(EventId);
}
