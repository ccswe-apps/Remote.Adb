using System.Diagnostics.CodeAnalysis;
using Moq;
using NUnit.Framework;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Settings;
using Remote.Adb.Core.UnitTests.Fakes;

namespace Remote.Adb.Core.UnitTests.Adb.Wireless;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class WirelessAdbServiceTests
{
    protected const string Adb = "adb";
    protected const string Guid = "adb-R58M123ABC-AbCdEf";

    protected Mock<IProcessRunner> ProcessRunner = null!;
    protected List<string> Saved = null!;
    protected Mock<ISettingsService> Settings = null!;

    [SetUp]
    public void SetUp()
    {
        ProcessRunner = new Mock<IProcessRunner>();
        Saved = [];
        Settings = new Mock<ISettingsService>();
        Settings.SetupGet(settings => settings.WirelessAddresses).Returns(() => Saved);
        Settings.SetupSet(settings => settings.WirelessAddresses = It.IsAny<IReadOnlyList<string>>())
            .Callback<IReadOnlyList<string>>(value => Saved = [.. value]);
    }

    protected WirelessAdbService CreateService()
    {
        var sdk = new Mock<IAndroidSdk>();
        sdk.Setup(s => s.AdbPath).Returns(Adb);

        return new WirelessAdbService(ProcessRunner.Object, sdk.Object, Settings.Object, new LoggerFake<WirelessAdbService>())
        {
            PairConnectTimeout = TimeSpan.FromMilliseconds(50),
            PollInterval = TimeSpan.FromMilliseconds(10),
        };
    }

    protected void Returns(string[] arguments, string stdout, int exitCode = 0, string stderr = "") =>
        ProcessRunner
            .Setup(runner => runner.RunAsync(Adb, It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(arguments)), It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(exitCode, stdout, stderr));

    protected void VerifyRan(string[] arguments, Times times) =>
        ProcessRunner.Verify(
            runner => runner.RunAsync(Adb, It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(arguments)), It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            times);

    public class When_ConnectAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_passes_the_address_through_unchanged()
        {
            Returns(["connect", "192.168.1.20"], "connected to 192.168.1.20:5555\n");

            var result = await CreateService().ConnectAsync("  192.168.1.20 ");

            Assert.That(result.Outcome, Is.EqualTo(WirelessConnectOutcome.Connected));
            VerifyRan(["connect", "192.168.1.20"], Times.Once());
        }

        [Test]
        public async Task It_remembers_a_successful_connection_once()
        {
            Returns(["connect", "192.168.1.20:5555"], "connected to 192.168.1.20:5555\n");
            var service = CreateService();

            await service.ConnectAsync("192.168.1.20:5555");
            await service.ConnectAsync("192.168.1.20:5555");

            Assert.That(Saved, Is.EqualTo(new[] { "192.168.1.20:5555" }));
        }

        [Test]
        public async Task It_does_not_remember_a_failure()
        {
            Returns(["connect", "192.168.1.20:5555"], "failed to connect to '192.168.1.20:5555': Connection refused\n");

            await CreateService().ConnectAsync("192.168.1.20:5555");

            Assert.That(Saved, Is.Empty);
        }

        [Test]
        public async Task It_does_not_remember_when_asked_not_to()
        {
            Returns(["connect", "192.168.1.20:5555"], "connected to 192.168.1.20:5555\n");

            await CreateService().ConnectAsync("192.168.1.20:5555", remember: false);

            Assert.That(Saved, Is.Empty);
        }

        [Test]
        public async Task It_rejects_a_blank_address_without_running_adb()
        {
            var result = await CreateService().ConnectAsync("   ");

            Assert.That(result.Outcome, Is.EqualTo(WirelessConnectOutcome.InvalidAddress));
            ProcessRunner.VerifyNoOtherCalls();
        }
    }

    public class When_DisconnectAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_forgets_the_saved_address_even_when_saved_without_the_default_port()
        {
            Saved = ["192.168.1.20", "192.168.1.30:5555"];
            Returns(["disconnect", "192.168.1.20:5555"], "disconnected 192.168.1.20:5555\n");

            var result = await CreateService().DisconnectAsync("192.168.1.20:5555");

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True);
                Assert.That(Saved, Is.EqualTo(new[] { "192.168.1.30:5555" }));
            });
        }
    }

    public class When_PairAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_sends_the_code_on_stdin_and_never_as_an_argument()
        {
            Returns(["pair", "192.168.1.20:37123"], "Enter pairing code: error\n", exitCode: 1);

            await CreateService().PairAsync("192.168.1.20:37123", " 123456 ");

            ProcessRunner.Verify(runner => runner.RunAsync(
                Adb,
                It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "pair", "192.168.1.20:37123" })),
                "123456\n",
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task It_reports_connected_when_adb_connected_it_automatically()
        {
            Returns(["pair", "192.168.1.20:37123"], $"Successfully paired to 192.168.1.20:37123 [guid={Guid}]\n");
            Returns(["devices"], $"List of devices attached\n{Guid}._adb-tls-connect._tcp\tdevice\n");

            var result = await CreateService().PairAsync("192.168.1.20:37123", "123456");

            Assert.Multiple(() =>
            {
                Assert.That(result.Paired, Is.True);
                Assert.That(result.Connected, Is.True);
            });
        }

        [Test]
        public async Task It_connects_to_the_announced_address_when_adb_did_not()
        {
            Returns(["pair", "192.168.1.20:37123"], $"Successfully paired to 192.168.1.20:37123 [guid={Guid}]\n");
            Returns(["devices"], "List of devices attached\n");
            Returns(["mdns", "services"], $"List of discovered mdns services\n{Guid}\t_adb-tls-connect._tcp.\t192.168.1.20:41235\n");
            Returns(["connect", "192.168.1.20:41235"], "connected to 192.168.1.20:41235\n");

            var result = await CreateService().PairAsync("192.168.1.20:37123", "123456");

            Assert.Multiple(() =>
            {
                Assert.That(result.Connected, Is.True);
                Assert.That(Saved, Is.Empty, "a TLS connect port changes, so it isn't remembered");
            });
        }

        [Test]
        public async Task It_reports_paired_but_not_connected_when_the_device_never_appears()
        {
            Returns(["pair", "192.168.1.20:37123"], $"Successfully paired to 192.168.1.20:37123 [guid={Guid}]\n");
            Returns(["devices"], "List of devices attached\n");
            Returns(["mdns", "services"], "List of discovered mdns services\n");

            var result = await CreateService().PairAsync("192.168.1.20:37123", "123456");

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
            Returns(["pair", "192.168.1.20:37123"], "Enter pairing code: ", exitCode: 1, stderr: "error: protocol fault (couldn't read status message): Success\n");

            var result = await CreateService().PairAsync("192.168.1.20:37123", "000000");

            Assert.That(result.Paired, Is.False);
            VerifyRan(["devices"], Times.Never());
        }
    }

    public class When_ReconnectSavedAsync_Is_Called : WirelessAdbServiceTests
    {
        [Test]
        public async Task It_connects_each_saved_address_and_counts_the_successes()
        {
            Saved = ["192.168.1.20:5555", "192.168.1.30:5555"];
            Returns(["connect", "192.168.1.20:5555"], "already connected to 192.168.1.20:5555\n");
            Returns(["connect", "192.168.1.30:5555"], "failed to connect to '192.168.1.30:5555': Connection refused\n");

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
            ProcessRunner.VerifyNoOtherCalls();
        }
    }
}
