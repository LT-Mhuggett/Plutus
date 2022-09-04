using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Services.Analytics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.ClientUI.ViewModels
{
    public class LoadingIndicatorViewModel : INotifyPropertyChanged
    {
        private string _currentLoadingItem;

        public ILogger Logger { get; }
        public IAppState AppState { get; }
        public string CurrentLoadingItem
        {
            get => _currentLoadingItem;
            set => SetProperty(ref _currentLoadingItem, value);
        }

        public bool CurrentLoadingItemIsBlank
        {
            get => string.IsNullOrEmpty(_currentLoadingItem);
        }


        public LoadingIndicatorViewModel(ILogger logger, IAppState appState)
        {
            Logger = logger;
            AppState = appState;
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "", Action onChanged = null)
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;
            backingStore = value;
            onChanged?.Invoke();
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
