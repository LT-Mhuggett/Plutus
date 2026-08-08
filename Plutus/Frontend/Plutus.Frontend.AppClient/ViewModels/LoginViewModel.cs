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

        private string _connectionSummary;
        private string _connectionDetail;
        private Color _connectionColour = Colors.Gray;
        private bool _isCheckingConnection;

        #endregion

        #region Public Properties

        /// <summary>
        /// What the operator reads: "Connected to Plutus", or which of the three faults it is.
        ///
        /// ⚠ WP16a. "Offline" is three problems wearing one word, and the person standing at the
        /// till is the one who has to act on the difference — check the cable, ring support, or ask
        /// a manager to re-enrol the device. A single red badge sends shops to reboot routers over
        /// a portal setting.
        /// </summary>
        public string ConnectionSummary
        {
            get => _connectionSummary;
            private set => SetProperty(ref _connectionSummary, value);
        }

        /// <summary>The diagnostic line — status code, exception, server version. For whoever the
        /// operator rings, not for the sales floor.</summary>
        public string ConnectionDetail
        {
            get => _connectionDetail;
            private set => SetProperty(ref _connectionDetail, value);
        }

        public Color ConnectionColour
        {
            get => _connectionColour;
            private set => SetProperty(ref _connectionColour, value);
        }

        public bool IsCheckingConnection
        {
            get => _isCheckingConnection;
            private set => SetProperty(ref _isCheckingConnection, value);
        }

        /// <summary>
        /// Which build of THIS till this is — <c>versions/till-maui.txt</c>.
        ///
        /// ⚠ Its own number, not shared with the web till: they deploy separately and will diverge
        /// the first time a Windows printer or drawer problem is fixed here and nowhere else.
        ///
        /// ⚠ On the LOGIN screen deliberately, not buried in Settings. It is the first thing anyone
        /// sees, which is what makes it usable in the sentence "I'm testing 1.0.0 and it does X" —
        /// the thing a build timestamp could never do, because two builds ten minutes apart are
        /// indistinguishable in a bug report and nobody reads an ISO timestamp down a phone.
        ///
        /// The server's own version arrives on the connection probe and is shown beneath it, so a
        /// version mismatch is visible at the moment it starts mattering.
        /// </summary>
        public string TillVersionText { get; } =
            $"MAUI till v{Plutus.SharedKernel.PlutusVersion.Of(typeof(App).Assembly)}";
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
        private async void ExecuteShowLoggedUsers()
        {
            // BugFix plan, Bug 1 stopgap: this was `throw new NotImplementedException()`
            // on a synchronous command with no try/catch — tapping the people icon
            // crashed the app. Show a friendly notice until the users page is built.
            try
            {
                await App.Current.MainPage.DisplayAlert(
                    "Users", "User management is not available in this version yet.", "OK");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            //Implement Show Logged Users Page
        }
        #endregion

        #region Refresh connection
        Command _refreshConnectionCommand;

        /// <summary>Tapping the indicator re-checks. Cheap, and it is the first thing anyone does
        /// after plugging the cable back in.</summary>
        public Command RefreshConnectionCommand =>
            _refreshConnectionCommand ??= new Command(async () => await RefreshConnectionAsync());
        #endregion

        public LoginViewModel()
        {
            Title = "Login";
            Email_UserId_Placeholder = string.Format("{0}/{1} {2}", "EMail".Translate(), "User".Translate(), "Id".Translate());
            App.SetLoading(false);

            // ⚠ Fire and forget, deliberately. Sign-in must NEVER wait on this: the login screen is
            // exactly where an offline till has to keep working, and a shop with a dead router still
            // has to trade. The indicator fills itself in a moment later.
            _ = RefreshConnectionAsync();
        }

        /// <summary>
        /// WP8 — sign in against the roster synced from the portal, offline.
        /// </summary>
        /// <returns>True when this path handled the attempt (signed in, or told the operator why
        /// not). False means "no roster on this till", so the legacy local path should try.</returns>
        private async Task<bool> TrySignInFromRosterAsync()
        {
            var login = new Plutus.Client.Core.OperatorLogin(new Services.Connectivity.FileOperatorStore());
            var result = await login.SignInAsync(_email_UserId ?? string.Empty, _password ?? string.Empty);

            // No roster at all → let the legacy local database have a go. Every other failure is a
            // real answer about a real account and must NOT fall through to a second, differently
            // worded rejection.
            if (result.Failure == Plutus.Client.Core.LoginFailure.NoOperators) return false;

            if (!result.Succeeded)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Login",
                    new Dictionary<string, string> { { "Authorised", "False" }, { "Reason", result.Failure.ToString() } });
                await App.Current.MainPage.DisplayAlert("Can't sign in", result.Message, "OK");
                return true;
            }

            var op = result.Operator!;
            Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Login",
                new Dictionary<string, string> { { "Authorised", "True" }, { "Trust", op.Trust.ToString() } });

            // ⚠ Warn, never block. Past the money-out horizon this till still sells — a shop that
            // cannot trade is worse than a stale roster, because it falls back to a cash tin and
            // produces no attributable records at all.
            if (op.Warn && !string.IsNullOrEmpty(op.Message))
                await App.Current.MainPage.DisplayAlert("Signed in", op.Message, "OK");

            App.GetViewModel().SignedInOperator = op;
            App.Current.MainPage = new AppShell();
            return true;
        }

        /// <summary>Runs the shared probe and paints the result. Swallows everything — a broken
        /// connection check must never be the reason nobody can sign in.</summary>
        private async Task RefreshConnectionAsync()
        {
            if (IsCheckingConnection) return;
            IsCheckingConnection = true;
            ConnectionSummary = "Checking connection…";
            ConnectionDetail = null;
            ConnectionColour = Colors.Gray;
            try
            {
                var status = await Services.Connectivity.TillConnectionCheck.CheckAsync();
                ConnectionSummary = status.Summary;
                ConnectionColour = Services.Connectivity.TillConnectionCheck.ColourFor(status);
                ConnectionDetail = status.ClockSuspect
                    // Worth saying out loud: device tokens expire and VAT bands are effective-dated,
                    // so a till an hour out can have a day's takings judged against a different
                    // instant — and nothing else in the app would ever mention it.
                    ? $"⚠ This till's clock is out by {status.ClockSkew:hh\\:mm\\:ss}. {status.Detail}".Trim()
                    : status.Detail;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                ConnectionSummary = "Couldn't check the connection.";
                ConnectionColour = Colors.Gray;
            }
            finally
            {
                IsCheckingConnection = false;
            }
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
                // WP8: the PORTAL roster first. A portal-provisioned till has no local database at
                // all — its staff arrive from GET /api/v1/tills/{id}/operators and are verified here
                // against the synced PBKDF2 hash, with no network needed.
                //
                // ⚠ The legacy local path below is the fallback, not the primary, and it only still
                // exists because a till set up the old way must keep working until WP2's cutover.
                if (await TrySignInFromRosterAsync()) return;

                // ⚠ DatabaseProvider.Sqlite is 0, so a NULL setting parses to "local SQLite" rather
                // than failing — which is exactly how "no accounts at all" came out as "details not
                // correct", sending someone hunting for a typo that did not exist.
                Enum.TryParse(DatabaseProviderSetting, out DatabaseProvider databaseProvider);

                // ⚠ COUNT THE STAFF, don't test for the file. `LocalDbExist()` asks "does a file
                // exist" when the question is "is there anybody to sign in as" — and the Database
                // constructor CREATES and migrates an empty one on first touch. So a single earlier
                // attempt made the file exist, this guard stopped firing, and the honest message
                // reverted to "details not correct" on the very next try. The guard was defeating
                // itself after one use.
                int localStaff;
                using (var probe = new Helpers.Database.Database(databaseProvider))
                    localStaff = probe.Get<EmployeeModel>().Count();

                if (localStaff == 0)
                {
                    await App.Current.MainPage.DisplayAlert(
                        "No staff on this till yet",
                        "This till has no staff accounts on it.\n\n" +
                        "Open the Plutus tab and use “Sync staff from the portal” to fetch them.",
                        "OK");
                    return;
                }

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
