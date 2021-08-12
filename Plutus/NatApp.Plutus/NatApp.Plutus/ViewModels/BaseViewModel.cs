using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NatApp.Plutus.ViewModels
{
    public abstract class BaseViewModel : Settings, INotifyPropertyChanged, IDisposable
    {
        #region Private Variables
        private string _title;

        private string _icon;

#pragma warning disable IDE1006 // Naming Styles
        private static bool _isBusy;
#pragma warning restore IDE1006 // Naming Styles
        #endregion

        #region Public Properties
        /// <summary>
        /// Page Title
        /// </summary>
        public string Title
        {
            get => _title;
            set { SetProperty(ref _title, value); }
        }

        /// <summary>
        /// Page Icon location
        /// </summary>
        public string Icon
        {
            get => _icon;
            set { SetProperty(ref _icon, value); }
        }

        /// <summary>
        /// Is page busy
        /// </summary>
        public static bool IsBusy
        {
            get => _isBusy;
            set { StaticSetProperty(ref _isBusy, value); }
        }
        #endregion
        
        protected BaseViewModel()
        {

        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        public static event PropertyChangedEventHandler StaticPropertyChanged;

        /// <summary>
        /// Actions the change to the Variable(Property), fires the OnPropertyChanged event.
        /// This can execute an Action to run on value changed
        /// </summary>
        /// <typeparam name="T">The type of the Property</typeparam>
        /// <param name="backingStore">Property to change</param>
        /// <param name="value">Value to change to</param>
        /// <param name="propertyName">Caller Property name</param>
        /// <param name="onChanged">Action to run</param>
        /// <returns></returns>
        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName]string propertyName = "", Action onChanged = null)
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;
            backingStore = value;
            onChanged?.Invoke();
            OnPropertyChanged(propertyName);
            return true;
        }

        protected static bool StaticSetProperty<T>(ref T backingStore, T value, [CallerMemberName]string propertyName = "", Action onChanged = null)
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;
            backingStore = value;
            onChanged?.Invoke();
            StaticOnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected static void StaticOnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        } 
        #endregion

        #region IDisposable Support
        private bool _disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _title = null;
                    _icon = null;
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
