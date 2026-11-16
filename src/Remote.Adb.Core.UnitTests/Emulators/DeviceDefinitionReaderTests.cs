using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Remote.Adb.Core.Emulators;

namespace Remote.Adb.Core.UnitTests.Emulators;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class DeviceDefinitionReaderTests
{
    private protected string SdkRoot = string.Empty;
    private protected string TempDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "remote-adb-devices-" + Guid.NewGuid().ToString("N"));
        SdkRoot = Path.Combine(TempDirectory, "sdk");
        Directory.CreateDirectory(SdkRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, true);
        }
    }

    private protected static string DevicesXml(params (string Id, string Name)[] devices) =>
        "<d:devices xmlns:d=\"http://schemas.android.com/sdk/devices/3\">"
        + string.Concat(devices.Select(device => $"<d:device><d:name>{device.Name}</d:name><d:id>{device.Id}</d:id><d:manufacturer>Google</d:manufacturer></d:device>"))
        + "</d:devices>";

    // A cmdline-tools sdklib jar carrying the device XML as an embedded resource, as the real SDK ships it.
    private protected void WriteSdkLibJar(string version, string xml)
    {
        var directory = Path.Combine(SdkRoot, "cmdline-tools", version, "lib", "sdklib");
        Directory.CreateDirectory(directory);

        using var archive = ZipFile.Open(Path.Combine(directory, "sdklib.core.jar"), ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("com/android/sdklib/devices/nexus.xml").Open()))
        {
            writer.Write(xml);
        }

        using (var writer = new StreamWriter(archive.CreateEntry("com/android/sdklib/Other.class").Open()))
        {
            writer.Write("not xml");
        }
    }

    private protected string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(TempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public class When_Read_Is_Called : DeviceDefinitionReaderTests
    {
        [Test]
        public void It_reads_devices_embedded_in_the_sdklib_jars()
        {
            WriteSdkLibJar("latest", DevicesXml(("pixel_8", "Pixel 8")));

            var devices = DeviceDefinitionReader.Read(SdkRoot, userDevicesXmlPath: null);

            Assert.That(devices.Select(device => device.Id), Is.EqualTo(new[] { "pixel_8" }));
        }

        [Test]
        public void It_falls_back_to_the_legacy_catalog_without_embedded_devices()
        {
            WriteFile(Path.Combine("sdk", "tools", "lib", "devices.xml"), DevicesXml(("legacy", "Legacy")));

            var devices = DeviceDefinitionReader.Read(SdkRoot, userDevicesXmlPath: null);

            Assert.That(devices.Select(device => device.Id), Is.EqualTo(new[] { "legacy" }));
        }

        [Test]
        public void It_ignores_the_legacy_catalog_when_embedded_devices_exist()
        {
            WriteSdkLibJar("latest", DevicesXml(("pixel_8", "Pixel 8")));
            WriteFile(Path.Combine("sdk", "tools", "lib", "devices.xml"), DevicesXml(("legacy", "Legacy")));

            var devices = DeviceDefinitionReader.Read(SdkRoot, userDevicesXmlPath: null);

            Assert.That(devices.Select(device => device.Id), Is.EqualTo(new[] { "pixel_8" }));
        }

        [Test]
        public void It_lets_user_devices_override_built_ins_with_the_same_id()
        {
            WriteSdkLibJar("latest", DevicesXml(("pixel_8", "Pixel 8"), ("pixel_9", "Pixel 9")));
            var userXml = WriteFile("devices.xml", DevicesXml(("pixel_8", "My Pixel 8")));

            var devices = DeviceDefinitionReader.Read(SdkRoot, userXml);

            Assert.Multiple(() =>
            {
                Assert.That(devices.Select(device => device.Id), Is.EqualTo(new[] { "pixel_8", "pixel_9" }));
                Assert.That(devices[0].Name, Is.EqualTo("My Pixel 8"));
            });
        }

        [Test]
        public void It_skips_a_corrupt_jar()
        {
            WriteFile(Path.Combine("sdk", "cmdline-tools", "latest", "lib", "sdklib", "broken.jar"), "not a zip");

            var devices = DeviceDefinitionReader.Read(SdkRoot, userDevicesXmlPath: null);

            Assert.That(devices, Is.Empty);
        }

        [Test]
        public void It_reads_only_user_devices_without_an_sdk_root()
        {
            var userXml = WriteFile("devices.xml", DevicesXml(("custom", "Custom")));

            var devices = DeviceDefinitionReader.Read(sdkRoot: null, userXml);

            Assert.That(devices.Select(device => device.Id), Is.EqualTo(new[] { "custom" }));
        }
    }
}
