using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Logging;

/// <summary>
/// Event ids for the structured log messages a log viewer renders specially. Command events occupy the
/// <see cref="CommandStarted"/>…<see cref="CommandOutput"/> range.
/// </summary>
public static class LogEvents
{
    public const int CommandCancelled = 1002;
    public const int CommandCompleted = 1001;
    public const int CommandLaunchFailed = 1003;
    public const int CommandOutput = 1004;
    public const int CommandStarted = 1000;

    /// <summary>Whether <paramref name="eventId"/> is one of the command events.</summary>
    public static bool IsCommand(EventId eventId) => eventId.Id is >= CommandStarted and <= CommandOutput;
}
