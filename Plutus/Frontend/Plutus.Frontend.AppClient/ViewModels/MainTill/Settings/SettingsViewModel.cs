using CommonPOSLibrary.Exceptions;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Security;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Services.IOHandeling;
using Plutus.Frontend.AppClient.Services.POSHandeling;
using Plutus.Client.Storage;
using Plutus.SharedKernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Settings
{
    public class SettingsViewModel : BaseViewModel
    {
        #region Private Fields
        private string _version;
        #endregion

        #region Properties
        public string Version
        {
            get => _version;
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
                Tuple.Create("Archive legacy database", "BackupDbCommand"),
                Tuple.Create("RestoreDb".Translate(), "RestoreDbCommand"),
                Tuple.Create("Printer", ""),
                Tuple.Create("ChangePrinter".Translate(), "ChangePrinterCommand"),
                Tuple.Create("PrintTestPage".Translate(), "PrintTestPageCommand"),
                Tuple.Create("CheckoutOptions", ""),
                Tuple.Create("AskForReceiptOption".Translate(), "ChangeAskForReceiptOptionCommand"),
                Tuple.Create("ChangeCashDrawerExists".Translate(), "ChangeCashDrawerExistsCommand"),
                //Tuple.Create("ChangeBarcodeType".Translate(), "ChangeBarcodeTypeCommand"),
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

        #endregion

        #region Printer
        Command _changePrinterCommand;
        public Command ChangePrinterCommand
        {
            get => _changePrinterCommand ?? (_changePrinterCommand = new Command(ExecuteChangePrinter));
        }

        Command _printTestPageCommand;
        public Command PrintTestPageCommand
        {
            get => _printTestPageCommand ?? (_printTestPageCommand = new Command(ExecutePrintTestPage));
        }

        Command _changeAskForReceiptOptionCommand;
        public Command ChangeAskForReceiptOptionCommand
        {
            get => _changeAskForReceiptOptionCommand ?? (_changeAskForReceiptOptionCommand = new Command(ExecuteChangeAskForReceiptOption));
        }

        Command _changeCashDrawerExistsCommand;
        public Command ChangeCashDrawerExistsCommand
        {
            get => _changeCashDrawerExistsCommand ?? (_changeCashDrawerExistsCommand = new Command(ExecuteChangeCashDrawerExists));
        }
        /*
        Command _changeBarcodeTypeCommand;
        public Command ChangeBarcodeTypeCommand
        {
            get => _changeBarcodeTypeCommand ?? (_changeBarcodeTypeCommand = new Command(ExecuteChangeBarcodeType));
        }*/
        #endregion
        #endregion

        #region Execute Commands
        #region Database
        /// <summary>
        /// Archive the legacy database — the cutover on-ramp (step 21, binding default 9.3).
        ///
        /// ⚠ THIS IS WHAT UNLOCKS ENROLMENT. `EnrolmentFlow.BlockedReasonAsync` refuses to enrol a
        /// till that still holds an un-archived legacy file, because that file is the shop's sales
        /// history and the migration's only input. Until now nothing could archive, so the gate was
        /// passed `null` and did not run at all; this is the capability that lets it be switched on.
        ///
        /// ⚠ It was "Backup database": it copied the file through a save dialog, gated on
        /// `IsAuthorised` (the legacy `AuthActions` table a portal till has no rows in) via
        /// `EmployeeId` (null for every roster operator), falling back to
        /// `RequestAuthorisedUserInput` (which never terminates). So on the tills that most need to
        /// archive, it could not run — and even when it did, nothing recorded that it had happened.
        ///
        /// ⚠ COPY, NEVER MOVE, and never overwrite an existing archive: a second cutover must not
        /// quietly replace the only copy of the first one's data.
        /// </summary>
        private async void ExecuteBackupDb()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var gate = Services.Security.TillGate.Check(
                    App.GetViewModel().SignedInOperator, PermissionCatalogue.PosSettingsManage);

                if (!gate.Allowed)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                    return;
                }

                var legacyPath = Path.Combine(FileSystem.AppDataDirectory, "Database.db");
                if (!File.Exists(legacyPath))
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "There's no previous database on this till to archive.", "OK".Translate());
                    return;
                }

                var archiveDir = Path.Combine(FileSystem.AppDataDirectory, "legacy-archive");
                string archivedTo;
                try
                {
                    archivedTo = Plutus.Client.Storage.Cutover.ArchiveLegacyDatabase(
                        legacyPath, archiveDir, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    CrashLog.Write("SettingsViewModel.ExecuteBackupDb", ex);
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Couldn't archive the previous database. Nothing has been changed or deleted.",
                        "OK".Translate());
                    return;
                }

                // ⚠ STAMPED ONLY AFTER THE COPY SUCCEEDED. The stamp is what opens the enrolment
                // gate, so writing it first would let a till enrol having archived nothing.
                await Services.Storage.TillStoreAccess.UseAsync(async s =>
                {
                    await s.SetMetaAsync(MetaKeys.LegacyArchivedAtUtc,
                        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    return true;
                });

                await App.Current.MainPage.DisplayAlert("Archived",
                    $"The previous database has been archived to:\n\n{archivedTo}\n\n" +
                    "The original is untouched. This till can now be enrolled.", "OK".Translate());
            }
            catch (Exception ex)
            {
                CrashLog.Write("SettingsViewModel.ExecuteBackupDb", ex);
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
                        var dbFile = await AppServices.Get<IFile>().GetFile(new List<string> { ".db" });
                        if (dbFile == null)
                            return;
                        if (await AppServices.Get<IFile>().Copy(
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

        // ⚠ "DELETE DATABASE" IS GONE (cutover step 21), and not merely disabled.
        //
        // It deleted the legacy `Database.db` outright, behind a single "are you sure". That file
        // is the shop's entire sales history and the ONLY input the migration has — there is no
        // server copy of a pre-cutover till's data, and no undo. A button that can destroy a
        // business's records permanently does not belong on a settings screen next to the printer
        // picker, and it certainly does not belong there gated on an authorisation check that
        // cannot succeed on the tills that still have something to lose.
        //
        // "Archive legacy database" above is the operation that was actually wanted.
        #endregion

        #region Printer
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
                            var printerId = await printerMgr.SelectPrinterAndGetPrinterId();

                            if (!string.IsNullOrEmpty(printerId))
                                PrinterLogicalNameSetting = printerId;
                            else
                            {
                                await Application.Current.MainPage.DisplayAlert("Warning".Translate(), "NoPrinter".Translate(), "Cancel".Translate());
                                PrinterLogicalNameSetting = printerId;
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

        private async void ExecutePrintTestPage()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                if(DeviceInfo.Idiom == DeviceIdiom.Desktop)
                {
                    using (var printMgr = new PosPrinterManager())
                    {
                        if(await printMgr.InitPrinter())
                        {
                            printMgr.WriteText("Test Nomral text");
                            printMgr.WriteText("Test Bold On", bold: "true");
                            printMgr.WriteText("Test Underline On", underline: "true");
                            printMgr.WriteText("Test align Center", align: "cntr");
                            printMgr.WriteText("Test align Right", align: "rght");
                            printMgr.WriteText("test Bold + Underline", bold: "true", underline: "true");
                            printMgr.WriteText("test Bold + Algin Center", bold: "true", align: "cntr");
                            printMgr.WriteText("test Bold + Algin Right", bold: "true", align: "rght");
                            printMgr.WriteText("test Underline + Algin Center", underline: "true", align: "cntr");
                            printMgr.WriteText("test Underline + Algin Right", underline: "true", align: "rght");
                            printMgr.ScoreReceipt();
                            printMgr.WriteBarcode("123456789", App.GetViewModel().BarcodeSymbologySetting, 100, "cntr");
                            printMgr.ScoreReceipt();
                            printMgr.WriteText("Blank Line Test (3)");
                            printMgr.BlankLine();
                            printMgr.BlankLine();
                            printMgr.BlankLine();
                            printMgr.WriteText("End of Test Print!");
                            printMgr.CutPaper();
                            await printMgr.ExecuteOposOrPdfAsync();
                            await printMgr.CloseConnection();
                        }
                    }
                }
            }
            catch(POSObjectException pOSObjectException)
            {
                Debug.WriteLine(pOSObjectException.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteChangeAskForReceiptOption()
        {
            if (IsBusy)
                return;
            IsBusy = true;

            AskForReceipt = await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "AskForReceiptPrintingText".Translate(), "Yes".Translate(), "No".Translate());

            IsBusy = false;
        }

        private async void ExecuteChangeCashDrawerExists()
        {
            if (IsBusy)
                return;
            IsBusy = true;

            TryCashDrawer = await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "ChangeCashDrawerExistsText".Translate(), "Yes".Translate(), "No".Translate());

            IsBusy = false;
        }

        /*
        private async void ExecuteChangeBarcodeType()
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
                        bool tryAgain;
                        do
                        {
                            tryAgain = false;
                            using (var printerMgr = new PosPrinterManager())
                            {
                                var barcodeTypes = await printerMgr.GetBarcodeSymbols();
                                var selectedType = await App.Current.MainPage.DisplayActionSheet("ChangeBarcodeType".Translate(), "Cancel".Translate(), null, barcodeTypes);
                                if (selectedType != "Cancel".Translate())
                                {
                                    var barcodeTestID = $"{DateTime.Now.Year}" +
                                        $"{DateTime.Now.Month}" +
                                        $"{DateTime.Now.Day}" +
                                        $"{DateTime.Now.Hour}" +
                                        $"{DateTime.Now.Minute}" +
                                        $"{DateTime.Now.Second}" +
                                        $"{DateTime.Now.Millisecond}";
                                    if (await printerMgr.InitPrinter())
                                    {
                                        printerMgr.WriteText("TestPrint".Translate(), "cntr", "true");
                                        printerMgr.BlankLine();
                                        printerMgr.WriteText("TestPrint".Translate(), "cntr", "true");
                                        printerMgr.WriteBarcode(barcodeTestID, selectedType, 100, "cntr");
                                        printerMgr.CutPaper();
                                        await printerMgr.SetupExecutePrintMultiLine();
                                        if (!await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "CheckReceiptCorrect".Translate(), "Correct".Translate(), "TryAgain".Translate()))
                                            tryAgain = true;
                                        else
                                            BarcodeSymbologySetting = selectedType;
                                    }
                                    else
                                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "PrinterNotFound".Translate(), "OK".Translate());
                                }
                                escape = true;
                            }
                        } while (tryAgain);
                    }
                    if (!escape)
                    {
                        var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                        if (empAuthoriser == default)
                            escape = true;
                    }
                } while (!escape);
            }
            finally
            {
                IsBusy = false;
            }
        }
        */
        #endregion
        #endregion
    }
}
