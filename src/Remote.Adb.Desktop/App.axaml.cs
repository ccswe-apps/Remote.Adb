using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CCSWE.Avalonia.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Remote.Adb.Core.Settings;
using Remote.Adb.Desktop.Common.Diagnostics;
using Remote.Adb.Desktop.Shell;
using Remote.Adb.Desktop.Theming;

namespace Remote.Adb.Desktop;

[ExcludeFromCodeCoverage]
public partial class App : Application, IServiceProviderAccessor
{
    /// <summary>The host's service provider, set by the host before framework initialization completes. Null at
    /// design time (the previewer constructs <see cref="App"/> without a host), so composition is skipped then.</summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>Receives the built provider from <see cref="IServiceProviderAccessor"/> (the host's injection seam).</summary>
    IServiceProvider IServiceProviderAccessor.Services
    {
        set => Services = value;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (Services is null)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // The safety net: an exception nothing else caught — a command that threw on the UI thread, or a faulted task
        // nobody observed — is logged and shown as a toast rather than crashing the app or vanishing silently.
        var reporter = Services.GetRequiredService<IUnhandledExceptionReporter>();
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            reporter.Report(args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            reporter.Report(args.Exception.GetBaseException());
            args.SetObserved();
        };

        // Apply the persisted theme and density now that Avalonia's styles are loaded.
        var settings = Services.GetRequiredService<ISettingsService>();
        Services.GetRequiredService<IThemeApplier>().Apply(settings.Theme);
        Services.GetRequiredService<IDensityApplier>().Apply(settings.Density);

        // Register the DI-backed view locator before the main window binds its content area.
        DataTemplates.Add(new ViewLocator(Services));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
