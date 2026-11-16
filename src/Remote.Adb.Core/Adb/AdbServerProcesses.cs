using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <inheritdoc />
internal sealed class AdbServerProcesses : IAdbServerProcesses
{
    private const string AdbProcessName = "adb";

    private readonly IListeningProcessFinder _listeningProcesses;
    private readonly ILogger<AdbServerProcesses> _logger;
    private readonly IPortProbe _portProbe;
    private readonly ISystemProcesses _processes;

    public AdbServerProcesses(IListeningProcessFinder listeningProcesses, ISystemProcesses processes, IPortProbe portProbe, ILogger<AdbServerProcesses> logger)
    {
        _listeningProcesses = listeningProcesses;
        _processes = processes;
        _portProbe = portProbe;
        _logger = logger;
    }

    /// <summary>How long to wait for a killed server to exit.</summary>
    internal TimeSpan KillTimeout { get; init; } = TimeSpan.FromSeconds(3);

    // Nothing to kill (or the listener vanished before it could be inspected): stopped means the port is free.
    private async Task<bool> IsPortFreeAsync(int port, CancellationToken cancellationToken)
    {
        if (!await _portProbe.IsListeningAsync(port, cancellationToken))
        {
            return true;
        }

        _logger.LogWarning("Something is listening on the adb server port {Port}, but its process can't be identified, so it can't be stopped.", port);
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> KillAsync(int port, CancellationToken cancellationToken = default)
    {
        var processId = await _listeningProcesses.FindAsync(port, cancellationToken);
        if (processId is null)
        {
            return await IsPortFreeAsync(port, cancellationToken);
        }

        var name = _processes.GetName(processId.Value);
        if (name is null)
        {
            return await IsPortFreeAsync(port, cancellationToken);
        }

        if (!string.Equals(name, AdbProcessName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("The adb server port {Port} is held by {ProcessName} (process {ProcessId}), not adb; leaving it alone.", port, name, processId);
            return false;
        }

        _logger.LogInformation("Stopping the local adb server (process {ProcessId}) on port {Port}.", processId, port);
        if (!await _processes.KillAsync(processId.Value, KillTimeout, cancellationToken))
        {
            _logger.LogWarning("The local adb server (process {ProcessId}) didn't exit within {Timeout}.", processId, KillTimeout);
            return false;
        }

        if (await _portProbe.IsListeningAsync(port, cancellationToken))
        {
            _logger.LogWarning("The adb server port {Port} is still taken after stopping process {ProcessId}.", port, processId);
            return false;
        }

        return true;
    }
}
