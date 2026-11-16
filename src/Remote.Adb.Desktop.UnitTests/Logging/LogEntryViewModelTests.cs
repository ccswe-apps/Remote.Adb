using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Remote.Adb.Core.Logging;
using Remote.Adb.Desktop.Logging;

namespace Remote.Adb.Desktop.UnitTests.Logging;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class LogEntryViewModelTests
{
    private protected static LogEntryViewModel Create(LogLevel level = LogLevel.Information, string category = "Remote.Adb.Core.Common.ProcessRunner", string? exception = null) =>
        new(new LogEntry(1, new DateTimeOffset(2026, 1, 2, 13, 4, 5, 678, TimeSpan.Zero), level, category, new EventId(0), "hello", exception));

    public class When_Projected : LogEntryViewModelTests
    {
        [TestCase(LogLevel.Information, "INF", false, false)]
        [TestCase(LogLevel.Warning, "WRN", true, false)]
        [TestCase(LogLevel.Error, "ERR", false, true)]
        [TestCase(LogLevel.Critical, "CRT", false, true)]
        public void It_labels_and_flags_the_level(LogLevel level, string label, bool isWarning, bool isError)
        {
            var entry = Create(level);

            Assert.Multiple(() =>
            {
                Assert.That(entry.LevelLabel, Is.EqualTo(label));
                Assert.That(entry.IsWarning, Is.EqualTo(isWarning));
                Assert.That(entry.IsError, Is.EqualTo(isError));
            });
        }

        [TestCase("Remote.Adb.Core.Common.ProcessRunner", "ProcessRunner")]
        [TestCase("NoNamespace", "NoNamespace")]
        public void It_shows_the_last_segment_of_the_category(string category, string expected)
        {
            Assert.That(Create(category: category).Category, Is.EqualTo(expected));
        }

        [Test]
        public void It_appends_the_exception_to_the_message()
        {
            var entry = Create(exception: "System.InvalidOperationException: boom");

            Assert.That(entry.Message, Is.EqualTo($"hello{Environment.NewLine}System.InvalidOperationException: boom"));
        }

        [Test]
        public void It_formats_a_copyable_line()
        {
            var entry = Create();

            Assert.That(entry.ToString(), Is.EqualTo($"{entry.Time} INF ProcessRunner: hello"));
        }
    }
}
