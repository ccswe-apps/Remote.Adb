using System.ComponentModel;
using System.Diagnostics;

namespace Remote.Adb.Core.Common;

/// <inheritdoc />
internal sealed class SystemProcesses : ISystemProcesses
{
    private readonly TimeProvider _timeProvider;

    public SystemProcesses(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string? GetName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Not running (ArgumentException), already exited, or not inspectable.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> KillAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return true;
        }

        using (process)
        {
            process.Terminate(entireProcessTree: false);

            var (exited, _) = await Timeouts.TryWithTimeoutAsync(
                async token =>
                {
                    await process.WaitForExitAsync(token);
                    return true;
                },
                timeout,
                cancellationToken,
                _timeProvider);

            return exited;
        }
    }
}
