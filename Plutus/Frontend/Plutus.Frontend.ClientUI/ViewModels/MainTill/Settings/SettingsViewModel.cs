using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Helpers;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.IOHandeling;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.PosHandeling;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;

namespace Plutus.Frontend.ClientUI.ViewModels.MainTill.Settings
{
    /// <summary>
    /// Settings screen ported from the NatApp SettingsView. Groups: Database, Printer,
    /// Till options and Checkout options. Toggle options are backed by the static
    /// <see cref="Core.Settings"/> preference store; database/printer actions are gated on the
    /// current user's Admin permission via <see cref="Authorisation"/>.
    /// </summary>
    public partial class SettingsViewModel : BaseViewModel
    {
        private readonly IFileService _fileService;

        private string DatabasePath => Path.Combine(FileSystem.Current.AppDataDirectory, "Database.db");

        public string Version { get; }

        #region Toggle options (backed by Core.Settings preference store)
        public bool TillListOrderReversed
        {
            get => Core.Settings.TillListViewOrderReversed;
            set
            {
                if (Core.Settings.TillListViewOrderReversed == value) return;
                Core.Settings.TillListViewOrderReversed = value;
                OnPropertyChanged();
            }
        }

        public bool AskForReceipt
        {
            get => Core.Settings.AskForReceipt;
            set
            {
                if (Core.Settings.AskForReceipt == value) return;
                Core.Settings.AskForReceipt = value;
                OnPropertyChanged();
            }
        }

        public bool CashDrawerExists
        {
            get => Core.Settings.TryCashDrawer;
            set
            {
                if (Core.Settings.TryCashDrawer == value) return;
                Core.Settings.TryCashDrawer = value;
                OnPropertyChanged();
            }
        }
        #endregion

        public SettingsViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService,
            IRepositoryWrapper repositoryWrapper, IFileService fileService)
            : base(logger, appState, loadingViewService, repositoryWrapper)
        {
            _fileService = fileService;
            Title = "Settings";
            Icon = ""; // FontAwesome cog
            Version = $"Ver. {AppInfo.Current.VersionString}";
        }

        #region Database commands
        [RelayCommand]
        private async Task BackupDb()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                if (!await EnsureAdminAsync()) return;

                if (!File.Exists(DatabasePath))
                {
                    await Alert("Hmm", "There is no local database to back up.");
                    return;
                }

                var suggestedName = string.Format("{0} - Database - {1}.db",
                    AppState.Business?.Name ?? "Plutus",
                    DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.CurrentCulture));

                if (await _fileService.SaveCopyAsync(DatabasePath, suggestedName))
                    await Alert("Success", "Database backed up.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                await Alert("Hmm", "Could not back up the database.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task RestoreDb()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                if (!await EnsureAdminAsync()) return;

                var picked = await _fileService.PickFileAsync(".db");
                if (string.IsNullOrEmpty(picked))
                    return;

                File.Copy(picked, DatabasePath, overwrite: true);

                // SQLite keeps write-ahead-log side-car files next to the database; remove them so
                // the restored database isn't reconciled against stale WAL/SHM data on next open.
                foreach (var sidecar in new[] { DatabasePath + "-wal", DatabasePath + "-shm" })
                    if (File.Exists(sidecar))
                        File.Delete(sidecar);

                await Alert("Success", "Database restored. Please restart the app for the restored data to take effect.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                await Alert("Hmm", "Could not restore the database.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task DeleteDb()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                if (!await App.Current.MainPage.DisplayAlert("Hmm", "Are you sure you want to delete the local database?", "Yes", "Cancel"))
                    return;

                if (!await EnsureAdminAsync()) return;

                if (_fileService.DeleteFile(DatabasePath))
                    await Alert("Success", "Database deleted.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                await Alert("Hmm", "Could not delete the database.");
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        #region Printer commands
        [RelayCommand]
        private async Task ChangePrinter()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                if (!await EnsureAdminAsync()) return;

                using var printerManager = ServiceHelper.GetService<PosPrinterManager>();
                var printerId = await printerManager.SelectPrinterAndGetPrinterId();

                if (!string.IsNullOrEmpty(printerId))
                    Core.Settings.PrinterLogicalNameSetting = printerId;
                else
                    await Alert("Warning", "No printer was selected.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                await Alert("Hmm", "Could not change the printer.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task PrintTestPage()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                using var printerManager = ServiceHelper.GetService<PosPrinterManager>();
                if (await printerManager.InitPrinter())
                {
                    printerManager.WriteText("Test Normal text");
                    printerManager.WriteText("Test Bold On", bold: "true");
                    printerManager.WriteText("Test Underline On", underline: "true");
                    printerManager.WriteText("Test align Center", align: "cntr");
                    printerManager.WriteText("Test align Right", align: "rght");
                    printerManager.WriteText("Test Bold + Underline", bold: "true", underline: "true");
                    printerManager.ScoreReceipt();
                    printerManager.WriteBarcode("123456789", Core.Settings.BarcodeSymbologySetting, 100, "cntr");
                    printerManager.ScoreReceipt();
                    printerManager.WriteText("Blank Line Test (3)");
                    printerManager.BlankLine();
                    printerManager.BlankLine();
                    printerManager.BlankLine();
                    printerManager.WriteText("End of Test Print!");
                    printerManager.CutPaper();
                    await printerManager.ExecuteOposOrPdfAsync();
                }
                else
                {
                    await Alert("Hmm", "No printer is configured. Set one in 'Change Printer' first.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                await Alert("Hmm", "Could not print the test page.");
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion

        #region Helpers
        /// <summary>
        /// Guard for the database/printer administration actions: an authenticated user with a
        /// loaded business context must be present.
        /// </summary>
        /// <remarks>
        /// TODO: Enforce the specific "Admin"/Execute permission here, as the NatApp original did.
        /// It is not wired up yet for two reasons: (1) the client's
        /// <see cref="Services.Repository.Contracts.IRepositoryWrapper"/> interface does not expose
        /// the expression-based lookup the check needs (and there is no backend verify endpoint),
        /// and (2) prompting a *different* administrator to authorise interactively is impossible
        /// because <see cref="Plutus.Entities.Models.Employee"/> carries no credential fields
        /// (HashedPassword/Salt) on the client. In the current dev-bypass state no user is signed
        /// in, so these actions are correctly blocked.
        /// </remarks>
        private async Task<bool> EnsureAdminAsync()
        {
            if (AppState.CurrentActiveUser.Key == Guid.Empty || AppState.Business == null)
            {
                await Alert("Not authorised", "You must be signed in as an administrator to perform this action.");
                return false;
            }

            return true;
        }

        private static Task Alert(string title, string message)
            => App.Current.MainPage.DisplayAlert(title, message, "OK");
        #endregion
    }
}
