using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Desktop.Common;

namespace Remote.Adb.Desktop.UnitTests.Common;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class ObservableCollectionMergeExtensionsTests
{
    private protected static void Merge(ObservableCollection<Row> rows, params string[] items) =>
        rows.MergeBy(items, item => item, row => row.Key, item => new Row(item), (row, _) => row.Updates++, row => row.Key);

    public class When_MergeBy_Is_Called : ObservableCollectionMergeExtensionsTests
    {
        [Test]
        public void It_adds_updates_and_removes_rows_in_place()
        {
            var rows = new ObservableCollection<Row>();
            Merge(rows, "b", "a");
            var kept = rows.Single(row => row.Key == "a");

            Merge(rows, "a", "c");

            Assert.Multiple(() =>
            {
                Assert.That(rows.Select(row => row.Key), Is.EqualTo(new[] { "a", "c" }));
                Assert.That(rows[0], Is.SameAs(kept));
                Assert.That(kept.Updates, Is.EqualTo(1));
            });
        }

        [Test]
        public void It_orders_case_insensitively()
        {
            var rows = new ObservableCollection<Row>();

            Merge(rows, "Zebra", "alpha", "Mango");

            Assert.That(rows.Select(row => row.Key), Is.EqualTo(new[] { "alpha", "Mango", "Zebra" }));
        }
    }

    public class When_MergeByKind_Is_Called : ObservableCollectionMergeExtensionsTests
    {
        [Test]
        public void It_leaves_rows_owned_by_another_source_alone()
        {
            var rows = new ObservableCollection<Row> { new("other:1"), new("mine:1") };

            rows.MergeByKind(
                new[] { "mine:2" },
                item => item,
                row => row.Key,
                row => row.Key.StartsWith("mine:", StringComparison.Ordinal),
                item => new Row(item),
                (_, _) => { });

            Assert.That(rows.Select(row => row.Key), Is.EqualTo(new[] { "other:1", "mine:2" }));
        }
    }

    public class When_SortBy_Is_Called : ObservableCollectionMergeExtensionsTests
    {
        [Test]
        public void It_moves_rows_rather_than_replacing_them()
        {
            var rows = new ObservableCollection<Row> { new("c"), new("a"), new("b") };
            var originals = rows.ToList();
            var resets = 0;
            rows.CollectionChanged += (_, args) => resets += args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset ? 1 : 0;

            rows.SortBy(row => row.Key);

            Assert.Multiple(() =>
            {
                Assert.That(rows.Select(row => row.Key), Is.EqualTo(new[] { "a", "b", "c" }));
                Assert.That(rows, Is.EquivalentTo(originals));
                Assert.That(resets, Is.Zero);
            });
        }
    }

    private protected sealed class Row
    {
        public Row(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public int Updates { get; set; }
    }
}
