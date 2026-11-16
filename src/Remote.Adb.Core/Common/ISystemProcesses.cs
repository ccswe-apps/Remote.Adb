namespace Remote.Adb.Core.Common;

/// <summary>
/// Inspects and stops processes the app didn't start, by ID. A seam so code that stops a process found some other way
/// (e.g. the one listening on a port) is unit-testable without touching real processes.
/// </summary>
internal interface ISystemProcesses
{
    /// <summary>
    /// The name of process <paramref name="processId"/> (no extension, e.g. <c>adb</c>), or <see langword="null"/> when
    /// it isn't running or can't be inspected.
    /// </summary>
    string? GetName(int processId);

    /// <summary>
    /// Kills just process <paramref name="processId"/> (not its descendants) and waits up to <paramref name="timeout"/>
    /// for it to exit. Returns <see langword="true"/> once it's gone (including when it already was),
    /// <see langword="false"/> when it couldn't be killed or didn't exit in time.
    /// </summary>
    Task<bool> KillAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default);
}
