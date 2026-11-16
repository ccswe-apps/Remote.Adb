using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <inheritdoc />
internal sealed class AdbServerService : IAdbServerService
{
    private readonly IAdbRunner _adb;
    private readonly IListeningProcessFinder _listeningProcesses;
    private readonly ILogger<AdbServerService> _logger;
    private int? _ownedServerProcessId;
    private readonly Lock _ownershipGate = new();
    private bool _ownershipPending;
    private readonly IPortProbe _portProbe;
    private readonly IAdbServerProcesses _serverProcesses;

    public AdbServerService(IAdbRunner adb, IAdbServerProcesses serverProcesses, IListeningProcessFinder listeningProcesses, IPortProbe portProbe, ILogger<AdbServerService> logger)
    {
        _adb = adb;
        _serverProcesses = serverProcesses;
        _listeningProcesses = listeningProcesses;
        _portProbe = portProbe;
        _logger = logger;

        // Both are singletons, so the subscriptions live as long as the runner does.
        _adb.ServerStarted += OnServerStarted;
        _adb.ServerReplaced += OnServerReplaced;
    }

    /// <inheritdoc />
    public event EventHandler? ServerRestarted;

    /// <inheritdoc />
    public async Task<bool> EnsureOwnServerAsync(CancellationToken cancellationToken = default)
    {
        // A server someone else started (Android Studio, a terminal) doesn't carry ADB_REJECT_KILL_SERVER, so a remote adb
        // could kill it through the tunnel. Replace it with one started through the app.
        if (await IsRunningAsync(_adb.ServerPort, cancellationToken) && !await IsOwnServerAsync(cancellationToken))
        {
            _logger.LogInformation("Replacing an adb server started outside the app with one that rejects remote kill-server requests.");
            return await RestartAsync(cancellationToken);
        }

        return await StartAsync(cancellationToken);
    }

    // Whether the server listening now is the one the app started. Ownership is tracked by process, not a flag, so a
    // server that died and was replaced by someone else's isn't mistaken for the app's. A start reported by the runner
    // is pinned to whichever process is listening the first time it's checked after that start.
    private async Task<bool> IsOwnServerAsync(CancellationToken cancellationToken)
    {
        var processId = await _listeningProcesses.FindAsync(_adb.ServerPort, cancellationToken);
        if (processId is null)
        {
            return false;
        }

        lock (_ownershipGate)
        {
            if (_ownershipPending)
            {
                _ownedServerProcessId = processId;
                _ownershipPending = false;
            }

            return _ownedServerProcessId == processId;
        }
    }

    /// <inheritdoc />
    public Task<bool> IsRunningAsync(int port, CancellationToken cancellationToken = default) =>
        _portProbe.IsListeningAsync(port, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> KillAsync(CancellationToken cancellationToken = default)
    {
        // Not `adb kill-server`: the servers the app starts reject kill requests (see IAdbRunner), so the process is
        // stopped directly.
        var killed = await _serverProcesses.KillAsync(_adb.ServerPort, cancellationToken);
        if (killed)
        {
            lock (_ownershipGate)
            {
                _ownedServerProcessId = null;
                _ownershipPending = false;
            }
        }

        return killed;
    }

    // The runner bounced a stale server that refused to exit; to dependents that's a restart.
    private void OnServerReplaced(object? sender, EventArgs e) => ServerRestarted?.Invoke(this, EventArgs.Empty);

    private void OnServerStarted(object? sender, EventArgs e)
    {
        lock (_ownershipGate)
        {
            _ownedServerProcessId = null;
            _ownershipPending = true;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RestartAsync(CancellationToken cancellationToken = default)
    {
        if (!await KillAsync(cancellationToken) || !await StartAsync(cancellationToken))
        {
            return false;
        }

        ServerRestarted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        // A failure is logged by the process runner, with adb's output.
        var result = await _adb.RunAsync(["start-server"], cancellationToken: cancellationToken);
        if (!result.Success || !await IsRunningAsync(_adb.ServerPort, cancellationToken))
        {
            return false;
        }

        bool pending;
        lock (_ownershipGate)
        {
            pending = _ownershipPending;
        }

        // Pin a server this start launched to its process now, while it's certainly the one listening.
        if (pending)
        {
            await IsOwnServerAsync(cancellationToken);
        }

        return true;
    }
}
