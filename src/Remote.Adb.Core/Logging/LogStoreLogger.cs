using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Logging;

/// <summary>An <see cref="ILogger"/> that writes each message into a <see cref="LogStore"/>.</summary>
internal sealed class LogStoreLogger : ILogger
{
    private const string OriginalFormatKey = "{OriginalFormat}";

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

        _store.Add(logLevel, _category, eventId, formatter(state, exception), exception?.ToString(), CaptureProperties(state));
    }

    // Templated messages (LoggerMessage, LogInformation("{X}", x)) expose their values as key/value pairs; keep them,
    // minus the raw template, so readers can use the values without parsing the formatted text.
    private static Dictionary<string, object?>? CaptureProperties<TState>(TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> pairs || pairs.Count <= 1)
        {
            return null;
        }

        var properties = new Dictionary<string, object?>(pairs.Count, StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            if (key != OriginalFormatKey)
            {
                properties[key] = value;
            }
        }

        return properties;
    }
}
