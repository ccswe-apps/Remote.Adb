using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Logging;

/// <summary>
/// Registers the in-memory <see cref="ILogStore"/> as a logging provider. Kept separate from
/// <c>AddRemoteAdbCore()</c> because choosing logging providers is the host's decision.
/// </summary>
[ExcludeFromCodeCoverage]
[PublicAPI]
public static class LoggingBuilderExtensions
{
    /// <summary>Adds the in-memory log store provider, capturing <c>Remote.Adb</c> logging from <paramref name="minimumLevel"/> up.</summary>
    /// <param name="builder">The logging builder to add to.</param>
    /// <param name="minimumLevel">The lowest level captured for the app's own categories.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static ILoggingBuilder AddRemoteAdbLogStore(this ILoggingBuilder builder, LogLevel minimumLevel = LogLevel.Information)
    {
        builder.Services.TryAddSingleton<LogStore>();
        builder.Services.TryAddSingleton<ILogStore>(provider => provider.GetRequiredService<LogStore>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, LogStoreLoggerProvider>());

        // Provider-specific rules: the app's own categories at the requested level, everything else (framework,
        // hosting) only from Warning up.
        builder.AddFilter<LogStoreLoggerProvider>(null, LogLevel.Warning);
        builder.AddFilter<LogStoreLoggerProvider>("Remote.Adb", minimumLevel);

        return builder;
    }
}
