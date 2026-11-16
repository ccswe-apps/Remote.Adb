using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Settings;
using Remote.Adb.Desktop.Settings;
using Remote.Adb.Desktop.Theming;

namespace Remote.Adb.Desktop.UnitTests.Settings;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SettingsViewModelTests
{
    private protected Mock<IAndroidSdk> AndroidSdk = null!;
    private protected Mock<IDensityApplier> DensityApplier = null!;

    // A folder that exists on every machine the tests run on.
    private protected string ExistingFolder = null!;
    private protected Mock<ISettingsService> Settings = null!;
    private protected Mock<IThemeApplier> ThemeApplier = null!;

    [SetUp]
    public void SetUp()
    {
        AndroidSdk = new Mock<IAndroidSdk>();
        DensityApplier = new Mock<IDensityApplier>();
        ExistingFolder = TestContext.CurrentContext.WorkDirectory;
        Settings = new Mock<ISettingsService>();
        Settings.SetupAllProperties();
        ThemeApplier = new Mock<IThemeApplier>();
    }

    private protected SettingsViewModel CreateViewModel() => new(Settings.Object, AndroidSdk.Object, ThemeApplier.Object, DensityApplier.Object);

    public class When_A_Path_Override_Is_Set : SettingsViewModelTests
    {
        [Test]
        public void It_persists_an_existing_folder_trimmed()
        {
            var viewModel = CreateViewModel();

            viewModel.SdkRoot.Value = $"  {ExistingFolder}  ";

            Assert.Multiple(() =>
            {
                Assert.That(Settings.Object.SdkRoot, Is.EqualTo(ExistingFolder));
                Assert.That(viewModel.SdkRoot.Value, Is.EqualTo(ExistingFolder));
                Assert.That(viewModel.SdkRoot.Error, Is.Null);
            });
        }

        [Test]
        public void It_clears_the_override_when_blank()
        {
            Settings.Object.JavaHome = ExistingFolder;
            var viewModel = CreateViewModel();

            viewModel.JavaHome.Value = "   ";

            Assert.Multiple(() =>
            {
                Assert.That(Settings.Object.JavaHome, Is.Null);
                Assert.That(viewModel.JavaHome.Value, Is.Null);
            });
        }

        [Test]
        public void It_rejects_a_missing_folder_without_persisting_it()
        {
            Settings.Object.AvdHome = ExistingFolder;
            var viewModel = CreateViewModel();
            var missing = Path.Combine(ExistingFolder, "definitely-missing-4f1c");

            viewModel.AvdHome.Value = missing;

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.AvdHome.Error, Is.EqualTo("That folder doesn't exist."));
                Assert.That(viewModel.AvdHome.Value, Is.EqualTo(missing));
                Assert.That(Settings.Object.AvdHome, Is.EqualTo(ExistingFolder));
            });
        }

        [Test]
        public void It_refreshes_the_status_readout_after_saving()
        {
            AndroidSdk.Setup(sdk => sdk.SdkRoot).Returns(@"C:\sdk");
            AndroidSdk.Setup(sdk => sdk.SdkRootSource).Returns(SdkRootSource.Override);
            var viewModel = CreateViewModel();
            var changed = new List<string?>();
            viewModel.SdkRoot.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            viewModel.SdkRoot.Value = ExistingFolder;

            Assert.Multiple(() =>
            {
                Assert.That(changed, Does.Contain(nameof(PathOverrideField.Status)));
                Assert.That(viewModel.SdkRoot.Status, Is.EqualTo(@"Using C:\sdk — from your override"));
            });
        }

        [Test]
        public void It_starts_from_the_persisted_overrides()
        {
            Settings.Object.SdkRoot = "sdk";
            Settings.Object.AvdHome = "avd";
            Settings.Object.JavaHome = "jdk";

            var viewModel = CreateViewModel();

            Assert.That(
                new[] { viewModel.SdkRoot.Value, viewModel.AvdHome.Value, viewModel.JavaHome.Value },
                Is.EqualTo(new[] { "sdk", "avd", "jdk" }));
        }
    }

    public class When_A_Toggle_Changes : SettingsViewModelTests
    {
        [Test]
        public void It_applies_and_persists_the_theme()
        {
            Settings.Object.Theme = AppTheme.Dark;
            var viewModel = CreateViewModel();

            viewModel.IsLightTheme = true;

            Assert.That(Settings.Object.Theme, Is.EqualTo(AppTheme.Light));
            ThemeApplier.Verify(applier => applier.Apply(AppTheme.Light), Times.Once);
        }

        [Test]
        public void It_applies_and_persists_the_density()
        {
            Settings.Object.Density = AppDensity.Normal;
            var viewModel = CreateViewModel();

            viewModel.IsCompactDensity = true;

            Assert.That(Settings.Object.Density, Is.EqualTo(AppDensity.Compact));
            DensityApplier.Verify(applier => applier.Apply(AppDensity.Compact), Times.Once);
        }

        [Test]
        public void It_does_nothing_when_the_value_is_unchanged()
        {
            Settings.Object.Theme = AppTheme.Light;
            var viewModel = CreateViewModel();

            viewModel.IsLightTheme = true;

            ThemeApplier.Verify(applier => applier.Apply(It.IsAny<AppTheme>()), Times.Never);
        }
    }
}
