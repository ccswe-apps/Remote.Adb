using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Logging;

/// <summary>An <see cref="ILogger"/> that writes each message into a <see cref="LogStore"/>.</summary>
internal sealed class LogStoreLogger : ILogger
{
    private readonly string _category;
    private readonly LogStore _store;

    public LogStoreLogger(string category, LogStore store)
    {
        _category = category;
        _store = store;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    // Level filtering is configured on the logging builder (see LoggingBuilderExtensions); anything that reaches
    // this provider is wanted.
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        _store.Add(logLevel, _category, eventId, formatter(state, exception), exception?.ToString());
    }
}
