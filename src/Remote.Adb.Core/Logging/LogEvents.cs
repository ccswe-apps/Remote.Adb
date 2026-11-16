using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Logging;

/// <summary>
/// Event ids (and their structured property keys) for the log messages a log viewer renders specially. Command
/// events occupy the <see cref="CommandStarted"/>…<see cref="CommandOutput"/> range.
/// </summary>
public static class LogEvents
{
    public const int CommandCancelled = 1002;
    public const int CommandCompleted = 1001;
    public const int CommandLaunchFailed = 1003;
    public const int CommandOutput = 1004;

    /// <summary>The <see cref="LogEntry.Properties"/> key holding a command event's rendered command line.</summary>
    public const string CommandProperty = "Command";

    public const int CommandStarted = 1000;

    /// <summary>The <see cref="LogEntry.Properties"/> key holding a completed command's duration in milliseconds.</summary>
    public const string ElapsedMillisecondsProperty = "ElapsedMilliseconds";

    /// <summary>The <see cref="LogEntry.Properties"/> key holding a completed command's exit code.</summary>
    public const string ExitCodeProperty = "ExitCode";

    /// <summary>Whether <paramref name="eventId"/> is one of the command events.</summary>
    public static bool IsCommand(EventId eventId) => eventId.Id is >= CommandStarted and <= CommandOutput;
}
