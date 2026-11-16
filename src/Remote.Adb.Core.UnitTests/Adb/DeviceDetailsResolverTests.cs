using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Common;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Adb;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class DeviceDetailsResolverTests
{
    private protected AdbRunnerMock Adb = null!;

    private protected DeviceDetailsResolver CreateResolver() => new(Adb.Object);

    private protected static AdbDevice Device(string serial = "ABC123", string? transportId = "1") =>
        new(serial, "device", null, null, null, transportId);

    [SetUp]
    public void SetUp()
    {
        Adb = new AdbRunnerMock();
    }

    public class When_ResolveAsync_Is_Called : DeviceDetailsResolverTests
    {
        [Test]
        public async Task It_caches_by_connection_and_does_not_re_query()
        {
            Adb.ReturnsForAny("ro.product.model=SM-S921B\n");
            var resolver = CreateResolver();

            await resolver.ResolveAsync(Device());
            await resolver.ResolveAsync(Device());

            Adb.VerifyAny(Times.Once());
        }

        [Test]
        public async Task It_re_resolves_a_serial_reused_by_a_new_transport()
        {
            Adb.ReturnsForAny("ro.boot.qemu.avd_name=Pixel_9\n");
            var resolver = CreateResolver();
            await resolver.ResolveAsync(Device("emulator-5554", "1"));
            Adb.ReturnsForAny("ro.boot.qemu.avd_name=Pixel_Tablet\n");

            var details = await resolver.ResolveAsync(Device("emulator-5554", "7"));

            Assert.That(details!.Name, Is.EqualTo("Pixel Tablet"));
            Adb.VerifyAny(Times.Exactly(2));
        }

        [Test]
        public async Task It_reads_properties_and_builds_details()
        {
            Adb.ReturnsForAny("ro.product.marketing.name=Pixel 9\n");

            var details = await CreateResolver().ResolveAsync(Device());

            Assert.That(details!.Name, Is.EqualTo("Pixel 9"));
        }

        [Test]
        public async Task It_returns_null_when_adb_cannot_be_launched()
        {
            Adb.ThrowsForAny(new ProcessLaunchException("adb", new InvalidOperationException()));

            var details = await CreateResolver().ResolveAsync(Device());

            Assert.That(details, Is.Null);
        }

        [Test]
        public async Task It_returns_null_when_the_shell_exits_nonzero()
        {
            Adb.ReturnsForAny(string.Empty, exitCode: 1, stderr: "error: device offline");

            var details = await CreateResolver().ResolveAsync(Device());

            Assert.That(details, Is.Null);
        }
    }

    public class When_Retain_Is_Called : DeviceDetailsResolverTests
    {
        [Test]
        public async Task It_evicts_connections_no_longer_online_and_keeps_the_rest()
        {
            Adb.ReturnsForAny("ro.product.model=SM-S921B\n");
            var resolver = CreateResolver();
            await resolver.ResolveAsync(Device("ABC123"));
            await resolver.ResolveAsync(Device("192.168.1.20:5555", "2"));

            resolver.Retain([Device("ABC123")]);
            await resolver.ResolveAsync(Device("ABC123"));
            await resolver.ResolveAsync(Device("192.168.1.20:5555", "2"));

            Adb.Inner.Verify(runner => runner.RunAsync(It.Is<IReadOnlyList<string>>(args => args[1] == "ABC123"), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
            Adb.VerifyAny(Times.Exactly(3));
        }
    }
}
