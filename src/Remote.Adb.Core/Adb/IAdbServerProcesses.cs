namespace Remote.Adb.Core.Adb;

/// <summary>
/// Stops the local adb server by killing its process. The app can't use <c>adb kill-server</c>: the servers it starts
/// reject kills (see <see cref="IAdbRunner.EnvironmentOverrides"/>).
/// </summary>
internal interface IAdbServerProcesses
{
    /// <summary>
    /// Kills the adb server listening on <paramref name="port"/> — only that process, after checking it is an
    /// <c>adb</c>, never other adb clients or another port's server — waits for it to exit and confirms the port is free.
    /// Returns <see langword="true"/> when nothing listens on the port afterwards (including when nothing was running),
    /// <see langword="false"/> when the listener couldn't be identified, isn't adb, didn't exit in time, or the port is
    /// still taken.
    /// </summary>
    Task<bool> KillAsync(int port, CancellationToken cancellationToken = default);
}
