using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Tunnel;

namespace Remote.Adb.Console.UnitTests;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class TunnelCommandTests
{
    private protected StringWriter Output = null!;
    private protected TunnelStatus Status = null!;
    private protected Mock<ITunnelService> Tunnel = null!;

    [SetUp]
    public void SetUp()
    {
        Output = new StringWriter();
        Status = new TunnelStatus(TunnelState.Disconnected, null);
        Tunnel = new Mock<ITunnelService>();
        Tunnel.SetupGet(tunnel => tunnel.Status).Returns(() => Status);
        Tunnel.Setup(tunnel => tunnel.DisconnectAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Raise(TunnelState.Disconnected))
            .Returns(Task.CompletedTask);
    }

    [TearDown]
    public void TearDown() => Output.Dispose();

    private protected void ConnectsTo(TunnelState state, string? message = null) =>
        Tunnel.Setup(tunnel => tunnel.ConnectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Raise(TunnelState.Connecting);
                Raise(state, message);
            })
            .Returns(Task.CompletedTask);

    private protected void Raise(TunnelState state, string? message = null)
    {
        Status = new TunnelStatus(state, message);
        Tunnel.Raise(tunnel => tunnel.StatusChanged += null, Tunnel.Object, Status);
    }

    public class When_RunAsync_Is_Called : TunnelCommandTests
    {
        [Test]
        public async Task It_returns_nonzero_when_the_connect_faults()
        {
            ConnectsTo(TunnelState.Faulted, "Could not reach host");

            var exitCode = await TunnelCommand.RunAsync(Tunnel.Object, "devhost", Output, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(Output.ToString(), Does.Contain("[tunnel] Faulted: Could not reach host"));
            });
            Tunnel.Verify(tunnel => tunnel.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_returns_nonzero_when_the_tunnel_faults_after_failed_reconnects()
        {
            ConnectsTo(TunnelState.Connected);

            var run = TunnelCommand.RunAsync(Tunnel.Object, "devhost", Output, CancellationToken.None);
            Raise(TunnelState.Reconnecting, "The tunnel dropped.");
            Raise(TunnelState.Faulted, "Lost the tunnel and couldn't reconnect after 5 attempts.");
            var exitCode = await run;

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(Status.State, Is.EqualTo(TunnelState.Disconnected));
            });
        }

        [Test]
        public async Task It_disconnects_and_returns_zero_when_cancelled_while_connected()
        {
            ConnectsTo(TunnelState.Connected);
            using var cancel = new CancellationTokenSource();

            var run = TunnelCommand.RunAsync(Tunnel.Object, "devhost", Output, cancel.Token);
            await cancel.CancelAsync();
            var exitCode = await run;

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(0));
                Assert.That(Output.ToString(), Does.Contain("Tunnel closed."));
            });
            Tunnel.Verify(tunnel => tunnel.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task It_passes_the_cancellation_token_to_the_connect()
        {
            using var cancel = new CancellationTokenSource();
            Tunnel.Setup(tunnel => tunnel.ConnectAsync("devhost", cancel.Token))
                .Callback(() => Raise(TunnelState.Disconnected))
                .Returns(Task.CompletedTask);

            var exitCode = await TunnelCommand.RunAsync(Tunnel.Object, "devhost", Output, cancel.Token);

            Assert.That(exitCode, Is.EqualTo(0));
            Tunnel.Verify(tunnel => tunnel.ConnectAsync("devhost", cancel.Token), Times.Once);
        }

        [Test]
        public async Task It_returns_zero_when_cancelled_before_the_connect_starts()
        {
            using var cancel = new CancellationTokenSource();
            await cancel.CancelAsync();
            Tunnel.Setup(tunnel => tunnel.ConnectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException(cancel.Token));

            var exitCode = await TunnelCommand.RunAsync(Tunnel.Object, "devhost", Output, cancel.Token);

            Assert.That(exitCode, Is.EqualTo(0));
        }
    }
}
