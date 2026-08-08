using Database.Models;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Services.IOHandeling;
using Plutus.Frontend.AppClient.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.FirstTimeStartUp
{
    class RecoveryViewModel : BaseViewModel
    {
        public RecoveryViewModel()
        {
            Title = "RecoveryTitle".Translate();
            Icon = "";
        }

        #region Commands
        Command _recoverDbCommand;

        public Command RecoverDbCommand
        {
            get => _recoverDbCommand ?? (_recoverDbCommand = new Command(ExecuteRecoverDb));
        }
        #endregion

        #region Execute Commands
        private async void ExecuteRecoverDb()
        {
            // ⚠ EVERYTHING is inside the try now. It used to start AFTER the file picker and the
            // copy — which is why "Recovery doesn't appear to work" was really "Recovery kills the
            // app": see the cancel case below.
            try
            {
                var srcFile = await AppServices.Get<IFile>().GetFile(new List<string> { ".db" });

                // ⚠ THE CRASH. PickSingleFileAsync returns NULL when the dialog is cancelled or
                // closed, and Copy's `null is StorageFile` is false, so it fell through to
                // `throw new ArgumentException("File is not of type StorageFile")` — from an
                // `async void` command, outside any try, straight to the WinUI dispatcher. Pressing
                // Cancel on the file picker took the whole app down.
                if (srcFile == null)
                {
                    // Cancelling is not a failure and does not deserve a dialog.
                    return;
                }

                // ⚠ The result was discarded. A copy that failed then looked exactly like one that
                // worked, and the next step happily created an empty database instead.
                if (!await AppServices.Get<IFile>().Copy(srcFile, FileSystem.AppDataDirectory, "Database.db"))
                {
                    await App.Current.MainPage.DisplayAlert(
                        "Couldn't restore", "That file couldn't be copied onto this till.", "OK".Translate());
                    return;
                }

                int employees;
                using (var db = new Helpers.Database.Database(Database.Enums.DatabaseProvider.Sqlite))
                {
                    // ⚠ Ask the question recovery actually exists to answer. It used to read one
                    // ITEM and never look at the result, so an empty or wrong-schema database
                    // "recovered" successfully and left nobody able to sign in.
                    employees = db.Get<EmployeeModel>().Count();
                }

                if (employees == 0)
                {
                    await App.Current.MainPage.DisplayAlert(
                        "Nothing to sign in with",
                        "That database restored, but it contains no staff accounts. Check you picked the right file.",
                        "OK".Translate());
                    return;
                }

                DatabaseProviderSetting = Database.Enums.DatabaseProvider.Sqlite.ToString();
                App.Current.MainPage = new LoginView();
            }
            catch (Exception ex)
            {
                // ⚠ The reason used to reach Debug.WriteLine only — invisible on a real till. It now
                // goes to the crash log, which is the file someone can actually send.
                Services.Analytics.CrashLog.Write("RecoveryViewModel.RecoverDb", ex);
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "CriticalIssue".Translate(), "OK".Translate());
            }
        }
        #endregion
    }
}
