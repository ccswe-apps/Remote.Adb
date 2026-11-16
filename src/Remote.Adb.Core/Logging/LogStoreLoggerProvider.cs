using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Logging;

/// <summary>Routes application logging into the shared <see cref="LogStore"/>.</summary>
[ProviderAlias("LogStore")]
internal sealed class LogStoreLoggerProvider : ILoggerProvider
{
    private readonly LogStore _store;

    public LogStoreLoggerProvider(LogStore store)
    {
        _store = store;
    }

    public ILogger CreateLogger(string categoryName) => new LogStoreLogger(categoryName, _store);

    public void Dispose()
    {
    }
}
