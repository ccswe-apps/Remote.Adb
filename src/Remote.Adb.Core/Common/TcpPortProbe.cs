using System.Net;
using System.Net.Sockets;

namespace Remote.Adb.Core.Common;

/// <inheritdoc />
internal sealed class TcpPortProbe : IPortProbe
{
    private readonly TimeProvider _timeProvider;

    public TcpPortProbe(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// How long a connect may take. A listener on loopback accepts at once; without a bound, Windows retries a refused
    /// loopback connect for about two seconds before giving up.
    /// </summary>
    internal TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public async Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > IPEndPoint.MaxPort)
        {
            return false;
        }

        try
        {
            var (connected, _) = await Timeouts.TryWithTimeoutAsync(
                async token =>
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port, token);
                    return true;
                },
                ConnectTimeout,
                cancellationToken,
                _timeProvider);

            return connected;
        }
        catch (SocketException)
        {
            // Connection refused / unreachable — nothing is listening.
            return false;
        }
    }
}
