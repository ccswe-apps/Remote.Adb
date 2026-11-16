using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Moq;
using Remote.Adb.Core.Logging;
using Remote.Adb.Desktop.Logging;
using Remote.Adb.Desktop.UnitTests.Fakes;

namespace Remote.Adb.Desktop.UnitTests.Logging;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class LogPanelViewModelTests
{
    private protected Mock<IClipboardService> Clipboard = null!;
    private protected LogStore Store = null!;

    [SetUp]
    public void SetUp()
    {
        Clipboard = new Mock<IClipboardService>();
        Store = new LogStore();
    }

    private protected static LogEntry Entry(long sequence, LogLevel level = LogLevel.Information, string message = "message") =>
        new(sequence, DateTimeOffset.Now, level, "Remote.Adb.Tests", default, message, null);

    private protected void Log(string message, LogLevel level = LogLevel.Information) =>
        Store.Add(level, "Remote.Adb.Tests", default, message, null);

    private protected static IEnumerable<string> Messages(LogPanelViewModel viewModel) =>
        viewModel.Entries.Select(entry => entry.Entry.Message);

    public class When_It_Is_Created : LogPanelViewModelTests
    {
        [Test]
        public void It_shows_entries_logged_before_it_existed()
        {
            Log("earlier");

            var viewModel = new LogPanelViewModel(Store, new FakeUiDispatcher(), Clipboard.Object);

            Assert.That(Messages(viewModel), Is.EqualTo(new[] { "earlier" }));
        }

        [Test]
        public void It_ignores_an_event_for_an_entry_already_in_the_snapshot()
        {
            var store = new Mock<ILogStore>();
            store.Setup(s => s.Snapshot()).Returns([Entry(1, message: "snapshot")]);
            var viewModel = new LogPanelViewModel(store.Object, new FakeUiDispatcher(), Clipboard.Object);

            store.Raise(s => s.EntryAdded += null, store.Object, Entry(1, message: "snapshot"));
            store.Raise(s => s.EntryAdded += null, store.Object, Entry(2, message: "new"));

            Assert.That(Messages(viewModel), Is.EqualTo(new[] { "snapshot", "new" }));
        }
    }

    public class When_Entries_Are_Logged : LogPanelViewModelTests
    {
        [Test]
        public void It_appends_them_in_sequence_order()
        {
            var store = new Mock<ILogStore>();
            store.Setup(s => s.Snapshot()).Returns([]);
            var dispatcher = new DeferredUiDispatcher();
            var viewModel = new LogPanelViewModel(store.Object, dispatcher, Clipboard.Object);

            store.Raise(s => s.EntryAdded += null, store.Object, Entry(2, message: "second"));
            store.Raise(s => s.EntryAdded += null, store.Object, Entry(1, message: "first"));
            dispatcher.RunAll();

            Assert.That(Messages(viewModel), Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void It_posts_a_single_flush_for_a_burst()
        {
            var dispatcher = new DeferredUiDispatcher();
            var viewModel = new LogPanelViewModel(Store, dispatcher, Clipboard.Object);

            Log("one");
            Log("two");
            Log("three");

            Assert.That(dispatcher.PendingCount, Is.EqualTo(1));
            dispatcher.RunAll();
            Assert.That(Messages(viewModel), Is.EqualTo(new[] { "one", "two", "three" }));
        }

        [Test]
        public void It_hides_entries_below_the_minimum_level()
        {
            var viewModel = new LogPanelViewModel(Store, new FakeUiDispatcher(), Clipboard.Object)
            {
                MinimumLevel = LogLevel.Warning,
            };

            Log("info");
            Log("warning", LogLevel.Warning);

            Assert.That(Messages(viewModel), Is.EqualTo(new[] { "warning" }));
        }

        [Test]
        public void It_keeps_at_most_the_maximum_number_of_entries()
        {
            var viewModel = new LogPanelViewModel(Store, new FakeUiDispatcher(), Clipboard.Object);

            for (var index = 0; index <= LogPanelViewModel.MaxEntries; index++)
            {
                Log($"entry {index}");
            }

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Entries, Has.Count.EqualTo(LogPanelViewModel.MaxEntries));
                Assert.That(viewModel.Entries[0].Entry.Message, Is.EqualTo("entry 1"));
            });
        }
    }

    public class When_The_Minimum_Level_Changes : LogPanelViewModelTests
    {
        [Test]
        public void It_reapplies_the_filter_to_entries_already_received()
        {
            var viewModel = new LogPanelViewModel(Store, new FakeUiDispatcher(), Clipboard.Object);
            Log("info");
            Log("error", LogLevel.Error);

            viewModel.MinimumLevel = LogLevel.Error;

            Assert.That(Messages(viewModel), Is.EqualTo(new[] { "error" }));
        }
    }

    public class When_Paused : LogPanelViewModelTests
    {
        [Test]
        public void It_holds_new_entries_until_resumed()
        {
            var viewModel = new LogPanelViewModel(Store, new FakeUiDispatcher(), Clipboard.Object)
            {
                IsPaused = true,
            };

            Log("while paused");
            var whilePaused = Messages(viewModel).ToList();
            viewModel.IsPaused = false;

            Assert.Multiple(() =>
            {
                Assert.That(whilePaused, Is.Empty);
                Assert.That(Messages(viewModel), Is.EqualTo(new[] { "while paused" }));
            });
        }
    }

    public class When_ClearCommand_Is_Executed : LogPanelViewModelTests
    {
        [Test]
        public void It_empties_the_panel_and_the_store()
        {
            var viewModel = new LogPanelViewModel(Store, new FakeUiDispatcher(), Clipboard.Object);
            Log("old");

            viewModel.ClearCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Entries, Is.Empty);
                Assert.That(Store.Snapshot(), Is.Empty);
            });
        }

        [Test]
        public void It_drops_entries_queued_before_the_clear()
        {
            var dispatcher = new DeferredUiDispatcher();
            var viewModel = new LogPanelViewModel(Store, dispatcher, Clipboard.Object);
            Log("queued before clear");

            viewModel.ClearCommand.Execute(null);
            Log("after clear");
            dispatcher.RunAll();

            Assert.That(Messages(viewModel), Is.EqualTo(new[] { "after clear" }));
        }
    }

    public class When_CopyAllCommand_Is_Executed : LogPanelViewModelTests
    {
        [Test]
        public async Task It_copies_the_visible_entries_one_per_line()
        {
            var viewModel = new LogPanelViewModel(Store, new FakeUiDispatcher(), Clipboard.Object);
            Log("first");
            Log("second");
            string? copied = null;
            Clipboard.Setup(clipboard => clipboard.SetTextAsync(It.IsAny<string>()))
                .Callback<string>(text => copied = text)
                .Returns(Task.CompletedTask);

            await viewModel.CopyAllCommand.ExecuteAsync(null);

            Assert.That(copied?.Split(Environment.NewLine), Has.Length.EqualTo(2).And.All.Contains("INF Tests:"));
        }
    }

    public class When_Disposed : LogPanelViewModelTests
    {
        [Test]
        public void It_stops_receiving_entries()
        {
            var viewModel = new LogPanelViewModel(Store, new FakeUiDispatcher(), Clipboard.Object);

            viewModel.Dispose();
            Log("after dispose");

            Assert.That(viewModel.Entries, Is.Empty);
        }
    }
}
