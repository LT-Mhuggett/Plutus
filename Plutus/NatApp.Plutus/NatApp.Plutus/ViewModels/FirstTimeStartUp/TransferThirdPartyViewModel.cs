using Database.Enums;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using NatApp.Plutus.Exceptions;
using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Services.IOHandeling.Picker;
using NatApp.Plutus.Services.POSHandeling;
using NatApp.Plutus.Services.ThirdPartyTransfer;
using NatApp.Plutus.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace NatApp.Plutus.ViewModels.FirstTimeStartUp
{
    class TransferThirdPartyViewModel : BaseViewModel
    {
        public TransferThirdPartyViewModel()
        {
            Title = "TPT".Translate();
            Icon = "";
        }

        #region Commands
        private Command _copperCommand;
        public Command CopperCommand
        {
            get => _copperCommand ?? (_copperCommand = new Command(ExecuteCopperCommandAsync));
        }
        #endregion

        #region Command Executions
        private async void ExecuteCopperCommandAsync()
        {
            App.SetLoading(true);

            var folder = await GetFolder();

            if (folder != null)
            {
                InitCopperTransfer(folder, Environment.ProcessorCount / 2);
                return;
            }

            App.SetLoading(false);
        }
        #endregion

        #region Operations
        /// <summary>
        /// Get and set inital fodler to look for files
        /// </summary>
        /// <returns>Init folder for lookup</returns>
        private async Task<object> GetFolder()
        {
            object folder = null;

            try
            {
                DependencyService.Get<IFolderPicker>().InitFolderPicker("*");

                folder = await DependencyService.Get<IFolderPicker>().PickFolderAsync();
            }
            catch (PlatformNotSupportedException pNSEx)
            {
                await Application.Current.MainPage.DisplayAlert("Alert".Translate(), "DeviceNotSupported".Translate(), "OK".Translate());
                Debug.Write(pNSEx.Message);
            }
            catch (FolderPickerNotInitalizedException fPNIEx)
            {
                await Application.Current.MainPage.DisplayAlert("Alert".Translate(), "CriticalIssue".Translate(), "OK".Translate());
                Debug.Write(fPNIEx.Message);
            }

            return folder;
        }

        #region Copper Transfer
        /// <summary>
        /// Execute copper trasfer
        /// </summary>
        /// <param name="copperFolder"></param>
        /// <param name="processesToRun"></param>
        private async void InitCopperTransfer(object copperFolder, int processesToRun)
        {
            ///
            /// Fetch Data
            ///
            App.GetViewModel().CurrentLoadingItem = string.Format("InitArg".Translate(), "FolderLookup".Translate());
            var folderQueueAndEmpCount = await DependencyService.Get<ICopperTransfer>().GetFoldersForProcessing(copperFolder);

            App.GetViewModel().CurrentLoadingItem = string.Format("RetrievingArg".Translate(), string.Format("DataArg".Translate(), "Basic".Translate()));
            var baseData = await DependencyService.Get<ICopperTransfer>().GetStoreAndTaxDataAsync(folderQueueAndEmpCount.Item1.Dequeue());

            App.GetViewModel().CurrentLoadingItem = string.Format("RetrievingArg".Translate(), string.Format("DataArg".Translate(), "Item".Translate()));
            var itemData = await DependencyService.Get<ICopperTransfer>().GetItemsAsync(folderQueueAndEmpCount.Item1.Dequeue(), baseData.Item2);

            App.GetViewModel().CurrentLoadingItem = string.Format("RetrievingArg".Translate(), string.Format("DataArg".Translate(), "Employee".Translate()));
            var passwordSaltAmount = folderQueueAndEmpCount.Item2;

            var passwordSalts = new Queue<string>();

            for (int i = 0; i < passwordSaltAmount; i++)
                passwordSalts.Enqueue(Convert.ToBase64String(Helpers.Security.Password.GenerateSalt()));

            var employees = await DependencyService.Get<ICopperTransfer>().GetEmployeesAsync(folderQueueAndEmpCount.Item1.Dequeue(), passwordSalts);

            App.GetViewModel().CurrentLoadingItem = "";
            ///
            /// Set Server Style
            ///
            DatabaseProvider dbProvider = DatabaseProvider.Sqlite;
            {
                var i = 0;
                do
                {
                    if (i == 2)
                    {
                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "MissingRequiredDataMesg".Translate(), "OK".Translate());
                        App.SetLoading(false);
                        return;
                    }
                    var dbProviderAS = await App.Current.MainPage.DisplayActionSheet("ServerStyle".Translate(), "Cancel".Translate(), null, "Cloud".Translate(), "Local".Translate());

                    if (dbProviderAS == null)
                    {
                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(), string.Format("NotValid".Translate(), "ServerStyle".Translate()), "OK".Translate());
                        continue;
                    }

                    if (dbProviderAS == "Local".Translate())
                        dbProvider = DatabaseProvider.Sqlite;
                    else if (dbProviderAS == "Cloud".Translate())
                        dbProvider = DatabaseProvider.Cloud;
                    break;
                } while (i <= 2);
            }
            if (dbProvider == DatabaseProvider.Cloud)
            {
                App.SetLoading(false);
                throw new NotImplementedException();
            }
            DatabaseProviderSetting = dbProvider.ToString();

            App.GetViewModel().CurrentLoadingItem = "Saving".Translate();
            ///
            /// Prepare Employees
            /// 
            var emps = new List<EmployeeModel>();
            List<AuthActions> authActions;
            using (var dbContext = new Helpers.Database.Database(dbProvider))
            {
                dbContext.Init();
                dbContext.Save();
                authActions = dbContext.Get<AuthActions>().AsNoTracking().ToList();
            }
            foreach (var emp in employees)
            {
                emp.Item1.HashedPassword = Convert.ToBase64String(
                    Helpers.Security.Password.ComputeHash(
                        emp.Item2,
                        Convert.FromBase64String(emp.Item1.Salt)
                    ));
                emp.Item1.EmpAuths = new List<Emp_AuthActions>();
                Queue<Tuple<string, List<string>, string>> authSetupPrep = new Queue<Tuple<string, List<string>, string>>();
                foreach (var item in authActions)
                {
                    List<string> permisions = Enum.GetNames(typeof(Permissions)).ToList();
                    authSetupPrep.Enqueue(Tuple.Create<string, List<string>, string>(item.Name, permisions, null));
                }
                var empAuths = new Queue<string>(await Helpers.CustomViews.SliderAlertHelper.LaunchSliderAlertAsync(authSetupPrep, "Confirm".Translate(), false, string.Format("PermisionsArg".Translate(), emp.Item1.FullName)));
                emp.Item1.Store = baseData.Item1;
                emp.Item1.EmpAuths = new List<Emp_AuthActions>();
                foreach(var item in authActions)
                {                   
                    Enum.TryParse(empAuths.Dequeue(), out Permissions empAuth);
                    if (empAuth != Permissions.None)
                        empAuth |= empAuth - 1;
                    emp.Item1.EmpAuths.Add(new Emp_AuthActions { AuthAId = item.Id, Permissions = empAuth });
                }
                emps.Add(emp.Item1);
            }

            
            ///
            /// Process Data
            ///
            
            using (var dbContext = new Helpers.Database.Database(dbProvider))
            {
                //Add Store
                dbContext.Add(baseData.Item1);

                //Add Taxes
                foreach (var tax in baseData.Item2)
                    dbContext.Add(tax);

                //Add Items
                for (int i = 0; i < itemData.Item1.Count; i++)
                    dbContext.Add(itemData.Item1.Dequeue());

                //Add Employees
                foreach (var emp in emps)
                    dbContext.Add(emp);
                if (!dbContext.Save())
                {
                    App.SetLoading(false);
                    await Application.Current.MainPage.DisplayAlert("Alert".Translate(), "CriticalIssue".Translate(), "OK".Translate());
                }
            }

            ///
            /// Display non-transfered items
            ///
            if (itemData.Item2.Count > 0)
            {
                var itemsText = "";
                for (int i = 0; i <= itemData.Item2.Count - 1;)
                    itemsText += $"  \u2022 {itemData.Item2.Dequeue()}\n";
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), string.Format("FailedtoAddItems".Translate(), itemsText), "OK".Translate());
            }

            ///
            /// Display and allow printer selection
            ///
            using (var printerMgr = new PosPrinterManager())
            {
                await printerMgr.GetPrinterList();

                if (printerMgr.Printers.Count > 0)
                {
                    var result = await Application.Current.MainPage.DisplayActionSheet("PrinterList".Translate(), "Cancel".Translate(), null, printerMgr.Printers.Keys.ToArray());
                    if (result != "Cancel".Translate())
                        PrinterLogicalNameSetting = result;
                    else
                        await Application.Current.MainPage.DisplayAlert("Warning".Translate(), "NoPrinter".Translate(), "Cancel".Translate());
                }
                else
                    await Application.Current.MainPage.DisplayAlert("Warning".Translate(), "NoPrinter".Translate(), "Cancel".Translate());
            }

            /// Move to Login page
            Application.Current.MainPage = new NavigationPage(new LoginView());
        }
        #endregion
        #endregion
    }
}
