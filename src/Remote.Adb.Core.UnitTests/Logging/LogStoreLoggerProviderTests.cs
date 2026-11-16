using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Remote.Adb.Core.Logging;

namespace Remote.Adb.Core.UnitTests.Logging;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class LogStoreLoggerProviderTests
{
    public class When_A_Logger_Logs : LogStoreLoggerProviderTests
    {
        [Test]
        public void It_records_the_category_event_and_formatted_message()
        {
            var store = new LogStore();
            var logger = new LogStoreLoggerProvider(store).CreateLogger("Remote.Adb.Core.Common.ProcessRunner");

            logger.Log(LogLevel.Warning, new EventId(LogEvents.CommandCompleted), "adb exited with code {Code}", 1);

            var entry = store.Snapshot().Single();
            Assert.Multiple(() =>
            {
                Assert.That(entry.Category, Is.EqualTo("Remote.Adb.Core.Common.ProcessRunner"));
                Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
                Assert.That(entry.Message, Is.EqualTo("adb exited with code 1"));
                Assert.That(entry.IsCommand, Is.True);
            });
        }

        [Test]
        public void It_captures_the_exception_text()
        {
            var store = new LogStore();
            var logger = new LogStoreLoggerProvider(store).CreateLogger("Category");

            logger.LogError(new InvalidOperationException("boom"), "failed");

            Assert.That(store.Snapshot().Single().Exception, Does.Contain("boom"));
        }
    }
}
