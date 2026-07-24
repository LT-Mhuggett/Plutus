using Database.Enums;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Views;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels
{
    class LoginViewModel : BaseViewModel
    {
        #region Private Variables
        /// <summary>
        /// Holds data in the email or user id field
        /// </summary>
        private string _email_UserId;

        /// <summary>
        /// Holds data in the password field
        /// </summary>
        private string _password;

        #endregion

        #region Public Properties
        public string Email_Userid
        {
            get { return _email_UserId; }
            set { SetProperty(ref _email_UserId, value); }
        }

        public string Email_UserId_Placeholder { get; }

        public string Password
        {
            get { return _password; }
            set { SetProperty(ref _password, value); }
        }
        #endregion

        #region Commands
        #region Login Command
        Command _loginCommand;

        public Command LoginCommand
        {
            get => _loginCommand ?? (_loginCommand = new Command(ExecuteLoginCommand, CanLogin));
        }
        #endregion
        #region ShowUsers
        Command _showLoggedUsersCommand;

        public Command ShowLoggedUsersCommand
        {
            get => _showLoggedUsersCommand ?? (_showLoggedUsersCommand = new Command(ExecuteShowLoggedUsers));
        }
        #endregion
        #endregion

        #region Command Execution
        private void ExecuteShowLoggedUsers()
        {
            throw new NotImplementedException();
            //Implement Show Logged Users Page
        }
        #endregion

        public LoginViewModel()
        {
            Title = "Login";
            Email_UserId_Placeholder = string.Format("{0}/{1} {2}", "EMail".Translate(), "User".Translate(), "Id".Translate());
            App.SetLoading(false);
        }

        public bool CanLogin()
        {
            return string.IsNullOrEmpty(_password) && string.IsNullOrEmpty(_email_UserId);
        }

        #region Command Execution
        private async void ExecuteLoginCommand()
        {
            App.SetLoading(true);
            try
            {
                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);
                using (var dbHelper = new Helpers.Database.Database(databaseProvider))
                {
                    var tempUser = await dbHelper.Get<EmployeeModel>()
                        .Include(e => e.Store)
                        .Where(e => e.Id.Equals(_email_UserId) ||
                            EF.Functions.Like(e.Email.ToLower(), _email_UserId.ToLower())).FirstOrDefaultAsync();
                    if (tempUser == null || !await Task.Run(() =>
                        Helpers.Security.Password.Verify(_password, Convert.FromBase64String(tempUser.Salt),
                            Convert.FromBase64String(tempUser.HashedPassword))))
                    {

                        Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Login", new Dictionary<string, string> { { "Authorised", "False" } });
                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "DetailsNotCorrectORUserNotExistMesg".Translate(), "OK".Translate());
                        return;
                    }

                    Logger.LogEvent(AppLogLevel.Info, $"{this.GetType().Name}: Login", new Dictionary<string, string> { { "Authorised", "True" } });
                    var store = tempUser.Store;

                    App.GetViewModel().Employees.Add(tempUser);
                    // May change Store setter
                    App.GetViewModel().Store = store;
                    App.Current.MainPage = new AppShell();

                    Shell.Current.CurrentPage.ToolbarItems.Add(new IconToolbarItem
                    {
                        Text = "Users".Translate(),
                        IconImageSource = "md-people",
                        IconColor = Colors.White,
                        Command = ShowLoggedUsersCommand
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                Debug.WriteLine(ex.Message);
            }

            finally
            {
                App.SetLoading(false);
            }
        }
        #endregion
    }
}
