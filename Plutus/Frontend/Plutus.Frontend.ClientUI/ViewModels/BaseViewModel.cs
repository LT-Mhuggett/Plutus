using CommunityToolkit.Mvvm.ComponentModel;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.ClientUI.ViewModels
{
    public abstract partial class BaseViewModel : ObservableValidator, INotifyPropertyChanged, IDisposable
    {
        #region Private Variables
        private static bool _isBusy;
        [ObservableProperty]
        private string _icon;
        [ObservableProperty]
        private string _title;
        #endregion

        #region Properties

        /// <summary>
        /// Is page busy
        /// </summary>
        public static bool IsBusy
        {
            get => _isBusy;
            set => StaticSetProperty(ref _isBusy, value);
        }

        protected IAppState AppState { get; }
        protected LoadingViewService LoadingViewService { get; }
        protected ILogger Logger { get; }
        protected IRepositoryWrapper RepositoryWrapper { get; }
        #endregion

        protected BaseViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper)
        {
            Logger = logger;
            AppState = appState;
            LoadingViewService = loadingViewService;
            RepositoryWrapper = repositoryWrapper;
        }

        #region INotifyPropertyChanged
        public static event PropertyChangedEventHandler StaticPropertyChanged;

        protected static void StaticOnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }

        protected static bool StaticSetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "", Action onChanged = null)
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;
            backingStore = value;
            onChanged?.Invoke();
            StaticOnPropertyChanged(propertyName);
            return true;
        }
        #endregion

        #region IDisposable Support
        private bool _disposedValue = false;

        public void Dispose()
        {
            Dispose(true);
        }

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
        #endregion
    }
}
