using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Remote.Adb.Core.Logging;

/// <summary>
/// Registers the in-memory <see cref="ILogStore"/> as a logging provider. Kept separate from
/// <c>AddRemoteAdbCore()</c> because choosing logging providers is the host's decision.
/// </summary>
[ExcludeFromCodeCoverage]
public static class LoggingBuilderExtensions
{
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
