namespace Remote.Adb.Core.Adb;

/// <summary>
/// Manages the lifecycle of the local <c>adb</c> server — the server the SSH reverse tunnel forwards to.
/// </summary>
public interface IAdbServerService
{
    /// <summary>Raised after <see cref="RestartAsync"/> successfully brings the server back up, so dependents (e.g. an
    /// open tunnel whose forward target just bounced, or dropped wireless devices) can react.</summary>
    event EventHandler ServerRestarted;

    /// <summary>
    /// Ensures the local server is running <b>and was started by the app</b> (so it rejects a remote
    /// <c>kill-server</c>): starts it if it's down, and replaces a server started outside the app. Returns whether a
    /// server the app started is running afterwards.
    /// </summary>
    Task<bool> EnsureOwnServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether something is listening on <paramref name="port"/>. Uses a raw TCP probe so it does <b>not</b>
    /// auto-spawn a server the way an <c>adb</c> client command would.
    /// </summary>
    Task<bool> IsRunningAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>Stops the local adb server by killing its process; returns whether it's stopped.</summary>
    Task<bool> KillAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts the local adb server (kill then start), raising <see cref="ServerRestarted"/> and returning
    /// <see langword="true"/> only when both steps succeeded.
    /// </summary>
    Task<bool> RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>Ensures the local adb server is running (<c>adb start-server</c>); returns whether it's listening.</summary>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);
}
