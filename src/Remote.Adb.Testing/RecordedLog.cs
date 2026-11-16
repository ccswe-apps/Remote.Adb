using Microsoft.Extensions.Logging;

namespace Remote.Adb.Testing;

/// <summary>One message captured by <see cref="RecordingLogger{T}"/>.</summary>
public sealed record RecordedLog(LogLevel Level, EventId EventId, string Message, Exception? Exception);
