using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;

namespace Plutus.Frontend.AppClient.Collections
{
    public class ObservableList<T> : IList<T>, INotifyCollectionChanged
    {
        #region Properties
        private List<T> _list { get; set; } 
        T IList<T>.this[int index]
        {
            get => _list[index];
            set
            {
                if (index < 0 || index >= Count)
                    throw new IndexOutOfRangeException("The specified index is out of range.");
                var oldItem = _list[index];
                _list[index] = value;
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, oldItem, index));
            }
        }

        public int Count => _list.Count;

        public bool IsReadOnly => ((IList)_list).IsReadOnly;
        #endregion


        #region Constructors
        public ObservableList()
        {
            _list = new List<T>();
        }

        public ObservableList(IEnumerable<T> collection)
        {
            _list = new List<T>(collection);
        }
        #endregion

        #region Methods
        #region List Growth
        public void Add(T item)
        {
            _list.Add(item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
        }

        public void AddRange(IEnumerable<T> collection)
        {
            _list.AddRange(collection);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, collection));
        }

        public void Insert(int index, T item)
        {
            _list.Insert(index, item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
        }

        public void InsertRange(int index, IEnumerable<T> collection)
        {
            _list.InsertRange(index, collection);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, collection, index));
        }
        #endregion
        #region List Shrink
        void ICollection<T>.Clear()
        {
            _list.Clear();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public bool Remove(T item)
        {
            var result = _list.Remove(item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
            return result;
        }

        public void RemoveAt(int index)
        {
            var oldItem = _list[index];
            _list.RemoveAt(index);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItem, index));
        }

        public void RemoveRange(int index, int count)
        {
            List<T> removedItems = _list.GetRange(index, count);
            _list.RemoveRange(index, count);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removedItems));
        }

        public void RemoveAll(Predicate<T> match)
        {
            _list.RemoveAll(match);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove));
        }
        #endregion

        #region Querys
        bool ICollection<T>.Contains(T item)
        {
            return _list.Contains(item);
        }

        int IList<T>.IndexOf(T item)
        {
            return _list.IndexOf(item);
        }
        #endregion

        public void CopyTo(T[] array, int arrayIndex = 0)
        {
            _list.CopyTo(array, arrayIndex);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
        #endregion

        #region Notify
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        private void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            CollectionChanged?.Invoke(this, args);
        }
        #endregion
    }
}
