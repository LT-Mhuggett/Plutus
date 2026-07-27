using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Plutus.Frontend.AppClient.Collections;

namespace Plutus.Frontend.AppClient.Tests.Collections
{
    public class ObservableListTests
    {
        [Fact]
        public void Constructor_Default_IsEmpty()
        {
            var list = new ObservableList<int>();
            Assert.Equal(0, list.Count);
            Assert.False(list.IsReadOnly);
        }

        [Fact]
        public void Constructor_FromEnumerable_CopiesItems()
        {
            var list = new ObservableList<int>(new[] { 1, 2, 3 });
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void Add_RaisesCollectionChangedWithAddAction()
        {
            var list = new ObservableList<string>();
            NotifyCollectionChangedEventArgs? args = null;
            list.CollectionChanged += (_, e) => args = e;

            list.Add("a");

            Assert.Equal(1, list.Count);
            Assert.Equal(NotifyCollectionChangedAction.Add, args!.Action);
            Assert.Equal("a", args.NewItems![0]);
        }

        [Fact]
        public void AddRange_AddsAllItemsAndRaisesEvent()
        {
            var list = new ObservableList<int>();
            NotifyCollectionChangedEventArgs? args = null;
            list.CollectionChanged += (_, e) => args = e;

            list.AddRange(new[] { 1, 2, 3 });

            Assert.Equal(3, list.Count);
            Assert.Equal(NotifyCollectionChangedAction.Add, args!.Action);
        }

        [Fact]
        public void Insert_PlacesItemAtIndexAndRaisesEvent()
        {
            var list = new ObservableList<string>(new[] { "a", "c" });
            NotifyCollectionChangedEventArgs? args = null;
            list.CollectionChanged += (_, e) => args = e;

            list.Insert(1, "b");

            Assert.Equal(new[] { "a", "b", "c" }, AsEnumerable(list));
            Assert.Equal(NotifyCollectionChangedAction.Add, args!.Action);
        }

        [Fact]
        public void InsertRange_PlacesItemsAtIndexAndRaisesEvent()
        {
            var list = new ObservableList<int>(new[] { 1, 4 });
            NotifyCollectionChangedEventArgs? args = null;
            list.CollectionChanged += (_, e) => args = e;

            list.InsertRange(1, new[] { 2, 3 });

            Assert.Equal(new[] { 1, 2, 3, 4 }, AsEnumerable(list));
            Assert.Equal(NotifyCollectionChangedAction.Add, args!.Action);
        }

        [Fact]
        public void Clear_ThroughICollection_RemovesAllItemsAndRaisesResetEvent()
        {
            ICollection<int> list = new ObservableList<int>(new[] { 1, 2 });
            NotifyCollectionChangedEventArgs? args = null;
            ((ObservableList<int>)list).CollectionChanged += (_, e) => args = e;

            list.Clear();

            Assert.Equal(0, ((ObservableList<int>)list).Count);
            Assert.Equal(NotifyCollectionChangedAction.Reset, args!.Action);
        }

        [Fact]
        public void Remove_ExistingItem_RemovesItAndRaisesEvent()
        {
            var list = new ObservableList<string>(new[] { "a", "b" });
            NotifyCollectionChangedEventArgs? args = null;
            list.CollectionChanged += (_, e) => args = e;

            var removed = list.Remove("a");

            Assert.True(removed);
            Assert.Equal(1, list.Count);
            Assert.Equal(NotifyCollectionChangedAction.Remove, args!.Action);
        }

        [Fact]
        public void Remove_MissingItem_ReturnsFalse()
        {
            var list = new ObservableList<string>(new[] { "a" });
            Assert.False(list.Remove("z"));
        }

        [Fact]
        public void RemoveAt_RemovesItemAndRaisesEvent()
        {
            var list = new ObservableList<int>(new[] { 1, 2, 3 });
            NotifyCollectionChangedEventArgs? args = null;
            list.CollectionChanged += (_, e) => args = e;

            list.RemoveAt(1);

            Assert.Equal(new[] { 1, 3 }, AsEnumerable(list));
            Assert.Equal(NotifyCollectionChangedAction.Remove, args!.Action);
            Assert.Equal(2, args.OldItems![0]);
        }

        [Fact]
        public void RemoveRange_RemovesItemsAndRaisesEvent()
        {
            var list = new ObservableList<int>(new[] { 1, 2, 3, 4 });
            NotifyCollectionChangedEventArgs? args = null;
            list.CollectionChanged += (_, e) => args = e;

            list.RemoveRange(1, 2);

            Assert.Equal(new[] { 1, 4 }, AsEnumerable(list));
            Assert.Equal(NotifyCollectionChangedAction.Remove, args!.Action);
        }

        [Fact]
        public void RemoveAll_Throws_BecauseRemoveActionRequiresItemsArgument()
        {
            // Production bug: RemoveAll raises `new NotifyCollectionChangedEventArgs(Remove)` with no
            // items/index, but that constructor overload only accepts NotifyCollectionChangedAction.Reset
            // - so this always throws ArgumentException before RemoveAll can ever complete. Documented
            // here rather than fixed, consistent with how other pre-existing production bugs (the
            // NiNoValidator/VatINValidator regex issues) were handled in ValidatorsTests.
            var list = new ObservableList<int>(new[] { 1, 2, 3, 4 });

            Assert.Throws<ArgumentException>(() => list.RemoveAll(i => i % 2 == 0));
        }

        [Fact]
        public void ICollectionContains_ReflectsListContents()
        {
            ICollection<int> list = new ObservableList<int>(new[] { 1, 2 });
            Assert.True(list.Contains(1));
            Assert.False(list.Contains(99));
        }

        [Fact]
        public void IListIndexOf_ReturnsCorrectIndex()
        {
            IList<string> list = new ObservableList<string>(new[] { "a", "b", "c" });
            Assert.Equal(1, list.IndexOf("b"));
            Assert.Equal(-1, list.IndexOf("z"));
        }

        [Fact]
        public void IListIndexer_Get_ReturnsItemAtIndex()
        {
            IList<string> list = new ObservableList<string>(new[] { "a", "b" });
            Assert.Equal("b", list[1]);
        }

        [Fact]
        public void IListIndexer_Set_ReplacesItemAndRaisesEvent()
        {
            IList<string> list = new ObservableList<string>(new[] { "a", "b" });
            NotifyCollectionChangedEventArgs? args = null;
            ((ObservableList<string>)list).CollectionChanged += (_, e) => args = e;

            list[0] = "z";

            Assert.Equal("z", list[0]);
            Assert.Equal(NotifyCollectionChangedAction.Replace, args!.Action);
        }

        [Fact]
        public void IListIndexer_SetOutOfRange_Throws()
        {
            IList<string> list = new ObservableList<string>(new[] { "a" });
            Assert.Throws<IndexOutOfRangeException>(() => list[5] = "z");
        }

        [Fact]
        public void CopyTo_CopiesItemsIntoArray()
        {
            var list = new ObservableList<int>(new[] { 1, 2, 3 });
            var array = new int[3];

            list.CopyTo(array);

            Assert.Equal(new[] { 1, 2, 3 }, array);
        }

        [Fact]
        public void GetEnumerator_Generic_IteratesAllItems()
        {
            IEnumerable<int> list = new ObservableList<int>(new[] { 1, 2, 3 });
            Assert.Equal(new[] { 1, 2, 3 }, AsEnumerable(list));
        }

        [Fact]
        public void GetEnumerator_NonGeneric_IteratesAllItems()
        {
            IEnumerable list = new ObservableList<int>(new[] { 1, 2 });
            var items = new List<object>();
            foreach (var item in list)
                items.Add(item);

            Assert.Equal(new object[] { 1, 2 }, items);
        }

        private static List<T> AsEnumerable<T>(IEnumerable<T> source)
        {
            var result = new List<T>();
            foreach (var item in source)
                result.Add(item);
            return result;
        }
    }
}
