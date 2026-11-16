using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.UnitTests.Fakes;

/// <summary>An <see cref="ILogger{T}"/> that records every message at or above <see cref="MinimumLevel"/>.</summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<RecordedLog> Entries { get; } = [];

    public LogLevel MinimumLevel { get; init; } = LogLevel.Trace;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            Entries.Add(new RecordedLog(logLevel, eventId, formatter(state, exception), exception));
        }
    }
}

/// <summary>One message captured by <see cref="RecordingLogger{T}"/>.</summary>
public sealed record RecordedLog(LogLevel Level, EventId EventId, string Message, Exception? Exception);
