using CustomViews.Structs;
using Microsoft.Maui.Devices;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using Database.Models;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Security;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.Frontend.AppClient.Services.IOHandeling;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.StoreOptions
{
    public class StoreInformationViewModel : BaseViewModel
    {
        #region Fields
        private bool _displayLogo;
        private string _currencyFormat;
        private string _positiveCurrencyFormat;
        private string _negativeCurrencyFormat;
        #endregion

        #region Properties
        public StoreModel Store
        {
            get => App.GetViewModel().Store;
        }
        public bool DisplayLogo
        {
            get => _displayLogo;
            set => SetProperty(ref _displayLogo, value);
        }
        public CultureInfo CultureInfo
        {
            get => CultureInfo.CurrentCulture;
        }
        public string PositiveCurrencyDisplay
        {
            get => _positiveCurrencyFormat.Replace("n", _currencyFormat);
        }
        public string NegativeCurrencyDisplay
        {
            get => _negativeCurrencyFormat.Replace("n", _currencyFormat);
        }
        public string CurrencyDisplay
        {
            get => $"{PositiveCurrencyDisplay}/{NegativeCurrencyDisplay}";
        }
        #endregion

        public StoreInformationViewModel(StackLayout leftColumn, StackLayout rightColumn)
        {
            Title = "StoreInformation".Translate();
            Icon = "md-store";

            DisplayLogo = Store.Logo != null;

            SetCurrencyDisplays();

            var buttonsAndSubHeadings = new List<Tuple<string, string>>
            {
                Tuple.Create("Store".Translate(), ""),
                Tuple.Create("Name".Translate(), "StoreNameChangeCommand"),
                //Tuple.Create("Address".Translate(), "StoreAddressChangeCommand"),
                Tuple.Create("StoreLogo".Translate(), "StoreLogoChangeCommand"),
                Tuple.Create("ContactNumber".Translate(), "StoreContactNumberChangeCommand"),
                Tuple.Create("VatIN".Translate(), "VatINChangeCommand"),
                Tuple.Create("Bag".Translate(), "StoreDefaultBagChangeCommand"),
                /*Tuple.Create("Region".Translate(), ""),
                Tuple.Create("Currency", "CurrencySettingsChangeCommand"),
                Tuple.Create("Date", "DateSettingsChangeCommand"),
                Tuple.Create("Employee".Translate(), ""),
                Tuple.Create($"{"Add".Translate()} {"Employee".Translate()}", "AddEmployeeCommand"),
                Tuple.Create(string.Format("ViewAllArg".Translate(), "Employees".Translate()), "ViewAllEmployeesCommand"),*/
                Tuple.Create("","")
            };

            StackLayout stack = null;
            int? n = null;
            for (int i = 0; i < buttonsAndSubHeadings.Count; i++)
            {
                if (buttonsAndSubHeadings[i].Item2 == "")
                {
                    if (n == null)
                        n = 0;
                    else
                    {
                        if (n % 2 == 0)
                            leftColumn.Children.Add(stack);
                        else
                            rightColumn.Children.Add(stack);
                        n++;
                    }
                    stack = new StackLayout();
                    stack.Children.Add(new Label
                    {
                        Text = buttonsAndSubHeadings[i].Item1,
                        FontSize = new Label().FontSize,
                        TextColor = Colors.LightGray,
                        FontAttributes = FontAttributes.Bold
                    });
                }
                else
                {
                    var button = new Button { Text = buttonsAndSubHeadings[i].Item1 };
                    button.SetBinding(Button.CommandProperty, buttonsAndSubHeadings[i].Item2);
                    stack.Children.Add(button);
                }
            }
        }

        private void SetCurrencyDisplays()
        {
            CultureInfo.NumberFormat.CurrencyGroupSizes.ForEach((group) =>
            {
                _currencyFormat += new string('#', group);
                _currencyFormat += CultureInfo.NumberFormat.CurrencyGroupSeparator;
            });
            _currencyFormat +=
                $"{new string('#', CultureInfo.NumberFormat.CurrencyGroupSizes[0])}" +
                $"{CultureInfo.NumberFormat.CurrencyDecimalSeparator}" +
                $"{new string('#', CultureInfo.NumberFormat.CurrencyDecimalDigits)}";

            _negativeCurrencyFormat = (-1).ToString("C0", CultureInfo.NumberFormat).Replace('1', 'n');
            _positiveCurrencyFormat = 1.ToString("C0", CultureInfo.NumberFormat).Replace('1', 'n');
        }

        #region Commands
        #region Store Details
        Command _storeNameChangeCommand;
        public Command StoreNameChangeCommand
        {
            get => _storeNameChangeCommand ?? (_storeNameChangeCommand = new Command(ExecuteStoreNameChange));
        }

        Command _storeAddressChangeCommand;
        public Command StoreAddressChangeCommand
        {
            get => _storeAddressChangeCommand ?? (_storeAddressChangeCommand = new Command(ExecuteStoreAddressChange));
        }

        Command _storeLogoChangeCommand;
        public Command StoreLogoChangeCommand
        {
            get => _storeLogoChangeCommand ?? (_storeLogoChangeCommand = new Command(ExecuteStoreLogoChange));
        }

        Command _storeContactNumberChangeCommand;
        public Command StoreContactNumberChangeCommand
        {
            get => _storeContactNumberChangeCommand ?? (_storeContactNumberChangeCommand = new Command(ExecuteStoreContactNumberChange));
        }

        Command _vatINChangeCommand;
        public Command VatINChangeCommand
        {
            get => _vatINChangeCommand ?? (_vatINChangeCommand = new Command(ExecuteVatINChangeAsync));
        }

        Command _storeDefaultBagChangeCommand;
        public Command StoreDefaultBagChangeCommand
        {
            get => _storeDefaultBagChangeCommand ?? (_storeDefaultBagChangeCommand = new Command(ExecuteStoreDefaultBagChange));
        }
        #endregion
        #region Region
        Command _currencySettingsChangeCommand;

        public Command CurrencySettingsChangeCommand
        {
            get => _currencySettingsChangeCommand ?? (_currencySettingsChangeCommand = new Command(ExecuteCurrencySettingsChange));
        }

        Command _dateSettingsChangeCommand;

        public Command DateSettingsChangeCommand
        {
            get => _dateSettingsChangeCommand ?? (_dateSettingsChangeCommand = new Command(ExecuteDateSettingsChange));
        }
        #endregion
        #region Employee
        Command _addEmployeeCommand;

        public Command AddEmployeeCommmand
        {
            get => _addEmployeeCommand ?? (_addEmployeeCommand = new Command(ExecuteAddEmployee));
        }

        Command _viewAllEmployees;

        public Command ViewAllEmployeeCommand
        {
            get => _viewAllEmployees ?? (_viewAllEmployees = new Command(ExecuteViewAllEmployees));
        }
        #endregion
        #endregion

        #region Execute Commands
        #region Store Details
        private async void ExecuteStoreNameChange()
        {
            var empId = App.GetViewModel().EmployeeId;
            bool escape = false;
            do
            {
                Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Write, databaseProvider))
                {
                    var validators = new IValidator[]
                    {
                        new RequiredValidator()
                    };

                    var viewElements = new ViewElementData[]
                    {
                        new ViewElementData(1, "Name".Translate(), App.GetViewModel().Store.StoreName, validators.AsEnumerable(), false, true),
                        new ViewElementData(2, "Abbreviation".Translate(), App.GetViewModel().Store.StoreAbbr, validators.AsEnumerable(), false, true)
                    };

                    var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(viewElements, "Confirm".Translate(), false, cancelText: "Cancel".Translate());

                    if (data.Any(d => string.IsNullOrEmpty(d.Value)))
                    {
                        return;
                    }

                    using (var db = new Helpers.Database.Database(databaseProvider))
                    {
                        var tempStore = db.Get<StoreModel>().FirstOrDefault(s => s.Id.Equals(App.GetViewModel().Store.Id));

                        _ = data.TryGetValue(1, out var storeName);
                        _ = data.TryGetValue(2, out var storeAbbr);

                        tempStore.StoreName = storeName;
                        tempStore.StoreAbbr = storeAbbr;

                        db.Update(tempStore);
                        if (!db.Save())
                        {
                            Debug.Write("Save Failed!");
                            return;
                        }

                        App.GetViewModel().Store = db.Get<StoreModel>().FirstOrDefault(s => s.Id.Equals(App.GetViewModel().Store.Id));
                        OnPropertyChanged("Store");
                        return;
                    }
                }
                var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                if (empAuthoriser == default)
                    escape = true;
            } while (!escape);
        }

        private void ExecuteStoreAddressChange()
        {

        }

        private async void ExecuteStoreLogoChange()
        {
            var empId = App.GetViewModel().EmployeeId;
            bool escape = false;
            do
            {
                Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Write, databaseProvider))
                {
                    var image = await AppServices.Get<IFile>().GetFileAsByteArray(new List<string> { ".bmp", ".gif", ".exif", ".jpg", ".jpeg", ".png", ".tiff" });
                    if (image.Length == 0)
                        return;
                    using (var db = new Helpers.Database.Database(databaseProvider))
                    {
                        var store = db.Get<StoreModel>().First(s => s.Id.Equals(Store.Id));
                        store.Logo = image;
                        if (!db.Save())
                        {
                            Debug.Write("Save Failed!");
                            return;
                        }
                        App.GetViewModel().Store = db.Get<StoreModel>().FirstOrDefault(s => s.Id.Equals(App.GetViewModel().Store.Id));
                        OnPropertyChanged(nameof(Store));
                        return;
                    }
                }
                var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                if (empAuthoriser == default)
                    escape = true;
            } while (!escape);
        }

        private async void ExecuteStoreContactNumberChange()
        {
            var empId = App.GetViewModel().EmployeeId;
            bool escape = false;
            do
            {
                Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Write, databaseProvider))
                {
                    var validators = new IValidator[]
                    {
                        new RequiredValidator()
                    };

                    var viewElements = new ViewElementData[]
                    {
                        new ViewElementData(1, "ContactNumber".Translate(), Store.ContactNumber, validators.AsEnumerable(), false, true)
                    };

                    var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(viewElements, "Confirm".Translate(), false, cancelText: "Cancel".Translate());

                    if (data.Any(d => string.IsNullOrEmpty(d.Value)))
                        return;
                    using (var db = new Helpers.Database.Database(databaseProvider))
                    {
                        var tempStore = db.Get<StoreModel>().Where(s => s.Id == Store.Id).First();
                        _ = data.TryGetValue(1, out var contactNumberText);
                        tempStore.ContactNumber = contactNumberText;
                        db.Update(tempStore);
                        if (!db.Save())
                        {
                            Debug.Write("Save Failed!");
                            return;
                        }
                        App.GetViewModel().Store = db.Get<StoreModel>().FirstOrDefault(s => s.Id.Equals(App.GetViewModel().Store.Id));
                        OnPropertyChanged(nameof(Store));
                        return;
                    }
                }
                var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                if (empAuthoriser == default)
                    escape = true;
            } while (!escape);
        }

        private async void ExecuteVatINChangeAsync()
        {
            var empId = App.GetViewModel().EmployeeId;
            bool escape = false;
            do
            {
                Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Write, databaseProvider))
                {
                    var validators = new IValidator[]
                    {
                        new RequiredValidator()
                    };

                    var viewElements = new ViewElementData[]
                    {
                        new ViewElementData(1, "VatIN".Translate(), Store.VatIN, validators.AsEnumerable(), false, true)
                    };

                    var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(viewElements, "Confirm".Translate(), false, cancelText: "Cancel".Translate());

                    if (data.Any(d => string.IsNullOrEmpty(d.Value)))
                        return;
                    using (var db = new Helpers.Database.Database(databaseProvider))
                    {
                        var tempStore = db.Get<StoreModel>().Where(s => s.Id == Store.Id).First();
                        data.TryGetValue(1, out var vatINText);
                        tempStore.VatIN = vatINText;
                        db.Update(tempStore);
                        if (!db.Save())
                        {
                            Debug.Write("Save Failed!");
                            return;
                        }
                        App.GetViewModel().Store = db.Get<StoreModel>().FirstOrDefault(s => s.Id.Equals(App.GetViewModel().Store.Id));
                        OnPropertyChanged(nameof(Store));
                        return;
                    }
                }
                var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                if (empAuthoriser == default)
                {
                    escape = true;
                }
            } while (!escape);
        }

        private async void ExecuteStoreDefaultBagChange()
        {
            var empId = App.GetViewModel().EmployeeId;
            bool escape = false;
            do
            {
                Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Write, databaseProvider))
                {
                    var validators = new IValidator[]
                    {
                        new RequiredValidator()
                    };

                    var viewElements = new ViewElementData[]
                    {
                        new ViewElementData(1, string.Format("IdArg".Translate(), "Bag".Translate()), DefaultBagId, validators.AsEnumerable(), false, true),
                    };

                    var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(viewElements, "Confirm".Translate(), false, cancelText: "Cancel".Translate());

                    if (data.Any(d => string.IsNullOrEmpty(d.Value)))
                    {
                        return;
                    }

                    using (var db = new Helpers.Database.Database(databaseProvider))
                    {
                        _ = data.TryGetValue(1, out var bagIdText);
                        if (db.IsExists<ItemModel, string>(bagIdText))
                        {
                            DefaultBagId = bagIdText;
                            return;
                        }
                        else
                        {
                            await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemNotFoundMesg".Translate(), "OK".Translate());
                            continue;
                        }
                    }
                }
                var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                if (empAuthoriser == default)
                    escape = true;
            } while (!escape);
        }
        #endregion
        #region Region
        private void ExecuteCurrencySettingsChange()
        {

        }

        private void ExecuteDateSettingsChange()
        {

        }
        #endregion
        #region Employee
        private void ExecuteAddEmployee()
        {

        }
        private void ExecuteViewAllEmployees()
        {

        }
        #endregion
        #endregion

        #region Operation
        #endregion
    }
}
