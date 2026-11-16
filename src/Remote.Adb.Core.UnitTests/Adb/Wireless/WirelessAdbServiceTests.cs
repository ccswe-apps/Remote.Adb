using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Settings;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Adb.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class WirelessAdbServiceTests
{
    private protected const string Guid = "adb-R58M123ABC-AbCdEf";

    private protected static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private protected AdbRunnerMock Adb = null!;
    private protected List<string> Saved = null!;
    private protected Mock<ISettingsService> Settings = null!;
    private protected FakeTimeProvider Time = null!;

    private protected WirelessAdbService CreateService() =>
        new(Adb.Object, Settings.Object, Time, new LoggerFake<WirelessAdbService>())
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            EnableConnectTimeout = TimeSpan.FromSeconds(10),
            PairConnectTimeout = TimeSpan.FromSeconds(10),
            PairTimeout = TimeSpan.FromSeconds(30),
            PollInterval = PollInterval,
        };

    // Makes adb commands starting with `command` block until their token is cancelled.
    private protected void Hangs(string command) =>
        Adb.Inner
            .Setup(runner => runner.RunAsync(It.Is<IReadOnlyList<string>>(args => args[0] == command), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyList<string>, string?, CancellationToken>(async (_, _, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new ProcessResult(0, string.Empty, string.Empty);
            });

    [SetUp]
    public void SetUp()
    {
        Adb = new AdbRunnerMock();
        Saved = [];
        Settings = new Mock<ISettingsService>();
        Settings.SetupGet(settings => settings.WirelessAddresses).Returns(() => Saved);
        Settings.SetupSet(settings => settings.WirelessAddresses = It.IsAny<IReadOnlyList<string>>())
            .Callback<IReadOnlyList<string>>(value => Saved = [.. value]);
        Time = new FakeTimeProvider();
    }

    public class When_ConnectAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_does_not_remember_a_failure()
        {
            Adb.Returns(["connect", "192.168.1.20:5555"], "failed to connect to '192.168.1.20:5555': Connection refused\n");

            await CreateService().ConnectAsync("192.168.1.20:5555");

            Assert.That(Saved, Is.Empty);
        }

        [Test]
        public async Task It_does_not_remember_when_asked_not_to()
        {
            Adb.Returns(["connect", "192.168.1.20:5555"], "connected to 192.168.1.20:5555\n");

            await CreateService().ConnectAsync("192.168.1.20:5555", remember: false);

            Assert.That(Saved, Is.Empty);
        }

        [Test]
        public async Task It_passes_the_address_through_unchanged()
        {
            Adb.Returns(["connect", "192.168.1.20"], "connected to 192.168.1.20:5555\n");

            var result = await CreateService().ConnectAsync("  192.168.1.20 ");

            Assert.That(result.Outcome, Is.EqualTo(WirelessConnectOutcome.Connected));
            Adb.Verify(["connect", "192.168.1.20"], Times.Once());
        }

        [Test]
        public async Task It_rejects_a_blank_address_without_running_adb()
        {
            var result = await CreateService().ConnectAsync("   ");

            Assert.That(result.Outcome, Is.EqualTo(WirelessConnectOutcome.InvalidAddress));
            Adb.VerifyNoOtherCalls();
        }

        [Test]
        public async Task It_remembers_a_successful_connection_once()
        {
            Adb.Returns(["connect", "192.168.1.20:5555"], "connected to 192.168.1.20:5555\n");
            var service = CreateService();

            await service.ConnectAsync("192.168.1.20:5555");
            await service.ConnectAsync("192.168.1.20:5555");

            Assert.That(Saved, Is.EqualTo(new[] { "192.168.1.20:5555" }));
        }

        [Test]
        public async Task It_reports_the_server_unavailable_when_adb_cannot_be_launched()
        {
            Adb.ThrowsForAny(new ProcessLaunchException("adb", new InvalidOperationException()));

            var result = await CreateService().ConnectAsync("192.168.1.20:5555");

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WirelessConnectOutcome.ServerUnavailable));
                Assert.That(result.Message, Does.Contain("Could not start 'adb'"));
            });
        }

        [Test]
        public async Task It_times_out_a_connect_that_hangs()
        {
            Hangs("connect");

            var result = await Time.AdvanceUntilCompleteAsync(CreateService().ConnectAsync("192.168.1.20:5555"), PollInterval);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WirelessConnectOutcome.ConnectionFailed));
                Assert.That(result.Message, Does.Contain("Timed out"));
            });
        }
    }

    public class When_DisconnectAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_forgets_the_saved_address_even_when_saved_without_the_default_port()
        {
            Saved = ["192.168.1.20", "192.168.1.30:5555"];
            Adb.Returns(["disconnect", "192.168.1.20:5555"], "disconnected 192.168.1.20:5555\n");

            var result = await CreateService().DisconnectAsync("192.168.1.20:5555");

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True);
                Assert.That(Saved, Is.EqualTo(new[] { "192.168.1.30:5555" }));
            });
        }
    }

    public class When_DiscoverAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_lists_the_discovered_services()
        {
            Adb.Returns(["mdns", "services"], $"List of discovered mdns services\n{Guid}\t_adb-tls-connect._tcp.\t192.168.1.20:41235\n");

            var services = await CreateService().DiscoverAsync();

            Assert.That(services, Is.EqualTo(new[] { new MdnsService(Guid, MdnsServiceType.Connect, "192.168.1.20", 41235) }));
        }

        [Test]
        public async Task It_returns_nothing_when_adb_cannot_be_launched()
        {
            Adb.ThrowsForAny(new ProcessLaunchException("adb", new InvalidOperationException()));

            var services = await CreateService().DiscoverAsync();

            Assert.That(services, Is.Empty);
        }

        [Test]
        public async Task It_returns_nothing_when_mdns_services_fails()
        {
            Adb.Returns(["mdns", "services"], string.Empty, exitCode: 1, stderr: "error: unknown command\n");

            var services = await CreateService().DiscoverAsync();

            Assert.That(services, Is.Empty);
        }
    }

    public class When_EnableWirelessAsync_Is_Called : WirelessAdbServiceTests
    {
        private const string Serial = "R58M123ABC";

        private static readonly string[] Connect = ["connect", "192.168.1.20:5555"];
        private static readonly string[] IpAddr = ["-s", Serial, "shell", "ip", "-4", "addr", "show", "wlan0"];
        private static readonly string[] IpRoute = ["-s", Serial, "shell", "ip", "route"];
        private static readonly string[] Tcpip = ["-s", Serial, "tcpip", "5555"];

        private IReadOnlyList<string> CommandsRun() =>
            Adb.Inner.Invocations.Select(invocation => string.Join(' ', (IReadOnlyList<string>)invocation.Arguments[0]!)).ToList();

        [Test]
        public async Task It_changes_nothing_when_the_device_is_not_on_wifi()
        {
            Adb.Returns(IpRoute, "10.20.0.0/16 dev rmnet_data0 src 10.20.30.40\n");
            Adb.Returns(IpAddr, "30: wlan0: <NO-CARRIER> state DOWN\n");

            var result = await CreateService().EnableWirelessAsync(Serial);

            Assert.That(result.Outcome, Is.EqualTo(WirelessEnableOutcome.NoWifiAddress));
            Adb.Verify(Tcpip, Times.Never());
        }

        [Test]
        public async Task It_falls_back_to_the_wlan0_address()
        {
            Adb.Returns(IpRoute, "10.20.0.0/16 dev rmnet_data0 src 10.20.30.40\n");
            Adb.Returns(IpAddr, "    inet 192.168.1.20/24 brd 192.168.1.255 scope global wlan0\n");
            Adb.Returns(Tcpip, "restarting in TCP mode port: 5555\n");
            Adb.Returns(Connect, "connected to 192.168.1.20:5555\n");

            var result = await CreateService().EnableWirelessAsync(Serial);

            Assert.That(result.Address, Is.EqualTo("192.168.1.20:5555"));
        }

        [Test]
        public async Task It_reads_the_wifi_address_before_switching_to_tcp_and_remembers_the_connection()
        {
            Adb.Returns(IpRoute, "192.168.1.0/24 dev wlan0 proto kernel scope link src 192.168.1.20\n");
            Adb.Returns(Tcpip, "restarting in TCP mode port: 5555\n");
            Adb.Returns(Connect, "connected to 192.168.1.20:5555\n");

            var result = await CreateService().EnableWirelessAsync(Serial);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WirelessEnableOutcome.Connected));
                Assert.That(result.Address, Is.EqualTo("192.168.1.20:5555"));
                Assert.That(CommandsRun(), Is.EqualTo(new[] { string.Join(' ', IpRoute), string.Join(' ', Tcpip), string.Join(' ', Connect) }));
                Assert.That(Saved, Is.EqualTo(new[] { "192.168.1.20:5555" }));
            });
        }

        [Test]
        public async Task It_reports_a_partial_success_when_the_device_never_answers()
        {
            Adb.Returns(IpRoute, "192.168.1.0/24 dev wlan0 src 192.168.1.20\n");
            Adb.Returns(Tcpip, "restarting in TCP mode port: 5555\n");
            Adb.Returns(Connect, "failed to connect to '192.168.1.20:5555': Connection timed out\n");

            var result = await Time.AdvanceUntilCompleteAsync(CreateService().EnableWirelessAsync(Serial), PollInterval);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WirelessEnableOutcome.ConnectFailed));
                Assert.That(result.Address, Is.EqualTo("192.168.1.20:5555"));
                Assert.That(result.Message, Does.StartWith("failed to connect"));
                Assert.That(Saved, Is.Empty);
            });
        }

        [Test]
        public async Task It_reports_a_tcpip_failure()
        {
            Adb.Returns(IpRoute, "192.168.1.0/24 dev wlan0 src 192.168.1.20\n");
            Adb.Returns(Tcpip, "", exitCode: 1, stderr: "error: device unauthorized.\n");

            var result = await CreateService().EnableWirelessAsync(Serial);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WirelessEnableOutcome.TcpipFailed));
                Assert.That(result.Message, Is.EqualTo("error: device unauthorized."));
            });
            Adb.Verify(Connect, Times.Never());
        }

        [Test]
        public async Task It_retries_the_connect_every_poll_interval_while_adbd_restarts()
        {
            Adb.Returns(IpRoute, "192.168.1.0/24 dev wlan0 src 192.168.1.20\n");
            Adb.Returns(Tcpip, "restarting in TCP mode port: 5555\n");
            var attempts = 0;
            Adb.Inner
                .Setup(runner => runner.RunAsync(It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(Connect)), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ++attempts < 3
                    ? new ProcessResult(0, "failed to connect to '192.168.1.20:5555': Connection refused\n", string.Empty)
                    : new ProcessResult(0, "connected to 192.168.1.20:5555\n", string.Empty));

            var result = await Time.AdvanceUntilCompleteAsync(CreateService().EnableWirelessAsync(Serial), PollInterval);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WirelessEnableOutcome.Connected));
                Assert.That(attempts, Is.EqualTo(3));
            });
        }
    }

    public class When_IsDiscoveryAvailableAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_is_available_when_mdns_check_reports_a_daemon()
        {
            Adb.Returns(["mdns", "check"], "mdns daemon version [Openscreen discovery 0.0.0]\n");

            Assert.That(await CreateService().IsDiscoveryAvailableAsync(), Is.True);
        }

        [Test]
        public async Task It_is_unavailable_when_discovery_is_disabled_even_though_adb_exits_zero()
        {
            Adb.Returns(["mdns", "check"], "ERROR: mdns discovery disabled\n");

            Assert.That(await CreateService().IsDiscoveryAvailableAsync(), Is.False);
        }
    }

    public class When_PairAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_connects_to_the_announced_address_when_adb_did_not()
        {
            Adb.Returns(["pair", "192.168.1.20:37123"], $"Successfully paired to 192.168.1.20:37123 [guid={Guid}]\n");
            Adb.Returns(["devices"], "List of devices attached\n");
            Adb.Returns(["mdns", "services"], $"List of discovered mdns services\n{Guid}\t_adb-tls-connect._tcp.\t192.168.1.20:41235\n");
            Adb.Returns(["connect", "192.168.1.20:41235"], "connected to 192.168.1.20:41235\n");

            var result = await CreateService().PairAsync("192.168.1.20:37123", "123456");

            Assert.Multiple(() =>
            {
                Assert.That(result.Connected, Is.True);
                Assert.That(Saved, Is.Empty, "a TLS connect port changes, so it isn't remembered");
            });
        }

        [Test]
        public async Task It_gives_up_when_adb_pair_hangs()
        {
            Hangs("pair");

            var result = await Time.AdvanceUntilCompleteAsync(CreateService().PairAsync("192.168.1.20:37123", "123456"), PollInterval);

            Assert.Multiple(() =>
            {
                Assert.That(result.Paired, Is.False);
                Assert.That(result.Message, Does.Contain("Timed out"));
            });
        }

        [Test]
        public async Task It_propagates_a_caller_cancellation_instead_of_reporting_a_timeout()
        {
            Hangs("pair");
            using var cancellation = new CancellationTokenSource();
            var pairing = CreateService().PairAsync("192.168.1.20:37123", "123456", cancellation.Token);

            await cancellation.CancelAsync();

            await Assert.CatchAsync<OperationCanceledException>(() => pairing);
        }

        [Test]
        public async Task It_reports_a_launch_failure_as_not_paired()
        {
            Adb.ThrowsForAny(new ProcessLaunchException("adb", new InvalidOperationException()));

            var result = await CreateService().PairAsync("192.168.1.20:37123", "123456");

            Assert.Multiple(() =>
            {
                Assert.That(result.Paired, Is.False);
                Assert.That(result.Message, Does.Contain("Could not start 'adb'"));
            });
        }

        [Test]
        public async Task It_reports_connected_when_adb_connected_it_automatically()
        {
            Adb.Returns(["pair", "192.168.1.20:37123"], $"Successfully paired to 192.168.1.20:37123 [guid={Guid}]\n");
            Adb.Returns(["devices"], $"List of devices attached\n{Guid}._adb-tls-connect._tcp\tdevice\n");

            var result = await CreateService().PairAsync("192.168.1.20:37123", "123456");

            Assert.Multiple(() =>
            {
                Assert.That(result.Paired, Is.True);
                Assert.That(result.Connected, Is.True);
            });
        }

        [Test]
        public async Task It_reports_paired_but_not_connected_when_the_device_never_appears()
        {
            Adb.Returns(["pair", "192.168.1.20:37123"], $"Successfully paired to 192.168.1.20:37123 [guid={Guid}]\n");
            Adb.Returns(["devices"], "List of devices attached\n");
            Adb.Returns(["mdns", "services"], "List of discovered mdns services\n");

            var result = await Time.AdvanceUntilCompleteAsync(CreateService().PairAsync("192.168.1.20:37123", "123456"), PollInterval);

            Assert.Multiple(() =>
            {
                Assert.That(result.Paired, Is.True);
                Assert.That(result.Connected, Is.False);
                Assert.That(result.Message, Does.Contain("Wireless debugging"));
            });
        }

        [Test]
        public async Task It_returns_the_failure_without_waiting()
        {
            Adb.Returns(["pair", "192.168.1.20:37123"], "Enter pairing code: ", exitCode: 1, stderr: "error: protocol fault (couldn't read status message): Success\n");

            var result = await CreateService().PairAsync("192.168.1.20:37123", "000000");

            Assert.That(result.Paired, Is.False);
            Adb.Verify(["devices"], Times.Never());
        }

        [Test]
        public async Task It_sends_the_code_on_stdin_and_never_as_an_argument()
        {
            Adb.Returns(["pair", "192.168.1.20:37123"], "Enter pairing code: error\n", exitCode: 1);

            await CreateService().PairAsync("192.168.1.20:37123", " 123456 ");

            Adb.VerifyStdin(["pair", "192.168.1.20:37123"], "123456\n");
        }
    }

    public class When_ReconnectSavedAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_connects_each_saved_address_and_counts_the_successes()
        {
            Saved = ["192.168.1.20:5555", "192.168.1.30:5555"];
            Adb.Returns(["connect", "192.168.1.20:5555"], "already connected to 192.168.1.20:5555\n");
            Adb.Returns(["connect", "192.168.1.30:5555"], "failed to connect to '192.168.1.30:5555': Connection refused\n");

            var connected = await CreateService().ReconnectSavedAsync();

            Assert.Multiple(() =>
            {
                Assert.That(connected, Is.EqualTo(1));
                Assert.That(Saved, Has.Count.EqualTo(2), "a failed reconnect keeps the address for next time");
            });
        }

        [Test]
        public async Task It_does_nothing_without_saved_addresses()
        {
            var connected = await CreateService().ReconnectSavedAsync();

            Assert.That(connected, Is.Zero);
            Adb.VerifyNoOtherCalls();
        }
    }
}
