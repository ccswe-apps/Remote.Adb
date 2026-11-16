using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Common;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Adb;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class DeviceDetailsResolverTests
{
    private static DeviceDetailsResolver CreateResolver(AdbRunnerMock adb) => new(adb.Object);

    public class When_ResolveAsync_Is_Called : DeviceDetailsResolverTests
    {
        [Test]
        public async Task It_reads_properties_and_builds_details()
        {
            var adb = new AdbRunnerMock();
            adb.ReturnsForAny("ro.product.marketing.name=Pixel 9\n");

            var details = await CreateResolver(adb).ResolveAsync("ABC123");

            Assert.That(details!.Name, Is.EqualTo("Pixel 9"));
        }

        [Test]
        public async Task It_caches_by_serial_and_does_not_re_query()
        {
            var adb = new AdbRunnerMock();
            adb.ReturnsForAny("ro.product.model=SM-S921B\n");
            var resolver = CreateResolver(adb);

            await resolver.ResolveAsync("ABC123");
            await resolver.ResolveAsync("ABC123");

            adb.VerifyAny(Times.Once());
        }

        [Test]
        public async Task It_returns_null_when_the_shell_exits_nonzero()
        {
            var adb = new AdbRunnerMock();
            adb.ReturnsForAny(string.Empty, exitCode: 1, stderr: "error: device offline");

            var details = await CreateResolver(adb).ResolveAsync("ABC123");

            Assert.That(details, Is.Null);
        }

        [Test]
        public async Task It_returns_null_when_adb_cannot_be_launched()
        {
            var adb = new AdbRunnerMock();
            adb.ThrowsForAny(new ProcessLaunchException("adb", new InvalidOperationException()));

            var details = await CreateResolver(adb).ResolveAsync("ABC123");

            Assert.That(details, Is.Null);
        }
    }
}
