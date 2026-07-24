using CommunityToolkit.Mvvm.ComponentModel;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.EventArgs;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.ClientUI.ViewModels.PopupViewModels
{
    public partial class PopupBaseViewModel<T> : ObservableValidator, IDisposable
    {
        #region Variables
        [ObservableProperty]
        private string _title;
        #endregion

        #region Properties
        protected ILogger Logger { get; }
        protected IAppState AppState { get; set; }
        protected LoadingViewService LoadingViewService { get; }
        #endregion

        #region Events
        public delegate void PopupCloseRequestEventHandler(object sender, PopupCloseRequestEventArgs<T> popupCloseEventArgs);
        public event PopupCloseRequestEventHandler RaisePopupCloseRequest;
        #endregion

        public PopupBaseViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService)
        {
            Logger = logger;
            AppState = appState;
            LoadingViewService = loadingViewService;
        }

        protected virtual void OnCloseRequest(object sender, PopupCloseRequestEventArgs<T> closeRequestEventArgs)
        {
            RaisePopupCloseRequest?.Invoke(sender, closeRequestEventArgs);
        }

        #region IDsiposable Support
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~PopupBaseViewModel()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
