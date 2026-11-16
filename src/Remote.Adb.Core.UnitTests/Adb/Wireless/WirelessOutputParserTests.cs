using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.UnitTests.Adb.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class WirelessOutputParserTests
{
    private protected static ProcessResult Output(string stdout, int exitCode = 0, string stderr = "") => new(exitCode, stdout, stderr);

    public class When_ParseConnect_Is_Called : WirelessOutputParserTests
    {
        [TestCase("connected to 192.168.1.20:5555\n", WirelessConnectOutcome.Connected)]
        [TestCase("already connected to 192.168.1.20:5555\n", WirelessConnectOutcome.AlreadyConnected)]
        [TestCase("failed to authenticate to 192.168.1.20:5555\n", WirelessConnectOutcome.AuthenticationFailed)]
        [TestCase("failed to connect to '192.168.1.20:5555': Connection refused\n", WirelessConnectOutcome.ConnectionFailed)]
        [TestCase("cannot connect to 192.168.1.20:5555: No connection could be made (10061)\n", WirelessConnectOutcome.ConnectionFailed)]
        [TestCase("cannot resolve host 'nope' and port 5555: No such host is known. (11001)\n", WirelessConnectOutcome.ResolveFailed)]
        [TestCase("failed to resolve host: 'nope': Name or service not known\n", WirelessConnectOutcome.ResolveFailed)]
        [TestCase("empty address\n", WirelessConnectOutcome.InvalidAddress)]
        [TestCase("something new\n", WirelessConnectOutcome.Unknown)]
        public void It_classifies_the_message_even_though_adb_exits_zero(string stdout, WirelessConnectOutcome expected)
        {
            var result = WirelessOutputParser.ParseConnect(Output(stdout));

            Assert.That(result.Outcome, Is.EqualTo(expected));
        }

        [Test]
        public void It_keeps_adbs_message()
        {
            var result = WirelessOutputParser.ParseConnect(Output("connected to 192.168.1.20:5555\r\n"));

            Assert.That(result.Message, Is.EqualTo("connected to 192.168.1.20:5555"));
        }

        [Test]
        public void It_falls_back_to_stderr_when_stdout_is_empty()
        {
            var result = WirelessOutputParser.ParseConnect(Output("", stderr: "failed to connect to '192.168.1.20:5555': Connection refused\n"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WirelessConnectOutcome.ConnectionFailed));
                Assert.That(result.Message, Is.EqualTo("failed to connect to '192.168.1.20:5555': Connection refused"));
            });
        }

        [Test]
        public void It_reports_a_non_zero_exit_as_server_unavailable()
        {
            var result = WirelessOutputParser.ParseConnect(Output("", exitCode: 1, stderr: "error: cannot connect to daemon\n"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WirelessConnectOutcome.ServerUnavailable));
                Assert.That(result.Message, Is.EqualTo("error: cannot connect to daemon"));
                Assert.That(result.Success, Is.False);
            });
        }
    }

    public class When_ParseDisconnect_Is_Called : WirelessOutputParserTests
    {
        [Test]
        public void It_succeeds_on_disconnected()
        {
            var result = WirelessOutputParser.ParseDisconnect(Output("disconnected 192.168.1.20:5555\n"));

            Assert.That(result, Is.EqualTo(new WirelessResult(true, "disconnected 192.168.1.20:5555")));
        }

        [Test]
        public void It_fails_on_no_such_device()
        {
            var result = WirelessOutputParser.ParseDisconnect(Output("", exitCode: 1, stderr: "error: no such device '192.168.1.20:5555'\n"));

            Assert.That(result, Is.EqualTo(new WirelessResult(false, "error: no such device '192.168.1.20:5555'")));
        }
    }

    public class When_ParsePair_Is_Called : WirelessOutputParserTests
    {
        [Test]
        public void It_reads_the_guid_after_the_stdin_prompt()
        {
            var result = WirelessOutputParser.ParsePair(
                Output("Enter pairing code: Successfully paired to 192.168.1.20:37123 [guid=adb-R58M123ABC-AbCdEf]\n"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Paired, Is.True);
                Assert.That(result.Guid, Is.EqualTo("adb-R58M123ABC-AbCdEf"));
                Assert.That(result.Message, Is.EqualTo("Successfully paired to 192.168.1.20:37123 [guid=adb-R58M123ABC-AbCdEf]"));
            });
        }

        [Test]
        public void It_passes_through_a_protocol_fault()
        {
            var result = WirelessOutputParser.ParsePair(
                Output("Enter pairing code: ", exitCode: 1, stderr: "error: protocol fault (couldn't read status message): Success\n"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Paired, Is.False);
                Assert.That(result.Message, Is.EqualTo("error: protocol fault (couldn't read status message): Success"));
            });
        }

        [Test]
        public void It_handles_crlf_line_endings()
        {
            var result = WirelessOutputParser.ParsePair(
                Output("Enter pairing code: \r\nSuccessfully paired to 192.168.1.20:37123 [guid=adb-R58M123ABC-AbCdEf]\r\n"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Paired, Is.True);
                Assert.That(result.Guid, Is.EqualTo("adb-R58M123ABC-AbCdEf"));
                Assert.That(result.Message, Is.EqualTo("Successfully paired to 192.168.1.20:37123 [guid=adb-R58M123ABC-AbCdEf]"));
            });
        }

        [Test]
        public void It_strips_the_prompt_from_a_stdout_only_failure()
        {
            var result = WirelessOutputParser.ParsePair(Output("Enter pairing code: Failed: Wrong password", exitCode: 1));

            Assert.That(result.Message, Is.EqualTo("Failed: Wrong password"));
        }
    }

    public class When_ParseMdnsServices_Is_Called : WirelessOutputParserTests
    {
        [Test]
        public void It_handles_crlf_line_endings()
        {
            const string output =
                "List of discovered mdns services\r\n" +
                "adb-R58M123ABC-AbCdEf\t_adb-tls-connect._tcp.\t192.168.1.20:41235\r\n";

            var services = WirelessOutputParser.ParseMdnsServices(output);

            Assert.That(services, Is.EqualTo(new[] { new MdnsService("adb-R58M123ABC-AbCdEf", MdnsServiceType.Connect, "192.168.1.20", 41235) }));
        }

        [Test]
        public void It_parses_each_service_and_skips_the_header_and_junk()
        {
            const string output =
                "List of discovered mdns services\n" +
                "adb-R58M123ABC-AbCdEf\t_adb-tls-connect._tcp.\t192.168.1.20:41235\n" +
                "studio-Xy z\t_adb-tls-pairing._tcp.local.\t192.168.1.20:37123\n" +
                "adb-legacy\t_adb._tcp\t192.168.1.30:5555\n" +
                "not a service line\n" +
                "bad-port\t_adb._tcp\t192.168.1.31:abc\n";

            var services = WirelessOutputParser.ParseMdnsServices(output);

            Assert.That(services, Is.EqualTo(new[]
            {
                new MdnsService("adb-R58M123ABC-AbCdEf", MdnsServiceType.Connect, "192.168.1.20", 41235),
                new MdnsService("studio-Xy z", MdnsServiceType.Pairing, "192.168.1.20", 37123),
                new MdnsService("adb-legacy", MdnsServiceType.Legacy, "192.168.1.30", 5555),
            }));
        }
    }

    public class When_IsMdnsAvailable_Is_Called : WirelessOutputParserTests
    {
        [TestCase("mdns daemon version [Openscreen discovery 0.0.0]\n", true)]
        [TestCase("mdns daemon version [10970003]\n", true)]
        [TestCase("ERROR: mdns discovery disabled\n", false)]
        [TestCase("ERROR: mdns daemon unavailable\n", false)]
        [TestCase("", false)]
        public void It_reports_whether_discovery_works(string output, bool expected)
        {
            Assert.That(WirelessOutputParser.IsMdnsAvailable(output), Is.EqualTo(expected));
        }
    }

    public class When_ParseIpAddrInet_Is_Called : WirelessOutputParserTests
    {
        [Test]
        public void It_reads_the_address_without_the_prefix_length()
        {
            const string output =
                "30: wlan0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc mq state UP group default qlen 3000\n" +
                "    inet 192.168.1.20/24 brd 192.168.1.255 scope global wlan0\n" +
                "       valid_lft forever preferred_lft forever\n";

            Assert.That(WirelessOutputParser.ParseIpAddrInet(output), Is.EqualTo("192.168.1.20"));
        }

        [Test]
        public void It_returns_null_when_the_interface_has_no_address()
        {
            Assert.That(WirelessOutputParser.ParseIpAddrInet("30: wlan0: <NO-CARRIER,BROADCAST> mtu 1500 state DOWN\n"), Is.Null);
        }
    }

    public class When_ParseTcpip_Is_Called : WirelessOutputParserTests
    {
        [Test]
        public void It_succeeds_when_adbd_restarts_in_tcp_mode()
        {
            var result = WirelessOutputParser.ParseTcpip(Output("restarting in TCP mode port: 5555\n"));

            Assert.That(result, Is.EqualTo(new WirelessResult(true, "restarting in TCP mode port: 5555")));
        }

        [Test]
        public void It_fails_with_adbs_error()
        {
            var result = WirelessOutputParser.ParseTcpip(Output("", exitCode: 1, stderr: "error: device unauthorized.\n"));

            Assert.That(result, Is.EqualTo(new WirelessResult(false, "error: device unauthorized.")));
        }
    }

    public class When_ParseIpRouteSource_Is_Called : WirelessOutputParserTests
    {
        [Test]
        public void It_prefers_the_wlan_route()
        {
            const string output =
                "10.0.0.0/8 dev rmnet_data0 proto kernel scope link src 10.20.30.40\n" +
                "192.168.1.0/24 dev wlan0 proto kernel scope link src 192.168.1.20\n";

            Assert.That(WirelessOutputParser.ParseIpRouteSource(output), Is.EqualTo("192.168.1.20"));
        }

        [Test]
        public void It_ignores_mobile_data_and_vpn_routes()
        {
            const string output =
                "10.20.0.0/16 dev rmnet_data0 proto kernel scope link src 10.20.30.40\n" +
                "10.8.0.0/24 dev tun0 proto kernel scope link src 10.8.0.2\n";

            Assert.That(WirelessOutputParser.ParseIpRouteSource(output), Is.Null);
        }

        [Test]
        public void It_returns_null_when_no_route_has_a_source()
        {
            Assert.That(WirelessOutputParser.ParseIpRouteSource("default via 192.168.1.1 dev wlan0\n"), Is.Null);
        }
    }
}
