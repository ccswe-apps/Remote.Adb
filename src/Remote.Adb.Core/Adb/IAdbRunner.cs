using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <summary>
/// The one place the app runs <c>adb</c>. Every invocation goes through here so each one carries the same environment —
/// notably <c>ADB_REJECT_KILL_SERVER=1</c>: any adb command can start the server, and a server started with it refuses a
/// <c>kill-server</c> sent through the SSH tunnel by a version-mismatched remote adb. Services must not run adb via
/// <see cref="IProcessRunner"/> directly (a test guards this). Which server the app owns is tracked by
/// <see cref="IAdbServerService"/>, from the events raised here.
/// </summary>
internal interface IAdbRunner
{
    /// <summary>
    /// Raised after <see cref="RunAsync"/> stopped a mismatched-version server that refused to be replaced and retried
    /// its command — the local server was bounced, so dependents should react as to a restart.
    /// </summary>
    event EventHandler? ServerReplaced;

    /// <summary>
    /// Raised when an adb command reports it started the local server (adb prints <c>daemon started successfully</c>):
    /// that server was started by this app, so it carries <see cref="EnvironmentOverrides"/> and rejects remote kills.
    /// </summary>
    event EventHandler? ServerStarted;

    /// <summary>The environment variables applied to every adb invocation.</summary>
    IReadOnlyDictionary<string, string> EnvironmentOverrides { get; }

    /// <summary>
    /// The local adb server's port: <c>ANDROID_ADB_SERVER_PORT</c> when set, otherwise adb's default 5037. Not to be
    /// confused with the tunnel's configurable local port.
    /// </summary>
    int ServerPort { get; }

    /// <summary>
    /// Runs <c>adb</c> with <paramref name="arguments"/>. If a mismatched-version server can't be replaced because it
    /// rejects the client's kill (see <see cref="EnvironmentOverrides"/>), the server is stopped directly and the command
    /// retried once. A cancellation kills the adb client only — never the server it may just have started.
    /// </summary>
    Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput = null, CancellationToken cancellationToken = default);
}
