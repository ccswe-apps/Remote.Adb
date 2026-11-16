using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Moq;
using Remote.Adb.Desktop.Common.Diagnostics;
using Remote.Adb.Desktop.Common.Notifications;
using Remote.Adb.Testing;

namespace Remote.Adb.Desktop.UnitTests.Common.Diagnostics;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class UnhandledExceptionReporterTests
{
    public class When_Report_Is_Called : UnhandledExceptionReporterTests
    {
        [Test]
        public void It_logs_the_exception_and_shows_an_error_toast()
        {
            var logger = new RecordingLogger<UnhandledExceptionReporter>();
            var notifications = new Mock<INotificationService>();
            var reporter = new UnhandledExceptionReporter(notifications.Object, logger);
            var exception = new InvalidOperationException("boom");

            reporter.Report(exception);

            Assert.Multiple(() =>
            {
                Assert.That(logger.Entries.Single().Level, Is.EqualTo(LogLevel.Error));
                Assert.That(logger.Entries.Single().Exception, Is.SameAs(exception));
            });
            notifications.Verify(
                service => service.Show(It.IsAny<string>(), It.Is<string>(message => message.Contains("boom")), NotificationSeverity.Error, It.IsAny<TimeSpan?>(), It.IsAny<Action?>()),
                Times.Once);
        }
    }
}
