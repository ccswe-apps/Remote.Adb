using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Remote.Adb.Core;
using Remote.Adb.Core.Emulators;
using Remote.Adb.Desktop.Common;
using Remote.Adb.Desktop.Common.Diagnostics;
using Remote.Adb.Desktop.Common.Notifications;
using Remote.Adb.Desktop.Common.Threading;
using Remote.Adb.Desktop.Devices;
using Remote.Adb.Desktop.Devices.Wireless;
using Remote.Adb.Desktop.Logging;
using Remote.Adb.Desktop.Settings;
using Remote.Adb.Desktop.Shell;
using Remote.Adb.Desktop.Theming;
using Remote.Adb.Desktop.Tunnel;

namespace Remote.Adb.Desktop;

/// <summary>
/// Registers the Desktop head's services: the shared Core services, theming/density appliers, dialogs, the page
/// view models and their DI-resolved views, and the device-catalog warm-up background service.
/// </summary>
[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRemoteAdbDesktop(this IServiceCollection services)
    {
        services.AddRemoteAdbCore();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IThemeApplier, ThemeApplier>();
        services.AddSingleton<IDensityApplier, DensityApplier>();
        services.AddSingleton<ITimerFactory, DispatcherTimerFactory>();
        services.AddSingleton<IUiDispatcher, DispatcherUiDispatcher>();
        services.AddSingleton<IClipboardService, TopLevelClipboardService>();
        services.AddSingleton<IConfirmDialog, ConfirmDialog>();
        services.AddSingleton<IAvdCreateDialog, AvdCreateDialog>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(provider => provider.GetRequiredService<NotificationService>());
        services.AddSingleton<IUnhandledExceptionReporter, UnhandledExceptionReporter>();

        // A fresh details VM per row, with its store resolved from DI and the configuration/back callback passed in.
        services.AddSingleton<AvdDetailsViewModelFactory>(provider =>
            (configuration, back) => new AvdDetailsViewModel(configuration, provider.GetRequiredService<IAvdConfigStore>(), back));

        // A fresh wizard VM per dialog open (the dialog service is a singleton, so it cannot capture a transient).
        services.AddTransient<CreateAvdViewModel>();
        services.AddTransient<Func<CreateAvdViewModel>>(provider => provider.GetRequiredService<CreateAvdViewModel>);

        // A fresh "Add wireless device" VM per dialog open (the dialog service is a singleton).
        services.AddSingleton<IWirelessDeviceDialog, WirelessDeviceDialog>();
        services.AddTransient<AddWirelessDeviceViewModel>();
        services.AddTransient<Func<AddWirelessDeviceViewModel>>(provider => provider.GetRequiredService<AddWirelessDeviceViewModel>);

        services.AddHostedService<DeviceCatalogWarmup>();
        services.AddHostedService<WirelessReconnect>();
        services.AddHostedService<TunnelAutoConnect>();

        services.AddTransient<MainWindow>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DevicesViewModel>();
        services.AddTransient<TunnelViewModel>();
        services.AddTransient<SettingsViewModel>();

        // One per app, for its lifetime: both subscribe to app-wide event sources (the log store, the tunnel) and
        // unsubscribe on Dispose, which the container does at shutdown.
        services.AddSingleton<LogPanelViewModel>();
        services.AddSingleton<StatusBarViewModel>();

        // The three page views the ViewLocator resolves through DI; every other view is instantiated by XAML.
        services.AddTransient<DevicesView>();
        services.AddTransient<TunnelView>();
        services.AddTransient<SettingsView>();

        return services;
    }
}
