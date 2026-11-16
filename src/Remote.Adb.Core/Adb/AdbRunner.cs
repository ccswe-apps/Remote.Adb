using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <inheritdoc />
public sealed class AdbRunner : IAdbRunner
{
    private const int DefaultServerPort = 5037;

    private static readonly FrozenDictionary<string, string> Overrides = new Dictionary<string, string>
    {
        ["ADB_REJECT_KILL_SERVER"] = "1",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private const string ServerPortVariable = "ANDROID_ADB_SERVER_PORT";

    // adb prints this (to stderr) whenever a command had to start the server first.
    private const string StartedDaemonMessage = "daemon started successfully";

    private readonly IAndroidSdk _androidSdk;
    private readonly ILogger<AdbRunner> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly IAdbServerProcesses _serverProcesses;
    private volatile bool _startedServer;

    public AdbRunner(IProcessRunner processRunner, IAndroidSdk androidSdk, IAdbServerProcesses serverProcesses, ILogger<AdbRunner> logger)
    {
        _processRunner = processRunner;
        _androidSdk = androidSdk;
        _serverProcesses = serverProcesses;
        _logger = logger;
        ServerPort = ParsePort(Environment.GetEnvironmentVariable(ServerPortVariable));
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> EnvironmentOverrides => Overrides;

    /// <inheritdoc />
    public int ServerPort { get; }

    /// <inheritdoc />
    public bool StartedServer => _startedServer;

    /// <inheritdoc />
    public void ForgetServer() => _startedServer = false;

    // A client that doesn't match the running server's version asks it to exit so it can start its own; a server that
    // rejects kills refuses, leaving every command failing until the stale server is gone.
    internal static bool IsRejectedServerReplacement(ProcessResult result) =>
        !result.Success
        && (result.StandardError.Contains("doesn't match this client", StringComparison.Ordinal)
            || result.StandardError.Contains("rejected by remote server", StringComparison.Ordinal));

    internal static int ParsePort(string? value) =>
        int.TryParse(value, out var port) && port is > 0 and <= 65535 ? port : DefaultServerPort;

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput = null, CancellationToken cancellationToken = default)
    {
        var result = await RunOnceAsync(arguments, standardInput, cancellationToken);
        if (!IsRejectedServerReplacement(result))
        {
            return result;
        }

        _logger.LogWarning("The running adb server doesn't match this adb ({AdbPath}) and refused to exit; stopping it and retrying.", _androidSdk.AdbPath);
        if (!await _serverProcesses.KillAsync(cancellationToken))
        {
            return result;
        }

        ForgetServer();
        return await RunOnceAsync(arguments, standardInput, cancellationToken);
    }

    private async Task<ProcessResult> RunOnceAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(_androidSdk.AdbPath, arguments, standardInput, Overrides, cancellationToken);

        if (result.StandardError.Contains(StartedDaemonMessage, StringComparison.Ordinal))
        {
            _startedServer = true;
        }

        return result;
    }
}
