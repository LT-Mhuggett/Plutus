using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Helpers.Security;
using NatApp.Plutus.Services.IOHandeling;
using NatApp.Plutus.Services.POSHandeling;
using Plugin.FilePicker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace NatApp.Plutus.ViewModels.MainTill.Settings
{
    public class SettingsViewModel : BaseViewModel
    {
        #region Private Fields
        private string _version;
        #endregion

        #region Properties
        public string Version
        {
            get=> _version;
            set => SetProperty(ref _version, value);
        }
        #endregion

        public SettingsViewModel(StackLayout leftColumn, StackLayout rightColumn)
        {
            Title = "Settings".Translate();
            Icon = "md-settings";
            Version = $"Ver. {AppInfo.VersionString}";

            var buttonsAndSubHeadings = new List<Tuple<string, string>>
            {
                Tuple.Create("Database".Translate(),""),
                Tuple.Create("BackupDb".Translate(), "BackupDbCommand"),
                Tuple.Create("RestoreDb".Translate(), "RestoreDbCommand"),
                Tuple.Create("DeleteDb".Translate(), "DeleteDbCommand"),
                Tuple.Create("Other", ""),
                Tuple.Create("ChangePrinter".Translate(), "ChangePrinter"),
                Tuple.Create("","")
            };
            StackLayout stack = null;
            int? n = null;
            for(int i = 0; i < buttonsAndSubHeadings.Count; i++)
            {
                if(buttonsAndSubHeadings[i].Item2 == "")
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
                        FontSize = Device.GetNamedSize(NamedSize.Medium, typeof(Label)),
                        TextColor = Color.LightGray,
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

        #region Commands
        #region Database
        Command _backupDbCommand;
        public Command BackupDbCommand
        {
            get => _backupDbCommand ?? (_backupDbCommand = new Command(ExecuteBackupDb));
        }

        Command _restoreDbCommand;
        public Command RestoreDbCommand
        {
            get => _restoreDbCommand ?? (_restoreDbCommand = new Command(ExecuteRestoreDb));
        }

        Command _deleteDbCommand;
        public Command DeleteDbCommand
        {
            get => _deleteDbCommand ?? (_deleteDbCommand = new Command(ExecuteDeleteDb));
        }
        #endregion

        Command _changePrinter;
        public Command ChangePrinter
        {
            get => _changePrinter ?? (_changePrinter = new Command(ExecuteChangePrinter));
        }
        #endregion

        #region Execute Commands
        #region Database
        private async void ExecuteBackupDb()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                var empId = App.GetViewModel().EmployeeId;
                bool escape = false;
                do
                {
                    Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                    if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Execute, databaseProvider))
                    {
                        await DependencyService.Get<IFile>().Copy(
                            Path.Combine(FileSystem.AppDataDirectory, "Database.db"),
                            new List<KeyValuePair<string, List<string>>>
                            {
                                new KeyValuePair<string, List<string>>("SQL Database", new List<string> {".db"})
                            },
                            string.Format("{0} - Database - {1}", App.GetViewModel().Store.StoreName,
                                DateTime.Now.ToString(CultureInfo.CurrentCulture))
                            );
                        return;
                    }
                    var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                    if (empAuthoriser == default)
                        escape = true;
                } while (!escape);
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteRestoreDb()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                var empId = App.GetViewModel().EmployeeId;
                bool escape = false;
                do
                {
                    Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                    if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Execute, databaseProvider))
                    {
                        var dbFile = await DependencyService.Get<IFile>().GetFile(new List<string> { ".db" });
                        if (dbFile == null)
                            return;
                        if (await DependencyService.Get<IFile>().Copy(
                            dbFile,
                            FileSystem.AppDataDirectory,
                            "Database.db"))
                            await App.Current.MainPage.DisplayAlert("Success".Translate(), "Saved".Translate(), "OK".Translate());
                        else
                            await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "CriticalIssue".Translate(), "OK".Translate());
                        return;
                    }
                    var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                    if (empAuthoriser == default)
                        escape = true;
                } while (!escape);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteDeleteDb()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                if (await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "AreYouSureDeleteDb".Translate(), "Yes".Translate(), "Cancel".Translate()))
                {
                    var empId = App.GetViewModel().EmployeeId;
                    bool escape = false;
                    do
                    {
                        Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                        if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Execute, databaseProvider))
                        {
                            if (await DependencyService.Get<IFile>().DeleteFile(Path.Combine(FileSystem.AppDataDirectory, "Database.db")))
                            {

                            }
                            return;
                        }
                        var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                        if (empAuthoriser == default)
                            escape = true;
                    } while (!escape);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        private async void ExecuteChangePrinter()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                var empId = App.GetViewModel().EmployeeId;
                bool escape = false;
                do
                {
                    Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                    if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Execute, databaseProvider))
                    {
                        using (var printerMgr = new PosPrinterManager())
                        {
                            await printerMgr.GetPrinterList();

                            if (printerMgr.Printers.Count > 0)
                            {
                                var result = await Application.Current.MainPage.DisplayActionSheet("PrinterList".Translate(), "Cancel".Translate(), null, printerMgr.Printers.Keys.ToArray());
                                if (result != "Cancel".Translate())
                                    PrinterLogicalNameSetting = result;
                                else
                                {
                                    await Application.Current.MainPage.DisplayAlert("Warning".Translate(), "NoPrinter".Translate(), "Cancel".Translate());
                                    PrinterLogicalNameSetting = null;
                                }
                            }
                            else
                            {
                                await Application.Current.MainPage.DisplayAlert("Warning".Translate(), "NoPrinter".Translate(), "Cancel".Translate());
                                PrinterLogicalNameSetting = null;
                            }
                        }
                        return;
                    }
                    var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                    if (empAuthoriser == default)
                        escape = true;
                } while (!escape);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
    }
}
