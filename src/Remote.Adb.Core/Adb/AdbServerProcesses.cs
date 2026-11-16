using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.Adb;

/// <inheritdoc />
internal sealed class AdbServerProcesses : IAdbServerProcesses
{
    private readonly IAndroidSdk _androidSdk;
    private readonly ILogger<AdbServerProcesses> _logger;

    public AdbServerProcesses(IAndroidSdk androidSdk, ILogger<AdbServerProcesses> logger)
    {
        _androidSdk = androidSdk;
        _logger = logger;
    }

    /// <summary>How long to wait for a killed server to exit.</summary>
    internal TimeSpan KillTimeout { get; init; } = TimeSpan.FromSeconds(3);

    // Only processes running the configured adb binary — never another SDK's adb or an unrelated tool's.
    private List<Process> FindServers()
    {
        var adbPath = _androidSdk.AdbPath;
        var servers = new List<Process>();

        if (!Path.IsPathRooted(adbPath))
        {
            // adb was found on PATH rather than in a known SDK, so there's no path to match against; killing by name
            // alone could hit someone else's server.
            _logger.LogWarning("Can't identify the adb server process: adb isn't resolved to a full path ({AdbPath}). Configure the Android SDK in Settings.", adbPath);
            return servers;
        }

        var target = Path.GetFullPath(adbPath);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target)))
        {
            if (IsServer(process, target, comparison))
            {
                servers.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return servers;
    }

    private bool IsServer(Process process, string target, StringComparison comparison)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (path is null || !string.Equals(Path.GetFullPath(path), target, comparison))
            {
                return false;
            }

            // On Linux, tell the long-lived server ("adb ... fork-server server") from short-lived client commands.
            return !OperatingSystem.IsLinux() || ReadCommandLine(process.Id).Contains("fork-server", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // Elevated, another user's, or already gone: it can't be identified, so leave it alone.
            _logger.LogDebug(exception, "Skipping adb process {ProcessId}: couldn't read its path.", SafeId(process));
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> KillAsync(CancellationToken cancellationToken = default)
    {
        var servers = FindServers();
        try
        {
            if (servers.Count == 0)
            {
                return Path.IsPathRooted(_androidSdk.AdbPath);
            }

            foreach (var server in servers)
            {
                _logger.LogInformation("Stopping the local adb server (process {ProcessId}).", server.Id);
                server.KillTree();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(KillTimeout);

            try
            {
                await Task.WhenAll(servers.Select(server => server.WaitForExitAsync(timeout.Token)));
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("The local adb server didn't exit within {Timeout}.", KillTimeout);
                return false;
            }
        }
        finally
        {
            foreach (var server in servers)
            {
                server.Dispose();
            }
        }
    }

    private static string ReadCommandLine(int processId)
    {
        // /proc/<pid>/cmdline separates arguments with NULs.
        var path = $"/proc/{processId}/cmdline";
        return File.Exists(path) ? File.ReadAllText(path).Replace('\0', ' ') : string.Empty;
    }

    private static int? SafeId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
