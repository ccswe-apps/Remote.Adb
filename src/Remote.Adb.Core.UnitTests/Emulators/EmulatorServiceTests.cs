using System.Diagnostics.CodeAnalysis;
using Moq;
using NUnit.Framework;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Emulators;
using Remote.Adb.Core.UnitTests.Fakes;

namespace Remote.Adb.Core.UnitTests.Emulators;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class EmulatorServiceTests
{
    private const string EmulatorPath = "emulator";

    private static EmulatorService CreateService(Mock<IProcessRunner> processRunner, AdbRunnerMock? adb = null, IReadOnlyDictionary<string, AvdMetadata>? catalog = null)
    {
        var sdk = new Mock<IAndroidSdk>();
        sdk.SetupGet(s => s.EmulatorPath).Returns(EmulatorPath);

        var avdCatalog = new Mock<IAvdCatalog>();
        avdCatalog.Setup(c => c.Read()).Returns(catalog ?? new Dictionary<string, AvdMetadata>());

        return new EmulatorService(processRunner.Object, (adb ?? new AdbRunnerMock()).Object, sdk.Object, avdCatalog.Object, new LoggerFake<EmulatorService>());
    }

    private static Mock<IProcessRunner> EmulatorListing(string avds)
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(r => r.RunAsync(EmulatorPath, It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "-list-avds" })), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, avds, string.Empty));

        return processRunner;
    }

    public class When_ListAsync_Is_Called : EmulatorServiceTests
    {
        [Test]
        public async Task It_marks_running_avds_with_their_serial()
        {
            var processRunner = EmulatorListing("Pixel_6_API_34\nNexus_5\n");
            var adb = new AdbRunnerMock();
            adb.Returns(["devices"], "List of devices attached\nemulator-5554\tdevice\n");
            adb.Returns(["-s", "emulator-5554", "emu", "avd", "name"], "Pixel_6_API_34\nOK\n");
            var service = CreateService(processRunner, adb);

            var devices = await service.ListAsync();

            var running = devices.Single(device => device.Name == "Pixel_6_API_34");
            var stopped = devices.Single(device => device.Name == "Nexus_5");

            Assert.That(devices, Has.Count.EqualTo(2));
            Assert.That(running.IsRunning, Is.True);
            Assert.That(running.Serial, Is.EqualTo("emulator-5554"));
            Assert.That(stopped.IsRunning, Is.False);
            Assert.That(stopped.Serial, Is.Null);
        }

        [Test]
        public async Task It_uses_the_catalog_display_name_and_falls_back_to_the_id()
        {
            var processRunner = EmulatorListing("Pixel_6_API_34\nNexus_5\n");
            var adb = new AdbRunnerMock();
            adb.Returns(["devices"], "List of devices attached\n");
            var catalog = new Dictionary<string, AvdMetadata>
            {
                ["Pixel_6_API_34"] = new("Pixel_6_API_34", "Pixel 6 API 34", "Phone"),
            };

            var service = CreateService(processRunner, adb, catalog);

            var devices = await service.ListAsync();

            var pixel = devices.Single(device => device.Name == "Pixel_6_API_34");
            var nexus = devices.Single(device => device.Name == "Nexus_5");

            Assert.That(pixel.DisplayName, Is.EqualTo("Pixel 6 API 34"));
            Assert.That(pixel.Tag, Is.EqualTo("Phone"));
            Assert.That(nexus.DisplayName, Is.EqualTo("Nexus_5"));
            Assert.That(nexus.Tag, Is.Null);
        }
    }

    public class When_StartAsync_Is_Called : EmulatorServiceTests
    {
        [Test]
        public async Task It_appends_no_snapshot_load_when_cold_booting()
        {
            var processRunner = new Mock<IProcessRunner>();

            var service = CreateService(processRunner);

            await service.StartAsync("Pixel_6_API_34", coldBoot: true);

            processRunner.Verify(
                r => r.Start(EmulatorPath, It.Is<IReadOnlyList<string>>(arguments => arguments.SequenceEqual(new[] { "-avd", "Pixel_6_API_34", "-no-snapshot-load" }))),
                Times.Once);
        }

        [Test]
        public async Task It_launches_the_emulator_with_the_avd_name()
        {
            var processRunner = new Mock<IProcessRunner>();

            var service = CreateService(processRunner);

            await service.StartAsync("Pixel_6_API_34");

            processRunner.Verify(
                r => r.Start(EmulatorPath, It.Is<IReadOnlyList<string>>(arguments => arguments.SequenceEqual(new[] { "-avd", "Pixel_6_API_34" }))),
                Times.Once);
        }
    }

    public class When_StopAsync_Is_Called : EmulatorServiceTests
    {
        [Test]
        public async Task It_invokes_adb_emu_kill_for_the_serial()
        {
            var adb = new AdbRunnerMock();
            adb.Returns(["-s", "emulator-5554", "emu", "kill"], string.Empty);
            var service = CreateService(new Mock<IProcessRunner>(), adb);

            await service.StopAsync("emulator-5554");

            adb.Verify(["-s", "emulator-5554", "emu", "kill"], Times.Once());
        }
    }
}
