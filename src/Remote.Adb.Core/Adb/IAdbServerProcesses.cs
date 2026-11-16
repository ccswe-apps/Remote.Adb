namespace Remote.Adb.Core.Adb;

/// <summary>
/// Stops the local adb server by killing its process. The app can't use <c>adb kill-server</c>: the servers it starts
/// reject kills (see <see cref="IAdbRunner.EnvironmentOverrides"/>).
/// </summary>
internal interface IAdbServerProcesses
{
    /// <summary>
    /// Kills the adb server process(es) belonging to the configured adb and waits for them to exit. Returns
    /// <see langword="true"/> when none remain (including when none were running), <see langword="false"/> when one
    /// couldn't be identified or didn't exit in time.
    /// </summary>
    Task<bool> KillAsync(CancellationToken cancellationToken = default);
}
