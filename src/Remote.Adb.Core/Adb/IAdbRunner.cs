using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <summary>
/// The one place the app runs <c>adb</c>. Every invocation goes through here so each one carries the same environment —
/// notably <c>ADB_REJECT_KILL_SERVER=1</c>: any adb command can start the server, and a server started with it refuses a
/// <c>kill-server</c> sent through the SSH tunnel by a version-mismatched remote adb. Services must not run adb via
/// <see cref="IProcessRunner"/> directly (a test guards this).
/// </summary>
internal interface IAdbRunner
{
    /// <summary>The environment variables applied to every adb invocation.</summary>
    IReadOnlyDictionary<string, string> EnvironmentOverrides { get; }

    /// <summary>
    /// The local adb server's port: <c>ANDROID_ADB_SERVER_PORT</c> when set, otherwise adb's default 5037. Not to be
    /// confused with the tunnel's configurable local port.
    /// </summary>
    int ServerPort { get; }

    /// <summary>
    /// Whether the running adb server was started by this app instance (so it carries <see cref="EnvironmentOverrides"/>
    /// and rejects remote kills). Set when an adb command reports it started the daemon; cleared by
    /// <see cref="ForgetServer"/> when the app stops the server.
    /// </summary>
    bool StartedServer { get; }

    /// <summary>Records that the server this app started is gone (it was killed), so <see cref="StartedServer"/> is false.</summary>
    void ForgetServer();

    /// <summary>
    /// Runs <c>adb</c> with <paramref name="arguments"/>. If a mismatched-version server can't be replaced because it
    /// rejects the client's kill (see <see cref="EnvironmentOverrides"/>), the server is stopped directly and the command
    /// retried once.
    /// </summary>
    Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput = null, CancellationToken cancellationToken = default);
}
