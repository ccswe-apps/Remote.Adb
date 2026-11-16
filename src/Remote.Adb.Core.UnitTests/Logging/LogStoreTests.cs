using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Remote.Adb.Core.Logging;

namespace Remote.Adb.Core.UnitTests.Logging;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class LogStoreTests
{
    protected static void Add(LogStore store, string message) =>
        store.Add(LogLevel.Information, "Category", default, message, null);

    public class When_Add_Is_Called : LogStoreTests
    {
        [Test]
        public void It_raises_EntryAdded_with_increasing_sequence_numbers()
        {
            var store = new LogStore();
            var raised = new List<LogEntry>();
            store.EntryAdded += (_, entry) => raised.Add(entry);

            Add(store, "first");
            Add(store, "second");

            Assert.Multiple(() =>
            {
                Assert.That(raised.Select(entry => entry.Message), Is.EqualTo(new[] { "first", "second" }));
                Assert.That(raised[1].Sequence, Is.GreaterThan(raised[0].Sequence));
            });
        }

        [Test]
        public void It_evicts_the_oldest_entry_once_full()
        {
            var store = new LogStore(capacity: 2);

            Add(store, "one");
            Add(store, "two");
            Add(store, "three");

            Assert.That(store.Snapshot().Select(entry => entry.Message), Is.EqualTo(new[] { "two", "three" }));
        }
    }

    public class When_Clear_Is_Called : LogStoreTests
    {
        [Test]
        public void It_empties_the_buffer_and_returns_the_last_sequence()
        {
            var store = new LogStore();
            Add(store, "one");
            Add(store, "two");
            var last = store.Snapshot()[^1].Sequence;

            var cleared = store.Clear();

            Assert.Multiple(() =>
            {
                Assert.That(cleared, Is.EqualTo(last));
                Assert.That(store.Snapshot(), Is.Empty);
            });
        }

        [Test]
        public void It_keeps_numbering_after_the_clear()
        {
            var store = new LogStore();
            Add(store, "before");
            var cleared = store.Clear();

            Add(store, "after");

            Assert.That(store.Snapshot()[0].Sequence, Is.GreaterThan(cleared));
        }
    }
}
