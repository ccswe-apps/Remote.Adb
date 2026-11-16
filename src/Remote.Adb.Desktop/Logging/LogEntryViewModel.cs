using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Logging;

namespace Remote.Adb.Desktop.Logging;

/// <summary>One row of the log panel: a display projection of a captured <see cref="LogEntry"/>.</summary>
public sealed class LogEntryViewModel
{
    public LogEntryViewModel(LogEntry entry)
    {
        Entry = entry;
    }

    /// <summary>The category's last segment (the type name), e.g. <c>ProcessRunner</c>.</summary>
    public string Category
    {
        get
        {
            var category = Entry.Category;
            var dot = category.LastIndexOf('.');
            return dot < 0 ? category : category[(dot + 1)..];
        }
    }

    public LogEntry Entry { get; }

    public bool IsCommand => Entry.IsCommand;

    public bool IsError => Entry.Level >= LogLevel.Error;

    public bool IsWarning => Entry.Level == LogLevel.Warning;

    public string LevelLabel => Entry.Level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => string.Empty,
    };

    /// <summary>The message, followed by the exception's details when one was logged.</summary>
    public string Message => Entry.Exception is null ? Entry.Message : $"{Entry.Message}{Environment.NewLine}{Entry.Exception}";

    public string Time => Entry.Timestamp.ToString("HH:mm:ss.fff");

    /// <summary>A plain-text line for copying to the clipboard.</summary>
    public override string ToString() => $"{Time} {LevelLabel} {Category}: {Message}";
}
