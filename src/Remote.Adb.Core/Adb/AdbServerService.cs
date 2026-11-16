using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Adb;

/// <inheritdoc />
public sealed class AdbServerService : IAdbServerService
{
    private readonly IAdbRunner _adb;
    private readonly ILogger<AdbServerService> _logger;
    private readonly IAdbServerProcesses _serverProcesses;

    public AdbServerService(IAdbRunner adb, IAdbServerProcesses serverProcesses, ILogger<AdbServerService> logger)
    {
        _adb = adb;
        _serverProcesses = serverProcesses;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler? ServerRestarted;

    /// <inheritdoc />
    public async Task<bool> EnsureOwnServerAsync(CancellationToken cancellationToken = default)
    {
        // A server someone else started (Android Studio, a terminal) doesn't carry ADB_REJECT_KILL_SERVER, so a remote adb
        // could kill it through the tunnel. Replace it with one started through the app.
        if (!_adb.StartedServer && await IsRunningAsync(_adb.ServerPort, cancellationToken))
        {
            _logger.LogInformation("Replacing an adb server started outside the app with one that rejects remote kill-server requests.");
            return await RestartAsync(cancellationToken);
        }

        return await StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsRunningAsync(int port, CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535)
        {
            return false;
        }

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            return true;
        }
        catch (SocketException)
        {
            // Connection refused / unreachable — the server is not listening. A cancellation (OperationCanceledException)
            // is deliberately left to propagate so the health monitor stops probing rather than counting a failure.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> KillAsync(CancellationToken cancellationToken = default)
    {
        // Not `adb kill-server`: the servers the app starts reject kill requests (see IAdbRunner), so the process is
        // stopped directly.
        var killed = await _serverProcesses.KillAsync(cancellationToken);
        if (killed)
        {
            _adb.ForgetServer();
        }

        return killed;
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
        return result.Success && await IsRunningAsync(_adb.ServerPort, cancellationToken);
    }
}
