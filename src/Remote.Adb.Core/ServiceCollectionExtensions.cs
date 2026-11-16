using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Adb.Wireless;
using Remote.Adb.Core.Common;
using Remote.Adb.Core.Diagnostics;
using Remote.Adb.Core.Emulators;
using Remote.Adb.Core.Settings;
using Remote.Adb.Core.Tunnel;

namespace Remote.Adb.Core;

/// <summary>
/// Registers the shared <c>Remote.Adb.Core</c> services so both front-ends wire up identically.
/// </summary>
[ExcludeFromCodeCoverage]
[PublicAPI]
public static class ServiceCollectionExtensions
{
    /// <summary>Registers every Core service both front-ends resolve.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddRemoteAdbCore(this IServiceCollection services)
    {
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IAdbRunner, AdbRunner>();
        services.AddSingleton<IAdbServerProcesses, AdbServerProcesses>();
        services.AddSingleton<IAdbServerService, AdbServerService>();
        services.AddSingleton<IAndroidSdk, AndroidSdk>();
        services.AddSingleton<IAvdCatalog, AvdCatalog>();
        services.AddSingleton<IAvdConfigStore, AvdConfigStore>();
        services.AddSingleton<IDeviceDetailsResolver, DeviceDetailsResolver>();
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IAvdProvisioningService, AvdProvisioningService>();
        services.AddSingleton<IEmulatorService, EmulatorService>();
        services.AddSingleton<IExecutableFinder, ExecutableFinder>();
        services.AddSingleton(provider => ListeningProcessFinder.Create(provider.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IPortProbe, TcpPortProbe>();
        services.AddSingleton<ISystemProcesses, SystemProcesses>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISdkDiagnostics, SdkDiagnostics>();
        services.AddSingleton<ISettingsStore>(provider => new SettingsStore(
            provider.GetRequiredService<ILogger<SettingsStore>>(),
            SettingsStore.ResolveFilePath(Environment.GetEnvironmentVariable)));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ITunnelService, TunnelService>();
        services.AddSingleton<IWirelessAdbService, WirelessAdbService>();

        return services;
    }
}
