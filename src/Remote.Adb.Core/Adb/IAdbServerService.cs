using JetBrains.Annotations;

namespace Remote.Adb.Core.Adb;

/// <summary>
/// Manages the lifecycle of the local <c>adb</c> server — the server the SSH reverse tunnel forwards to.
/// </summary>
[PublicAPI]
public interface IAdbServerService
{
    /// <summary>Raised after <see cref="RestartAsync"/> successfully brings the server back up — or after an adb command
    /// replaced a stale, version-mismatched server that refused to exit — so dependents (e.g. an open tunnel whose
    /// forward target just bounced, or dropped wireless devices) can react.</summary>
    event EventHandler ServerRestarted;

    /// <summary>
    /// Ensures the local server is running <b>and was started by the app</b> (so it rejects a remote
    /// <c>kill-server</c>): starts it if it's down, and replaces a server started outside the app (one whose process
    /// isn't the server the app last started). Returns whether a server the app started is running afterwards.
    /// </summary>
    Task<bool> EnsureOwnServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether something is listening on <paramref name="port"/>. Uses a raw TCP probe so it does <b>not</b>
    /// auto-spawn a server the way an <c>adb</c> client command would. A connect that doesn't complete promptly counts as
    /// not listening.
    /// </summary>
    Task<bool> IsRunningAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the local adb server by killing the adb process listening on the server port (only that process); returns
    /// whether the port is free afterwards.
    /// </summary>
    Task<bool> KillAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts the local adb server (kill then start), raising <see cref="ServerRestarted"/> and returning
    /// <see langword="true"/> only when both steps succeeded.
    /// </summary>
    Task<bool> RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>Ensures the local adb server is running (<c>adb start-server</c>); returns whether it's listening.</summary>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);
}
