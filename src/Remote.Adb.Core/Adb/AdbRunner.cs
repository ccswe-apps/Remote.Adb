using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <inheritdoc />
internal sealed class AdbRunner : IAdbRunner
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
    private readonly SemaphoreSlim _replacementGate = new(1, 1);
    private int _replacements;
    private readonly IAdbServerProcesses _serverProcesses;

    public AdbRunner(IProcessRunner processRunner, IAndroidSdk androidSdk, IAdbServerProcesses serverProcesses, ILogger<AdbRunner> logger)
    {
        _processRunner = processRunner;
        _androidSdk = androidSdk;
        _serverProcesses = serverProcesses;
        _logger = logger;
        ServerPort = ParsePort(Environment.GetEnvironmentVariable(ServerPortVariable));
    }

    /// <inheritdoc />
    public event EventHandler? ServerReplaced;

    /// <inheritdoc />
    public event EventHandler? ServerStarted;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> EnvironmentOverrides => Overrides;

    /// <inheritdoc />
    public int ServerPort { get; }

    // A client that doesn't match the running server's version asks it to exit so it can start its own; a server that
    // rejects kills refuses, leaving every command failing until the stale server is gone.
    internal static bool IsRejectedServerReplacement(ProcessResult result) =>
        !result.Success
        && (result.StandardError.Contains("doesn't match this client", StringComparison.Ordinal)
            || result.StandardError.Contains("rejected by remote server", StringComparison.Ordinal));

    internal static int ParsePort(string? value) =>
        int.TryParse(value, out var port) && port is > 0 and <= 65535 ? port : DefaultServerPort;

    // Stops the stale server — once, however many commands hit it concurrently: a command that failed against a server
    // another command has since replaced just retries, rather than killing the fresh server.
    private async Task<Replacement> ReplaceServerAsync(int generation, CancellationToken cancellationToken)
    {
        await _replacementGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _replacements) != generation)
            {
                return Replacement.AlreadyReplaced;
            }

            _logger.LogWarning("The running adb server doesn't match this adb ({AdbPath}) and refused to exit; stopping it and retrying.", _androidSdk.AdbPath);
            if (!await _serverProcesses.KillAsync(ServerPort, cancellationToken))
            {
                return Replacement.Failed;
            }

            Interlocked.Increment(ref _replacements);
            return Replacement.Replaced;
        }
        finally
        {
            _replacementGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput = null, CancellationToken cancellationToken = default)
    {
        var generation = Volatile.Read(ref _replacements);
        var result = await RunOnceAsync(arguments, standardInput, cancellationToken);
        if (!IsRejectedServerReplacement(result))
        {
            return result;
        }

        var replacement = await ReplaceServerAsync(generation, cancellationToken);
        if (replacement == Replacement.Failed)
        {
            return result;
        }

        var retried = await RunOnceAsync(arguments, standardInput, cancellationToken);
        if (replacement == Replacement.Replaced)
        {
            ServerReplaced?.Invoke(this, EventArgs.Empty);
        }

        return retried;
    }

    private async Task<ProcessResult> RunOnceAsync(IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken)
    {
        // A cancelled adb client is killed alone: the server it may have just started is its child, and must survive.
        var result = await _processRunner.RunAsync(_androidSdk.AdbPath, arguments, standardInput, Overrides, killProcessTree: false, cancellationToken);

        if (result.StandardError.Contains(StartedDaemonMessage, StringComparison.Ordinal))
        {
            ServerStarted?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    private enum Replacement
    {
        Failed,
        AlreadyReplaced,
        Replaced,
    }
}
