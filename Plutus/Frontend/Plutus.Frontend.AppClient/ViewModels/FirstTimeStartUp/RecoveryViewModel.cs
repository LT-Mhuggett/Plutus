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
            var srcFile = await AppServices.Get<IFile>().GetFile(new List<string> { ".db" });
            await AppServices.Get<IFile>().Copy(srcFile, FileSystem.AppDataDirectory, "Database.db");

            try
            {
                using(var db = new Helpers.Database.Database(Database.Enums.DatabaseProvider.Sqlite))
                {
                    var item = db.Get<ItemModel>().FirstOrDefault();
                }

                DatabaseProviderSetting = Database.Enums.DatabaseProvider.Sqlite.ToString();
                App.Current.MainPage = new LoginView();
            }
            catch(Exception ex)
            {
                await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "CriticalIssue".Translate(), "OK".Translate());
                Debug.WriteLine(ex.Message);
                return;
            }
        }
        #endregion
    }
}
