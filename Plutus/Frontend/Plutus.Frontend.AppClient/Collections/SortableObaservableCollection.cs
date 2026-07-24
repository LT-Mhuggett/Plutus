using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace Plutus.Frontend.AppClient.Collections
{
    public class SortableObservableCollection<T> : ObservableCollection<T>
    {
        #region Fields
        private bool _sortDisabled;
        #endregion
        #region Properties
        public Func<T, object> SortingSelector { get; set; }
        public bool Descending { get; set; }
        #endregion

        #region Constructors
        public SortableObservableCollection() : base() { }

        public SortableObservableCollection(Func<T, object> sortingSelector) : base()
        {
            SortingSelector = sortingSelector;
        }

        public SortableObservableCollection(IEnumerable<T> collection, Func<T, object> sortingSelector) : base(collection)
        {
            SortingSelector = sortingSelector;
        }

        public SortableObservableCollection(List<T> list, Func<T, object> sortingSelector) : base(list)
        {
            SortingSelector = sortingSelector;
        }
        #endregion

        #region Opertions
        public void AddRange(IEnumerable<T> collection)
        {
            _sortDisabled = true;

            for(var i = 0; i < collection.Count(); i++)
            {
                if (i == collection.Count() - 1)
                    _sortDisabled = false;
                Add(collection.ElementAt(i));
            }
        }

        public void AddRange(T[] array)
        {
            _sortDisabled = true;

            for (var i = 0; i < array.Count(); i++)
            {
                if (i == array.Count() - 1)
                    _sortDisabled = false;
                Add(array[i]);
            }
        }
        #endregion

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnCollectionChanged(e);
            if (e.Action != NotifyCollectionChangedAction.Reset &&
                e.Action != NotifyCollectionChangedAction.Move &&
                e.Action != NotifyCollectionChangedAction.Remove &&
                SortingSelector != null &&
                !_sortDisabled)
            {
                var query = this.Select((item, index) => new { Index = index, Item = item });
                query = Descending ?
                    query.OrderBy(tuple => SortingSelector(tuple.Item)) :
                    query.OrderByDescending(tuple => SortingSelector(tuple.Item));

                var map = query.Select((tuple, index) => new { OldIndex = tuple.Index, NewIndex = index })
                    .Where(o => o.OldIndex != o.NewIndex);

                using (var enumerator = map.GetEnumerator())
                    if (enumerator.MoveNext())
                        Move(enumerator.Current.OldIndex, enumerator.Current.NewIndex);
            }
        }
    }
}
