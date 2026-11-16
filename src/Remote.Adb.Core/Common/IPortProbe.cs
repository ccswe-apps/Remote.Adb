namespace Remote.Adb.Core.Common;

/// <summary>
/// Checks whether something accepts TCP connections on a loopback port. A seam so services that ask "is the adb server
/// up?" are unit-testable without real sockets.
/// </summary>
internal interface IPortProbe
{
    /// <summary>
    /// Returns whether something accepts a TCP connection on <c>127.0.0.1:<paramref name="port"/></c>. A raw connect,
    /// so — unlike an <c>adb</c> client command — it never spawns a server. An out-of-range port, a refused connection
    /// and one that doesn't complete within the probe's timeout all count as "not listening"; cancellation of
    /// <paramref name="cancellationToken"/> propagates.
    /// </summary>
    Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken = default);
}
