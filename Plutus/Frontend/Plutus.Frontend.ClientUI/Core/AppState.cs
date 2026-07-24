using AutoMapper;
using Microsoft.AppCenter;
using Microsoft.Extensions.Configuration;
using Microsoft.Maui.Networking;
using Newtonsoft.Json;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core.AppSettings;
using Plutus.Frontend.ClientUI.Core.Collections;
using Plutus.Frontend.ClientUI.Domain.Models;
using Plutus.Frontend.ClientUI.Services.Analytics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Core
{
    public class AppState : IAppState
    {
        #region Fields
        private Guid _installId;
        private Guid _sessionId;
        private Guid _deviceVendorId;
        private AppLogLevel _appLogLevel;
        private KeyValuePair<Guid, string> _currentActiveUser;
        private Business _business;
        private Store _store;
        private Till _till;
        #endregion

        #region Properties
        public Guid InstallId
        {
            get => _installId;
            private set
            {
                if (EqualityComparer<Guid>.Default.Equals(_installId, value)) return;
                _installId = value;
                OnPropertyChanged();
            }
        }
        public Guid SessionId
        {
            get => _sessionId;
            private set
            {
                if (EqualityComparer<Guid>.Default.Equals(_sessionId, value)) return;
                _sessionId = value;
                OnPropertyChanged();
            }
        }
        public Guid DeviceVendorId
        {
            get => _deviceVendorId;
            private set
            {
                if (EqualityComparer<Guid>.Default.Equals(_deviceVendorId, value)) return;
                _deviceVendorId = value;
                OnPropertyChanged();
            }
        }
        public AppLogLevel AppLogLevel
        {
            get => _appLogLevel;
            private set
            {
                if (EqualityComparer<AppLogLevel>.Default.Equals(_appLogLevel, value)) return;
                _appLogLevel = value;
                OnPropertyChanged();
            }
        }
        public ObservableCollection<Employee> LoggedInEmployees { get; init; } = new ObservableCollection<Employee>();
        public ObservableDictionary<Guid, string> Employee_AuthenticationData { get; init; } = new ObservableDictionary<Guid, string>();
        public KeyValuePair<Guid, string> CurrentActiveUser
        {
            get => _currentActiveUser;
            set
            {
                if (EqualityComparer<KeyValuePair<Guid, string>>.Default.Equals(_currentActiveUser, value)) return;
                _currentActiveUser = value;
                OnPropertyChanged();
            }
        }
        public Business Business
        {
            get => _business;
            set
            {
                if (EqualityComparer<Business>.Default.Equals(_business, value)) return;
                _business = value;
                OnPropertyChanged();
            }
        }
        public Store Store
        {
            get => _store;
            set
            {
                if (EqualityComparer<Store>.Default.Equals(_store, value)) return;
                _store = value;
                OnPropertyChanged();
            }
        }
        public Till Till
        {
            get => _till;
            set
            {
                if (EqualityComparer<Till>.Default.Equals(_till, value)) return;
                _till = value;
                OnPropertyChanged();
            }
        }
        public IMapper Mapper { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion

        public async Task Init()
        {
            InstallId = Guid.NewGuid();
            SessionId = Guid.NewGuid();
#if WINDOWS
            DeviceVendorId = Windows.Storage.Streams.DataReader.FromBuffer(Windows.System.Profile.SystemIdentification.GetSystemIdForPublisher().Id)
                                                               .ReadGuid();
#elif ANDROID
            DeviceVendorId = Guid.Parse(Android.Provider.Settings.Secure.GetString(Android.App.Application.Context.ContentResolver, Android.Provider.Settings.Secure.AndroidId));
#elif IOS
            DeviceVendorId = Guid.Parse(UIKit.UIDevice.CurrentDevice.IdentifierForVendor.AsString());
#endif
            SessionId = Guid.NewGuid();

            var config = new MapperConfiguration(cfg =>
            {
                //Base -> Child
                cfg.CreateMap<BasketItem, BasketReturnItem>();
                //Child -> Base
                cfg.CreateMap<BasketReturnItem, BasketItem>();
            });

            if (await CheckAppCenter())
            {
                Guid? installId = await AppCenter.GetInstallIdAsync();
                if (installId != null)
                {
                    InstallId = (Guid)installId;
                }
            }
        }

        public void SetAppLogLevel(AppLogLevel level)
        {
            AppLogLevel = level;
        }

        private async Task<bool> CheckAppCenter()
        {
            var retValue = true;

            try
            {
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    bool isAnalyticsEnabled = await Microsoft.AppCenter.Analytics.Analytics.IsEnabledAsync();
                    if (isAnalyticsEnabled)
                    {
                        Debug.WriteLine($"[{GetType()}] Warning: AppCenter Analytics is NOT enabled.");
                        retValue = false;
                    }

                    bool isCrashEnabled = await Microsoft.AppCenter.Crashes.Crashes.IsEnabledAsync();
                    if (isCrashEnabled)
                    {
                        Debug.WriteLine(
                            $"[{GetType()}] Warning: AppCenter Crash Reporting is NOT enabled.");
                        retValue = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{GetType()}] Exception while checking if AppCenter is enabled {ex.Message} {ex.StackTrace}");
                retValue = false;
            }

            return retValue;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
