using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Logging;

namespace Remote.Adb.Core.Common;

/// <summary>
/// Source-generated log messages for <see cref="ProcessRunner"/>. Levels are passed in because the same message is
/// Information for user-initiated work and Debug inside a <see cref="CommandLogScope.Background"/> scope.
/// </summary>
internal static partial class ProcessRunnerLog
{
    [LoggerMessage(EventId = LogEvents.CommandCancelled, Message = "{Command} cancelled after {ElapsedMilliseconds} ms")]
    public static partial void CommandCancelled(ILogger logger, LogLevel level, string command, long elapsedMilliseconds);

    [LoggerMessage(EventId = LogEvents.CommandCompleted, Message = "{Command} exited with code {ExitCode} in {ElapsedMilliseconds} ms")]
    public static partial void CommandCompleted(ILogger logger, LogLevel level, string command, int exitCode, long elapsedMilliseconds);

    [LoggerMessage(EventId = LogEvents.CommandLaunchFailed, Message = "{Command} failed to start")]
    public static partial void CommandLaunchFailed(ILogger logger, LogLevel level, string command, Exception exception);

    [LoggerMessage(EventId = LogEvents.CommandOutput, Message = "{Command} {Stream}:\n{Output}")]
    public static partial void CommandOutput(ILogger logger, LogLevel level, string command, string stream, string output);

    [LoggerMessage(EventId = LogEvents.CommandStarted, Message = "Running {Command}")]
    public static partial void CommandStarted(ILogger logger, LogLevel level, string command);
}
