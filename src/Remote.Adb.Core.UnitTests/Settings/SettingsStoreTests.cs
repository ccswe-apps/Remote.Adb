using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Settings;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Settings;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SettingsStoreTests
{
    private protected string TempDirectory = string.Empty;
    private protected string FilePath = string.Empty;
    private protected RecordingLogger<SettingsStore> Logger = null!;

    [SetUp]
    public void SetUp()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "remote-adb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
        FilePath = Path.Combine(TempDirectory, "settings.json");
        Logger = new RecordingLogger<SettingsStore>();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, true);
        }
    }

    private protected SettingsStore CreateStore(string? filePath = null) =>
        new(Logger, filePath ?? FilePath);

    public class When_ResolveFilePath_Is_Called : SettingsStoreTests
    {
        [Test]
        public void It_uses_the_override_when_set()
        {
            var path = SettingsStore.ResolveFilePath(name => name == SettingsStore.FilePathVariable ? FilePath : null);

            Assert.That(path, Is.EqualTo(FilePath));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void It_falls_back_to_the_default_when_unset_or_blank(string? value)
        {
            var path = SettingsStore.ResolveFilePath(_ => value);

            Assert.That(path, Is.EqualTo(SettingsStore.DefaultFilePath()));
        }

        [Test]
        public void It_makes_a_relative_override_absolute()
        {
            var path = SettingsStore.ResolveFilePath(_ => Path.Combine("scratch", "settings.json"));

            Assert.Multiple(() =>
            {
                Assert.That(Path.IsPathRooted(path), Is.True);
                Assert.That(path, Is.EqualTo(Path.GetFullPath(Path.Combine("scratch", "settings.json"))));
            });
        }
    }

    public class When_Load_Is_Called : SettingsStoreTests
    {
        [Test]
        public void It_returns_defaults_when_the_file_is_missing()
        {
            var settings = CreateStore().Load();

            Assert.That(settings.Theme, Is.EqualTo(AppTheme.Dark));
            Assert.That(settings.Density, Is.EqualTo(AppDensity.Compact));
        }

        [Test]
        public void It_returns_defaults_when_the_file_is_corrupt()
        {
            File.WriteAllText(FilePath, "{ not json");

            var settings = CreateStore().Load();

            Assert.Multiple(() =>
            {
                Assert.That(settings.Theme, Is.EqualTo(AppTheme.Dark));
                Assert.That(settings.Density, Is.EqualTo(AppDensity.Compact));
            });
        }

        [Test]
        public void It_backs_up_a_corrupt_file_and_warns()
        {
            File.WriteAllText(FilePath, "{ not json");
            var store = CreateStore();

            store.Load();

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(FilePath), Is.False);
                Assert.That(File.ReadAllText(store.BackupFilePath), Is.EqualTo("{ not json"));
                Assert.That(Logger.Entries, Has.Some.Matches<RecordedLog>(log => log is { Level: LogLevel.Warning } && log.Message.Contains("not valid JSON")));
            });
        }

        [Test]
        public void It_reads_a_null_wireless_address_list_as_empty()
        {
            File.WriteAllText(FilePath, "{\"wirelessAddresses\":null}");

            var settings = CreateStore().Load();

            Assert.That(settings.WirelessAddresses, Is.Empty);
        }

        [Test]
        public void It_returns_defaults_when_the_content_is_null()
        {
            File.WriteAllText(FilePath, "null");

            var settings = CreateStore().Load();

            Assert.That(settings.Theme, Is.EqualTo(AppTheme.Dark));
        }

        [Test]
        public void It_round_trips_a_saved_model()
        {
            CreateStore().Save(new SettingsModel { Theme = AppTheme.Light, Density = AppDensity.Normal });

            var settings = CreateStore().Load();

            Assert.That(settings.Theme, Is.EqualTo(AppTheme.Light));
            Assert.That(settings.Density, Is.EqualTo(AppDensity.Normal));
        }

        [Test]
        public void It_reads_enums_written_as_strings()
        {
            File.WriteAllText(FilePath, "{\"theme\":\"Light\",\"density\":\"Normal\"}");

            var settings = CreateStore().Load();

            Assert.That(settings.Theme, Is.EqualTo(AppTheme.Light));
            Assert.That(settings.Density, Is.EqualTo(AppDensity.Normal));
        }

        [Test]
        public void It_preserves_unknown_keys_across_a_resave()
        {
            File.WriteAllText(FilePath, "{\"theme\":\"Light\",\"futureKey\":123}");
            var store = CreateStore();

            store.Save(store.Load());

            Assert.That(File.ReadAllText(FilePath), Does.Contain("futureKey"));
        }
    }

    public class When_Save_Is_Called : SettingsStoreTests
    {
        [Test]
        public void It_creates_the_directory_when_it_does_not_exist()
        {
            var nestedPath = Path.Combine(TempDirectory, "nested", "settings.json");

            CreateStore(nestedPath).Save(new SettingsModel());

            Assert.That(File.Exists(nestedPath), Is.True);
        }

        [Test]
        public void It_writes_enums_as_strings()
        {
            CreateStore().Save(new SettingsModel { Theme = AppTheme.Light, Density = AppDensity.Normal });

            var json = File.ReadAllText(FilePath);
            Assert.That(json, Does.Contain("\"Light\""));
            Assert.That(json, Does.Contain("\"Normal\""));
        }

        [Test]
        public void It_warns_when_the_file_cannot_be_written()
        {
            // A directory where the file should be makes the final move fail.
            Directory.CreateDirectory(FilePath);

            CreateStore().Save(new SettingsModel());

            Assert.That(Logger.Entries, Has.Some.Matches<RecordedLog>(log => log is { Level: LogLevel.Warning } && log.Message.Contains("Could not write settings")));
        }

        [Test]
        public void It_overwrites_without_leaving_a_temp_file()
        {
            var store = CreateStore();
            store.Save(new SettingsModel { Theme = AppTheme.Dark });
            store.Save(new SettingsModel { Theme = AppTheme.Light });

            Assert.That(CreateStore().Load().Theme, Is.EqualTo(AppTheme.Light));
            Assert.That(File.Exists(FilePath + ".tmp"), Is.False);
        }
    }
}
