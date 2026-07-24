using Plutus.Frontend.AppClient.Collections;

namespace Plutus.Frontend.AppClient.Tests.Collections
{
    public class SortableObservableCollectionTests
    {
        [Fact]
        public void Constructor_Default_IsEmpty()
        {
            var collection = new SortableObservableCollection<int>();
            Assert.Empty(collection);
            Assert.Null(collection.SortingSelector);
        }

        [Fact]
        public void Constructor_WithSortingSelector_SetsProperty()
        {
            Func<int, object> selector = i => i;
            var collection = new SortableObservableCollection<int>(selector);
            Assert.Same(selector, collection.SortingSelector);
        }

        [Fact]
        public void Constructor_WithEnumerableAndSelector_CopiesItemsAndSetsSelector()
        {
            Func<int, object> selector = i => i;
            var collection = new SortableObservableCollection<int>(new[] { 1, 2 }, selector);
            Assert.Equal(new[] { 1, 2 }, collection);
            Assert.Same(selector, collection.SortingSelector);
        }

        [Fact]
        public void Constructor_WithListAndSelector_CopiesItemsAndSetsSelector()
        {
            Func<int, object> selector = i => i;
            var collection = new SortableObservableCollection<int>(new List<int> { 1, 2 }, selector);
            Assert.Equal(new[] { 1, 2 }, collection);
            Assert.Same(selector, collection.SortingSelector);
        }

        [Fact]
        public void Add_WithNoSortingSelector_DoesNotReorder()
        {
            var collection = new SortableObservableCollection<int>();
            collection.Add(3);
            collection.Add(1);
            collection.Add(2);

            Assert.Equal(new[] { 3, 1, 2 }, collection);
        }

        [Fact]
        public void Add_WithSortingSelector_ConvergesToDescendingOrder()
        {
            // Despite the name, Descending=false takes the OrderByDescending branch in production code
            // (Plutus.Frontend.AppClient.Collections.SortableObaservableCollection.cs) - the property's sense is
            // inverted from what "Descending" implies. Each CollectionChanged event corrects only the
            // first out-of-place item it finds (not a full re-sort), so this documents the observed
            // end state for individual Add() calls rather than asserting a general single-pass sort.
            var collection = new SortableObservableCollection<int>(i => i);

            collection.Add(1);
            collection.Add(3);
            collection.Add(2);

            Assert.Equal(new[] { 3, 2, 1 }, collection);
        }

        [Fact]
        public void Add_WithSortingSelectorAndDescendingTrue_ConvergesToAscendingOrder()
        {
            var collection = new SortableObservableCollection<int>(i => i) { Descending = true };

            collection.Add(3);
            collection.Add(1);

            Assert.Equal(new[] { 1, 3 }, collection);
        }

        [Fact]
        public void AddRange_IEnumerable_AppendsAllItemsThenSortsOnce()
        {
            var collection = new SortableObservableCollection<int>(i => i);

            collection.AddRange(new List<int> { 1, 2 });

            Assert.Equal(new[] { 2, 1 }, collection);
        }

        [Fact]
        public void AddRange_Array_AppendsAllItemsThenSortsOnce()
        {
            var collection = new SortableObservableCollection<int>(i => i);

            collection.AddRange(new[] { 1, 2 });

            Assert.Equal(new[] { 2, 1 }, collection);
        }

        [Fact]
        public void Remove_DoesNotThrow_WithSortingSelectorSet()
        {
            var collection = new SortableObservableCollection<int>(i => i);
            collection.AddRange(new[] { 1, 2, 3 });

            var ex = Record.Exception(() => collection.Remove(2));

            Assert.Null(ex);
            Assert.DoesNotContain(2, collection);
        }
    }
}
