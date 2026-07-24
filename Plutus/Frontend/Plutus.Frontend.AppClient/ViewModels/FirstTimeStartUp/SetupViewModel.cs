using Database.Enums;
using Database.Models;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.FirstTimeStartUp
{
    class SetupViewModel : BaseViewModel
    {
        #region Private Fields
        #region Account Fields
        private EmployeeModel _employee = new EmployeeModel();
        private string _password = "";
        private string _passwordConf = "";
        private byte[] _salt;
        #endregion
        #region Store Fields
        private StoreModel _store = new StoreModel();
        #endregion
        private List<Placemark> _placemarks;
        private Placemark _placemark;
        private List<KeyValuePair<string, DatabaseProvider>> _serverOptions =
            new List<KeyValuePair<string, DatabaseProvider>>();
        private KeyValuePair<string, DatabaseProvider> _serverOption;
        #endregion

        #region Public Properties for Fields
        #region Account Properties
        public EmployeeModel Employee
        {
            get { return _employee; }
            set
            {
                _employee = value;
                OnPropertyChanged();
            }
        }

        public string Password
        {
            get { return _password; }
            set
            {
                if (value != _password)
                {
                    _password = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PasswordConf
        {
            get { return _passwordConf; }
            set
            {
                if (value != _passwordConf)
                {
                    _passwordConf = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion
        #region Store Properties

        public StoreModel Store
        {
            get { return _store; }
            set
            {
                _store = value;
                OnPropertyChanged();
            }
        }
        #endregion

        public List<Placemark> Placemarks
        {
            get { return _placemarks; }
            set
            {
                _placemarks = value;
                OnPropertyChanged();
            }
        }

        public Placemark Placemark
        {
            get { return _placemark; }
            set
            {
                _placemark = value;
                OnPropertyChanged();
            }
        }

        public List<KeyValuePair<string, DatabaseProvider>> ServerOptions
        {
            get { return _serverOptions; }
            set
            {
                _serverOptions = value;
                OnPropertyChanged();
            }
        }

        public KeyValuePair<string, DatabaseProvider> ServerOption
        {
            get { return _serverOption; }
            set
            {
                _serverOption = value;
                OnPropertyChanged();
            }
        }
        #endregion

        #region Commands
        #region Create Command
        Command _createCommand;
        public Command CreateCommand
        {
            get
            {
                return _createCommand ?? (_createCommand = new Command(ExecuteCreateCommand, CanCreate));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool CanCreate()
        {
            return _password.Equals(_passwordConf) && !string.IsNullOrEmpty(_password);
        }
        #endregion
        #region Location Command
        Command _locationCommand;
        public Command LocationCommand
        {
            get
            {
                return _locationCommand ?? (_locationCommand = new Command(ExecuteLocationCommand));
            }
        }
        #endregion
        #region Use Address Command
        Command _useAddressCommand;
        public Command UseAddressCommand
        {
            get
            {
                return _useAddressCommand ?? (_useAddressCommand = new Command<string>(ExecuteUseAddressCommand));
            }
        }
        #endregion
        #endregion

        public SetupViewModel()
        {
            _salt = Helpers.Security.Password.GenerateSalt();
            _serverOptions.Add(new KeyValuePair<string, DatabaseProvider>("Local Application", DatabaseProvider.Sqlite));
            _serverOptions.Add(new KeyValuePair<string, DatabaseProvider>("Cloud", DatabaseProvider.Cloud));

            Title = "SetUpTitle".Translate();
            Icon = "";
        }

        #region Command Executions
        /// <summary>
        /// Create Database and store employee and store in database
        /// </summary>
        private async void ExecuteCreateCommand()
        {
            App.SetLoading(true);
            if (!_serverOption.Key.Equals(null))
            {
                _employee.Salt = Convert.ToBase64String(_salt);
                _employee.HashedPassword = Convert.ToBase64String(await Task.Run(() => Helpers.Security.Password.ComputeHash(_password, _salt)));
                _employee.Store = _store;

                if (_serverOption.Value == DatabaseProvider.Sqlite)
                {
                    using (var dbHelper = new Helpers.Database.Database(_serverOption.Value))
                    {
                        dbHelper.Add(_employee);
                        dbHelper.Add(_store);
                        dbHelper.Init();
                        if (!dbHelper.Save())
                        {
                            //Deal with errors
                        }
                        else
                        {
                            DatabaseProviderSetting = _serverOption.Value.ToString();
                            App.Current.MainPage = new LoginView();
                        }
                    }
                }
            }
            App.SetLoading(false);
        }

        private async void ExecuteLocationCommand()
        {
            App.SetLoading(true);
            try
            {
                var request = new GeolocationRequest(GeolocationAccuracy.Best);
                var location = await Geolocation.GetLocationAsync(request);

                try
                {
                    var address = await Geocoding.GetPlacemarksAsync(location.Latitude, location.Longitude);
                    Placemarks = address.ToList();
                }
                catch (FeatureNotSupportedException fNSEx)
                {
                    Debug.WriteLine(fNSEx);
                }
                catch (Exception Ex)
                {
                    Debug.WriteLine(Ex);
                }
            }
            catch (FeatureNotSupportedException fNSEx)
            {
                Debug.WriteLine(fNSEx);
            }
            catch (FeatureNotEnabledException fNEEx)
            {
                Debug.WriteLine(fNEEx);
            }
            catch (PermissionException pEx)
            {
                Debug.WriteLine(pEx);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            App.SetLoading(false);
        }

        private void ExecuteUseAddressCommand(string arg)
        {
            switch (arg)
            {
                case "Account":
                    Employee.AdLine1 = string.Format("{0} {1}", Placemark.SubThoroughfare, Placemark.Thoroughfare);
                    Employee.AdLine2 = Placemark.SubLocality;
                    Employee.City = Placemark.Locality;
                    Employee.PostCode = Placemark.PostalCode;
                    Employee.Country = Placemark.CountryName;
                    OnPropertyChanged("Employee");
                    break;
                case "Store":
                    Store.AdLine1 = string.Format("{0} {1}", Placemark.SubThoroughfare, Placemark.Thoroughfare);
                    Store.AdLine2 = Placemark.SubLocality;
                    Store.City = Placemark.Locality;
                    Store.PostCode = Placemark.PostalCode;
                    Store.Country = Placemark.CountryName;
                    OnPropertyChanged("Store");
                    break;
                default:
                    return;
            }
        }
        #endregion
    }
}
