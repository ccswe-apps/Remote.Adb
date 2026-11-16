using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Adb;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class DeviceServiceTests
{
    private protected AdbRunnerMock Adb = null!;
    private protected Mock<IDeviceDetailsResolver> DetailsResolver = null!;

    private protected DeviceService CreateService() => new(Adb.Object, DetailsResolver.Object);

    private protected void DevicesReturn(string standardOutput, int exitCode = 0) => Adb.Returns(["devices", "-l"], standardOutput, exitCode);

    [SetUp]
    public void SetUp()
    {
        Adb = new AdbRunnerMock();
        DetailsResolver = new Mock<IDeviceDetailsResolver>();
    }

    public class When_ListAsync_Is_Called : DeviceServiceTests
    {
        [Test]
        public async Task It_applies_resolved_details_to_online_devices()
        {
            DevicesReturn("List of devices attached\nABC123\tdevice transport_id:1\n");
            DetailsResolver
                .Setup(resolver => resolver.ResolveAsync(It.Is<AdbDevice>(device => device.Serial == "ABC123" && device.TransportId == "1"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeviceDetails("Pixel 9", DeviceForm.Phone, false, 35, "arm64-v8a"));

            var device = (await CreateService().ListAsync()).Single();

            Assert.Multiple(() =>
            {
                Assert.That(device.Name, Is.EqualTo("Pixel 9"));
                Assert.That(device.Form, Is.EqualTo(DeviceForm.Phone));
                Assert.That(device.IsEmulator, Is.False);
                Assert.That(device.ApiLevel, Is.EqualTo(35));
                Assert.That(device.Abi, Is.EqualTo("arm64-v8a"));
            });
        }

        [Test]
        public async Task It_does_not_evict_details_when_the_listing_fails()
        {
            DevicesReturn(string.Empty, exitCode: 1);

            await CreateService().ListAsync();

            DetailsResolver.Verify(resolver => resolver.Retain(It.IsAny<IReadOnlyCollection<AdbDevice>>()), Times.Never);
        }

        [Test]
        public async Task It_does_not_resolve_details_for_offline_devices()
        {
            DevicesReturn("List of devices attached\nABC123\toffline\n");

            await CreateService().ListAsync();

            DetailsResolver.Verify(resolver => resolver.ResolveAsync(It.IsAny<AdbDevice>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_retains_details_only_for_the_devices_listed_online()
        {
            DevicesReturn("List of devices attached\nABC123\tdevice transport_id:1\nDEF456\toffline transport_id:2\n");

            await CreateService().ListAsync();

            DetailsResolver.Verify(
                resolver => resolver.Retain(It.Is<IReadOnlyCollection<AdbDevice>>(devices => devices.Select(device => device.Serial).SequenceEqual(new[] { "ABC123" }))),
                Times.Once);
        }

        [Test]
        public async Task It_runs_adb_devices_long_and_parses_the_output()
        {
            DevicesReturn("List of devices attached\nemulator-5554\tdevice model:Pixel_7 transport_id:1\n");

            var devices = await CreateService().ListAsync();

            Adb.Verify(["devices", "-l"], Times.Once());
            Assert.That(devices.Single().Model, Is.EqualTo("Pixel_7"));
        }
    }
}
