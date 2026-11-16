using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Core.Settings;

namespace Remote.Adb.Core.UnitTests.Settings;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SettingsServiceTests
{
    private protected SettingsStoreFake Store = null!;

    [SetUp]
    public void SetUp()
    {
        Store = new SettingsStoreFake();
    }

    private protected SettingsService CreateService() => new(Store);

    public class When_A_Setting_Is_Changed : SettingsServiceTests
    {
        [Test]
        public void It_persists_a_changed_theme_via_the_store()
        {
            var service = CreateService();

            service.Theme = AppTheme.Light;

            Assert.Multiple(() =>
            {
                Assert.That(Store.SaveCount, Is.EqualTo(1));
                Assert.That(Store.Saved!.Theme, Is.EqualTo(AppTheme.Light));
            });
        }

        [Test]
        public void It_persists_a_changed_density_via_the_store()
        {
            var service = CreateService();

            service.Density = AppDensity.Normal;

            Assert.Multiple(() =>
            {
                Assert.That(Store.SaveCount, Is.EqualTo(1));
                Assert.That(Store.Saved!.Density, Is.EqualTo(AppDensity.Normal));
            });
        }

        [Test]
        public void It_persists_changed_ports_and_skips_unchanged_ones()
        {
            var service = CreateService();

            service.TunnelLocalPort = 5038;
            service.TunnelRemotePort = 5037;

            Assert.Multiple(() =>
            {
                Assert.That(Store.SaveCount, Is.EqualTo(1));
                Assert.That(service.TunnelLocalPort, Is.EqualTo(5038));
                Assert.That(Store.Saved!.TunnelRemotePort, Is.EqualTo(5037));
            });
        }

        [Test]
        public void It_does_not_save_when_the_value_is_unchanged()
        {
            Store.Model = new SettingsModel { Theme = AppTheme.Dark };
            var service = CreateService();

            service.Theme = AppTheme.Dark;

            Assert.That(Store.SaveCount, Is.EqualTo(0));
        }
    }

    public class When_An_Override_Is_Changed : SettingsServiceTests
    {
        [Test]
        public void It_trims_the_value()
        {
            var service = CreateService();

            service.SdkRoot = "  /opt/android-sdk  ";

            Assert.That(service.SdkRoot, Is.EqualTo("/opt/android-sdk"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void It_stores_a_blank_value_as_no_override(string? value)
        {
            Store.Model = new SettingsModel { TunnelHost = "devhost" };
            var service = CreateService();

            service.TunnelHost = value;

            Assert.Multiple(() =>
            {
                Assert.That(service.TunnelHost, Is.Null);
                Assert.That(Store.SaveCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void It_does_not_save_when_the_normalized_value_is_unchanged()
        {
            Store.Model = new SettingsModel { JavaHome = "/jdk" };
            var service = CreateService();

            service.JavaHome = " /jdk ";

            Assert.That(Store.SaveCount, Is.EqualTo(0));
        }
    }

    public class When_Constructed : SettingsServiceTests
    {
        [Test]
        public void It_loads_the_persisted_settings_from_the_store()
        {
            Store.Model = new SettingsModel { Theme = AppTheme.Light, Density = AppDensity.Normal };

            var service = CreateService();

            Assert.Multiple(() =>
            {
                Assert.That(service.Theme, Is.EqualTo(AppTheme.Light));
                Assert.That(service.Density, Is.EqualTo(AppDensity.Normal));
            });
        }
    }

    public class When_UpdateWirelessAddresses_Is_Called : SettingsServiceTests
    {
        [Test]
        public void It_applies_the_update_to_the_current_list_and_saves()
        {
            Store.Model = new SettingsModel { WirelessAddresses = ["a:5555"] };
            var service = CreateService();

            service.UpdateWirelessAddresses(saved => [.. saved, "b:5555"]);

            Assert.Multiple(() =>
            {
                Assert.That(service.WirelessAddresses, Is.EqualTo(new[] { "a:5555", "b:5555" }));
                Assert.That(Store.SaveCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void It_does_not_save_when_the_update_changes_nothing()
        {
            Store.Model = new SettingsModel { WirelessAddresses = ["a:5555"] };
            var service = CreateService();

            service.UpdateWirelessAddresses(saved => saved);

            Assert.That(Store.SaveCount, Is.EqualTo(0));
        }

        [Test]
        public async Task It_keeps_every_concurrent_update()
        {
            var service = CreateService();

            await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => service.UpdateWirelessAddresses(saved => [.. saved, $"10.0.0.{i}:5555"]))));

            Assert.That(service.WirelessAddresses, Has.Count.EqualTo(50));
        }
    }

    public class When_WirelessAddresses_Is_Set : SettingsServiceTests
    {
        [Test]
        public void It_stores_a_copy_of_the_list()
        {
            var service = CreateService();
            var addresses = new List<string> { "a:5555" };

            service.WirelessAddresses = addresses;
            addresses.Add("b:5555");

            Assert.That(service.WirelessAddresses, Is.EqualTo(new[] { "a:5555" }));
        }

        [Test]
        public void It_does_not_save_an_equal_sequence()
        {
            Store.Model = new SettingsModel { WirelessAddresses = ["a:5555", "b:5555"] };
            var service = CreateService();

            service.WirelessAddresses = new List<string> { "a:5555", "b:5555" };

            Assert.That(Store.SaveCount, Is.EqualTo(0));
        }
    }

    private protected sealed class SettingsStoreFake : ISettingsStore
    {
        private int _saveCount;

        public SettingsModel Model { get; set; } = new();

        public int SaveCount => Volatile.Read(ref _saveCount);

        public SettingsModel? Saved { get; private set; }

        public SettingsModel Load() => Model;

        public void Save(SettingsModel settings)
        {
            Saved = settings;
            Interlocked.Increment(ref _saveCount);
        }
    }
}
