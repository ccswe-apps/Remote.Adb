using Remote.Adb.Core.Common;
using Remote.Adb.Core.Emulators;
using Remote.Adb.Core.Settings;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Theming;
using AvdHomeLocator = Remote.Adb.Core.Emulators.AvdHome;

namespace Remote.Adb.Desktop.Settings;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IDensityApplier _densityApplier;
    private readonly ISettingsService _settings;
    private readonly IThemeApplier _themeApplier;

    public SettingsViewModel(ISettingsService settings, IAndroidSdk androidSdk, IThemeApplier themeApplier, IDensityApplier densityApplier)
    {
        _settings = settings;
        _themeApplier = themeApplier;
        _densityApplier = densityApplier;

        SdkRoot = new PathOverrideField(
            "SDK path (ANDROID_HOME)",
            "Leave blank to auto-detect",
            settings.SdkRoot,
            value => settings.SdkRoot = value,
            () => $"Using {androidSdk.SdkRoot ?? "(not found)"} — {DescribeSource(androidSdk.SdkRootSource)}");
        AvdHome = new PathOverrideField(
            "AVD home (ANDROID_AVD_HOME)",
            "Leave blank to auto-detect",
            settings.AvdHome,
            value => settings.AvdHome = value,
            () => $"Using {AvdHomeLocator.Resolve(settings.AvdHome) ?? "(not found)"}");
        JavaHome = new PathOverrideField(
            "JDK path (JAVA_HOME)",
            "Leave blank to use JAVA_HOME / PATH",
            settings.JavaHome,
            value => settings.JavaHome = value,
            () => $"Using {androidSdk.JavaHome ?? "java on PATH"}");
    }

    public PathOverrideField AvdHome { get; }

    // Reads through to the settings service rather than caching, so a recreated view always reflects the live
    // density — the toggle can never desync from what is actually applied.
    public bool IsCompactDensity
    {
        get => _settings.Density == AppDensity.Compact;
        set
        {
            var density = value ? AppDensity.Compact : AppDensity.Normal;
            if (_settings.Density == density)
            {
                return;
            }

            _settings.Density = density;
            _densityApplier.Apply(density);
            OnPropertyChanged();
        }
    }

    // Reads through to the settings service rather than caching, so a recreated view always reflects the live
    // theme — the toggle can never desync from what is actually applied.
    public bool IsLightTheme
    {
        get => _settings.Theme == AppTheme.Light;
        set
        {
            var theme = value ? AppTheme.Light : AppTheme.Dark;
            if (_settings.Theme == theme)
            {
                return;
            }

            _settings.Theme = theme;
            _themeApplier.Apply(theme);
            OnPropertyChanged();
        }
    }

    public PathOverrideField JavaHome { get; }

    public PathOverrideField SdkRoot { get; }

    private static string DescribeSource(SdkRootSource source) => source switch
    {
        SdkRootSource.Override => "from your override",
        SdkRootSource.EnvironmentVariable => "from ANDROID_HOME",
        SdkRootSource.DefaultFallback => "default guess — set the path if this is the wrong SDK",
        _ => "not found — set the SDK path below",
    };
}
