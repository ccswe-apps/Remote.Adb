using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Emulators;
using Remote.Adb.Desktop.Devices;

namespace Remote.Adb.Desktop.UnitTests.Devices;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class DeviceRowViewModelTests
{
    private protected static DeviceRowViewModel PhysicalRow(AdbDevice device) =>
        new(device, Mock.Of<ICommand>(), Mock.Of<ICommand>());

    public class When_Update_Is_Called_With_An_AdbDevice : DeviceRowViewModelTests
    {
        [Test]
        public void It_summarizes_the_resolved_version_and_abi()
        {
            var row = PhysicalRow(new AdbDevice("s1", "device", "Pixel", "oriole", "oriole", "1") { ApiLevel = 34, Abi = "arm64-v8a" });

            Assert.Multiple(() =>
            {
                Assert.That(row.Summary, Does.StartWith("Android"));
                Assert.That(row.Summary, Does.EndWith(" · arm64-v8a"));
            });
        }

        [Test]
        public void It_falls_back_to_the_product_and_device_columns_when_the_properties_are_unknown()
        {
            var row = PhysicalRow(new AdbDevice("s1", "offline", null, "oriole", "oriole_dev", null));

            Assert.That(row.Summary, Is.EqualTo("oriole · oriole_dev"));
        }

        [Test]
        public void It_is_empty_when_nothing_is_known()
        {
            var row = PhysicalRow(new AdbDevice("s1", "offline", null, null, null, null));

            Assert.That(row.Summary, Is.Empty);
        }

        [Test]
        public void It_names_the_row_by_model_then_serial()
        {
            var withModel = PhysicalRow(new AdbDevice("s1", "device", "Pixel 8", null, null, null));
            var withoutModel = PhysicalRow(new AdbDevice("s2", "device", null, null, null, null));

            Assert.Multiple(() =>
            {
                Assert.That(withModel.DisplayName, Is.EqualTo("Pixel 8"));
                Assert.That(withoutModel.DisplayName, Is.EqualTo("s2"));
            });
        }
    }

    public class When_KeyFor_Is_Called : DeviceRowViewModelTests
    {
        [Test]
        public void It_matches_the_identity_key_of_the_row_it_builds()
        {
            var avd = new AndroidVirtualDevice("Pixel_8", "Pixel 8", null, false, null);
            var device = new AdbDevice("s1", "device", null, null, null, null);

            var virtualRow = new DeviceRowViewModel(avd, Mock.Of<ICommand>(), Mock.Of<ICommand>());

            Assert.Multiple(() =>
            {
                Assert.That(DeviceRowViewModel.KeyFor(avd), Is.EqualTo(virtualRow.IdentityKey));
                Assert.That(DeviceRowViewModel.KeyFor(device), Is.EqualTo(PhysicalRow(device).IdentityKey));
            });
        }
    }
}
