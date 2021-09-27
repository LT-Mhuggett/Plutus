using Database.Models;
using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Services.IOHandeling;
using NatApp.Plutus.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace NatApp.Plutus.ViewModels.FirstTimeStartUp
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
            var srcFile = await DependencyService.Get<IFile>().GetFile(new List<string> { ".db" });
            await DependencyService.Get<IFile>().Copy(srcFile, FileSystem.AppDataDirectory, "Database.db");

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
