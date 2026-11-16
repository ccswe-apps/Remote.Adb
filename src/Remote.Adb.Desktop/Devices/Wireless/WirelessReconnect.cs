using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Tunnel;

namespace Remote.Adb.Desktop.Devices.Wireless;

/// <summary>
/// Keeps hand-connected wireless devices connected. Restarting the local adb server drops every network device, so
/// saved addresses are reconnected at launch, after the server is restarted, and whenever the tunnel comes (back)
/// up — the tunnel's own recovery restarts a dead local server. Requests that pile up while a reconnect is running
/// collapse into one follow-up pass.
/// </summary>
public sealed class WirelessReconnect : BackgroundService
{
    private readonly IAdbServerService _adbServer;
    private readonly ILogger<WirelessReconnect> _logger;
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ITunnelService _tunnel;
    private readonly IWirelessAdbService _wireless;

    public WirelessReconnect(IWirelessAdbService wireless, IAdbServerService adbServer, ITunnelService tunnel, ILogger<WirelessReconnect> logger)
    {
        _wireless = wireless;
        _adbServer = adbServer;
        _tunnel = tunnel;
        _logger = logger;
    }

    public override void Dispose()
    {
        _adbServer.ServerRestarted -= OnServerRestarted;
        _tunnel.StatusChanged -= OnTunnelStatusChanged;
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _adbServer.ServerRestarted += OnServerRestarted;
        _tunnel.StatusChanged += OnTunnelStatusChanged;

        Request();

        try
        {
            await foreach (var _ in _requests.Reader.ReadAllAsync(stoppingToken))
            {
                await ReconnectAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private void OnServerRestarted(object? sender, EventArgs e) => Request();

    private void OnTunnelStatusChanged(object? sender, TunnelStatus status)
    {
        if (status.State == TunnelState.Connected)
        {
            Request();
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _wireless.ReconnectSavedAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // adb missing or similar: nothing to do until the next trigger; the Devices page surfaces adb problems.
            _logger.LogWarning(exception, "Reconnecting saved wireless devices failed.");
        }
    }

    private void Request() => _requests.Writer.TryWrite(true);
}
