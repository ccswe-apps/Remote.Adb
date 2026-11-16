using Microsoft.Extensions.Logging;
using Remote.Adb.Desktop.Common.Notifications;

namespace Remote.Adb.Desktop.Common.Diagnostics;

/// <inheritdoc />
public sealed class UnhandledExceptionReporter : IUnhandledExceptionReporter
{
    private readonly ILogger<UnhandledExceptionReporter> _logger;
    private readonly INotificationService _notifications;

    public UnhandledExceptionReporter(INotificationService notifications, ILogger<UnhandledExceptionReporter> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Report(Exception exception)
    {
        _logger.LogError(exception, "Unhandled exception.");
        _notifications.Show("Something went wrong", $"{exception.Message} See the log for details.", NotificationSeverity.Error);
    }
}
