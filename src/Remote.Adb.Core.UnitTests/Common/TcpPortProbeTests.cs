using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.UnitTests.Common;

/// <summary>
/// The one place a real loopback socket is used: everything else mocks <see cref="IPortProbe"/>.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class TcpPortProbeTests
{
    // Bounds Windows' retries of a refused loopback connect, so the "not listening" case stays fast.
    private protected static TcpPortProbe CreateProbe() => new(TimeProvider.System) { ConnectTimeout = TimeSpan.FromMilliseconds(250) };

    public class When_IsListeningAsync_Is_Called : TcpPortProbeTests
    {
        [Test]
        public async Task It_reflects_whether_a_listener_is_bound_to_the_port()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = CreateProbe();

            var whileListening = await probe.IsListeningAsync(port);
            listener.Stop();
            listener.Dispose();
            var afterStopped = await probe.IsListeningAsync(port);

            Assert.Multiple(() =>
            {
                Assert.That(whileListening, Is.True);
                Assert.That(afterStopped, Is.False);
            });
        }

        [TestCase(0)]
        [TestCase(70000)]
        public async Task It_returns_false_for_an_out_of_range_port(int port)
        {
            var result = await CreateProbe().IsListeningAsync(port);

            Assert.That(result, Is.False);
        }
    }
}
