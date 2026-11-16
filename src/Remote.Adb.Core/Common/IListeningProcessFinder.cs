namespace Remote.Adb.Core.Common;

/// <summary>
/// Finds the process that owns the TCP listening socket on a port — how the app identifies the local adb server
/// precisely (the server listening on the adb server port, not every <c>adb</c> process of the same binary). Implemented
/// per platform; see <see cref="ListeningProcessFinder.Create"/>.
/// </summary>
internal interface IListeningProcessFinder
{
    /// <summary>
    /// Returns the ID of the process listening for IPv4 TCP connections on <paramref name="port"/>, or
    /// <see langword="null"/> when nothing is listening there or the owner can't be determined (e.g. another user's
    /// process the OS won't disclose).
    /// </summary>
    Task<int?> FindAsync(int port, CancellationToken cancellationToken = default);
}
