using System.Globalization;
using Remote.Adb.Core.Logging;

namespace Remote.Adb.Core.Common;

/// <summary>
/// Finds a port's listener on macOS with <c>lsof -nP -iTCP:&lt;port&gt; -sTCP:LISTEN -t</c>, which prints the listening
/// process IDs one per line.
/// </summary>
internal sealed class MacListeningProcessFinder : IListeningProcessFinder
{
    // /usr/sbin isn't always on a GUI app's PATH.
    private const string Lsof = "/usr/sbin/lsof";

    private readonly IProcessRunner _processRunner;

    public MacListeningProcessFinder(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    /// <inheritdoc />
    public async Task<int?> FindAsync(int port, CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            using var scope = CommandLogScope.Background();
            result = await _processRunner.RunAsync(Lsof, ["-nP", $"-iTCP:{port.ToString(CultureInfo.InvariantCulture)}", "-sTCP:LISTEN", "-t"], cancellationToken: cancellationToken);
        }
        catch (ProcessLaunchException)
        {
            return null;
        }

        // lsof exits 1 when nothing matched.
        return result.Success ? ParseProcessId(result.StandardOutput) : null;
    }

    /// <summary>The first process ID in <c>lsof -t</c> output, or <see langword="null"/> when there's none.</summary>
    internal static int? ParseProcessId(string output)
    {
        foreach (var line in output.AsSpan().EnumerateLines())
        {
            if (int.TryParse(line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
            {
                return processId;
            }
        }

        return null;
    }
}
