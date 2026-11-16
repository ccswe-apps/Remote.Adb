using Remote.Adb.Core.Tunnel;

namespace Remote.Adb.Console;

/// <summary>
/// <c>tunnel [host]</c>: opens the reverse tunnel and keeps it up until the user cancels (Ctrl+C) or the tunnel ends
/// on its own (it faults, possibly after failed auto-reconnects). Exits non-zero whenever the tunnel faulted.
/// </summary>
internal static class TunnelCommand
{
    /// <summary>Runs the command and returns the process exit code.</summary>
    /// <param name="tunnel">The tunnel service.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="output">Where status lines go.</param>
    /// <param name="cancellationToken">Cancelled by Ctrl+C — during the connect as well as while connected.</param>
    public static async Task<int> RunAsync(ITunnelService tunnel, string host, TextWriter output, CancellationToken cancellationToken)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var faulted = false;

        void OnStatusChanged(object? sender, TunnelStatus status)
        {
            output.WriteLine(status.Message is null ? $"[tunnel] {status.State}" : $"[tunnel] {status.State}: {status.Message}");

            if (status.State == TunnelState.Faulted)
            {
                Volatile.Write(ref faulted, true);
            }

            if (status.State is TunnelState.Faulted or TunnelState.Disconnected)
            {
                ended.TrySetResult();
            }
        }

        output.WriteLine($"Opening reverse tunnel to {host}. Press Ctrl+C to close.");
        tunnel.StatusChanged += OnStatusChanged;
        try
        {
            try
            {
                await tunnel.ConnectAsync(host, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ctrl+C before the connect even started.
            }

            if (tunnel.Status.State is TunnelState.Connected or TunnelState.Reconnecting)
            {
                try
                {
                    await ended.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }

                tunnel.StatusChanged -= OnStatusChanged;
                await tunnel.DisconnectAsync(CancellationToken.None);
                output.WriteLine("Tunnel closed.");
            }
        }
        finally
        {
            tunnel.StatusChanged -= OnStatusChanged;
        }

        return Volatile.Read(ref faulted) ? 1 : 0;
    }
}
