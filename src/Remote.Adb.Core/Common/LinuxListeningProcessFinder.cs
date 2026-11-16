using System.Globalization;

namespace Remote.Adb.Core.Common;

/// <summary>
/// Finds a port's listener on Linux through <c>/proc</c>: the LISTEN socket's inode from <c>/proc/net/tcp</c>
/// (<c>tcp6</c> as a fallback), then the process holding a descriptor to <c>socket:[inode]</c>. When the descriptors
/// can't be read (another user's process), it falls back to the adb server's command line
/// (<c>adb -L tcp:&lt;port&gt; fork-server server …</c>).
/// </summary>
internal sealed class LinuxListeningProcessFinder : IListeningProcessFinder
{
    private const string ListenState = "0A";
    private const string ProcRoot = "/proc";

    private static int? Find(int port)
    {
        var inode = ReadListeningInode(Path.Combine(ProcRoot, "net", "tcp"), port)
                    ?? ReadListeningInode(Path.Combine(ProcRoot, "net", "tcp6"), port);
        if (inode is null)
        {
            return null;
        }

        var socketLink = $"socket:[{inode}]";
        var processIds = ProcessIds().ToList();

        foreach (var processId in processIds)
        {
            if (HoldsSocket(processId, socketLink))
            {
                return processId;
            }
        }

        foreach (var processId in processIds)
        {
            if (IsServerCommandLine(ReadCommandLine(processId), port))
            {
                return processId;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public Task<int?> FindAsync(int port, CancellationToken cancellationToken = default) => Task.Run(() => Find(port), cancellationToken);

    private static bool HoldsSocket(int processId, string socketLink)
    {
        try
        {
            foreach (var descriptor in Directory.EnumerateFileSystemEntries(Path.Combine(ProcRoot, processId.ToString(CultureInfo.InvariantCulture), "fd")))
            {
                if (string.Equals(new FileInfo(descriptor).LinkTarget, socketLink, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another user's process, or it exited mid-scan.
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="commandLine"/> (NUL-separated, as in <c>/proc/&lt;pid&gt;/cmdline</c>) is an adb server
    /// listening on <paramref name="port"/>: it has a <c>fork-server</c> argument and a <c>tcp:…:&lt;port&gt;</c> (or
    /// <c>tcp:&lt;port&gt;</c>) socket spec.
    /// </summary>
    internal static bool IsServerCommandLine(string commandLine, int port)
    {
        var source = commandLine.AsSpan();
        var suffix = $":{port.ToString(CultureInfo.InvariantCulture)}";
        var forkServer = false;
        var listensOnPort = false;

        foreach (var range in source.Split('\0'))
        {
            var argument = source[range];
            forkServer |= argument.SequenceEqual("fork-server");
            listensOnPort |= argument.StartsWith("tcp:", StringComparison.Ordinal) && argument.EndsWith(suffix, StringComparison.Ordinal);
        }

        return forkServer && listensOnPort;
    }

    /// <summary>
    /// Parses <c>/proc/net/tcp</c> (or <c>tcp6</c>) text for the inode of the socket in LISTEN state (<c>0A</c>) on
    /// <paramref name="port"/>; <see langword="null"/> when there's none.
    /// </summary>
    internal static ulong? ParseListeningInode(string table, int port)
    {
        foreach (var rawLine in table.AsSpan().EnumerateLines())
        {
            // sl local_address rem_address st tx_queue:rx_queue tr:tm->when retrnsmt uid timeout inode …
            var rest = rawLine;
            var slot = SpanTokenizer.NextToken(ref rest);
            var localAddress = SpanTokenizer.NextToken(ref rest);
            SpanTokenizer.NextToken(ref rest);
            var state = SpanTokenizer.NextToken(ref rest);
            if (slot.IsEmpty || slot[^1] != ':' || !state.SequenceEqual(ListenState))
            {
                continue;
            }

            var colon = localAddress.LastIndexOf(':');
            if (colon < 0 || !int.TryParse(localAddress[(colon + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var localPort) || localPort != port)
            {
                continue;
            }

            // Skip tx/rx queue, timer, retransmits, uid and timeout to reach the inode.
            for (var skipped = 0; skipped < 5; skipped++)
            {
                SpanTokenizer.NextToken(ref rest);
            }

            if (ulong.TryParse(SpanTokenizer.NextToken(ref rest), NumberStyles.None, CultureInfo.InvariantCulture, out var inode) && inode != 0)
            {
                return inode;
            }
        }

        return null;
    }

    private static IEnumerable<int> ProcessIds()
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateDirectories(ProcRoot).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            if (int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
            {
                yield return processId;
            }
        }
    }

    private static string ReadCommandLine(int processId)
    {
        try
        {
            return File.ReadAllText(Path.Combine(ProcRoot, processId.ToString(CultureInfo.InvariantCulture), "cmdline"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static ulong? ReadListeningInode(string path, int port)
    {
        try
        {
            return File.Exists(path) ? ParseListeningInode(File.ReadAllText(path), port) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
