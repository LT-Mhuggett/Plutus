using CustomViews.Structs;
using Database.Enums;
using Database.Models;
using Microsoft.EntityFrameworkCore;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Validators;
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
        /// <summary>
        /// ⚠⚠ EVERY DIALOG ON THIS FLOW GOES THROUGH HERE. `App.Current.MainPage` is null whenever
        /// there is no MAUI host — which includes the test that guards this very command against the
        /// crash it used to cause — so an unguarded `DisplayAlert` throws from inside the `catch`
        /// that was supposed to contain the failure. That is how the original people-icon crash
        /// worked, and it would have come back by a different route.
        /// </summary>
        private static async Task SayAsync(string title, string message)
        {
            if (App.Current?.MainPage is Page page)
                await page.DisplayAlert(title, message, "OK");
        }

        /// <summary>⚠ Same guard as <see cref="SayAsync"/>. Null means "nobody chose anything",
        /// which every caller already handles.</summary>
        private static async Task<string> AskAsync(string title, params string[] choices) =>
            App.Current?.MainPage is Page page
                ? await page.DisplayActionSheet(title, "Cancel", null, choices)
                : null;

        /// <summary>
        /// WP8 / step 24 — who works here, add somebody, set a password.
        ///
        /// ⚠ ON THE LOGIN SCREEN, reached by the people icon, which is where the web till puts it
        /// too ("Users is reached via the people button"). It is also the only place it is any use:
        /// the person who needs it is standing at a till nobody can get into.
        ///
        /// ⚠ Until 1.70.0 this said *"User management is not available in this version yet"* — and
        /// before that it threw `NotImplementedException` on a synchronous command and closed the
        /// app. The empty `ExecuteAddEmployee`/`ExecuteViewAllEmployees` pair in
        /// `StoreOptionsViewModel` is dead scaffolding behind a commented-out menu, and its command
        /// property is misspelt `AddEmployeeCommmand` against a binding to `AddEmployeeCommand` — so
        /// it would have done nothing even if the menu were restored. Marked for the removal sweep.
        ///
        /// ⚠⚠ NO `Modal.ShowAsync` WRAPPER — `InputAlertHelper` gates internally, and the redundant
        /// outer guard is what deadlocked the checkout on 2026-08-13.
        /// </summary>
        private async void ExecuteShowLoggedUsers()
        {
            try
            {
                var (staff, problem) = await Services.People.StaffDirectory.LoadAsync();

                if (staff is null)
                {
                    await SayAsync("Users", problem ?? "Plutus can't be reached from this till right now.");
                    return;
                }

                const string addSomebody = "Add somebody";
                var choices = new List<string> { addSomebody };
                choices.AddRange(staff.Select(Services.People.StaffDirectory.StaffLine));

                // ⚠ The cap is SAID, not swallowed — the legacy list is ordered by CreatedAt, so it
                // is the newest staff who fall off, which is exactly who this screen is opened for.
                var warning = Services.People.StaffDirectory.TruncationWarning(staff.Count);
                if (warning is not null) await SayAsync("Users", warning);

                var picked = await AskAsync("Users", choices.ToArray());

                if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel") return;

                if (picked == addSomebody) { await AddSomebodyAsync(); return; }

                // ⚠ BY INDEX. Two people can share a name and an email is not guaranteed unique on
                // the legacy table, so matching the label back would set the wrong person's password.
                var index = choices.IndexOf(picked) - 1;
                if (index >= 0 && index < staff.Count) await SetPasswordAsync(staff[index]);
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("Login.Users", ex);
                await SayAsync("Users", "That didn't work. Nothing has been changed.");
            }
        }

        /// <summary>
        /// ⚠ THE PASSWORD IS ASKED FOR HERE, in the same flow. A person created without one cannot
        /// sign in anywhere — see `StaffDirectory.CreateAsync`.
        /// </summary>
        private async Task AddSomebodyAsync()
        {
            var required = new IValidator[] { new RequiredValidator() };

            var fields = new ViewElementData[]
            {
                new(1, "First name", "", required, false, true),
                new(2, "Last name", "", required, false, true),
                // ⚠ This is what they SIGN IN WITH, and what SetPassword matches on.
                new(3, "Email", "", required, false, true),
                new(4, "Mobile (optional)", "", null, false, true),
                // ⚠ Masked, and asked twice — a mistyped password on a new starter's account is
                // indistinguishable from "the till is broken" on their first shift.
                new(5, "Password", "", required, true, true),
                new(6, "Password again", "", required, true, true),
            };

            var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                fields, "Add", true, "Add somebody", "Cancel");

            if (answers is null) return;

            answers.TryGetValue(1, out string first);
            answers.TryGetValue(2, out string last);
            answers.TryGetValue(3, out string email);
            answers.TryGetValue(4, out string mobile);
            answers.TryGetValue(5, out string password);
            answers.TryGetValue(6, out string again);

            if (!Services.People.StaffDirectory.CanCreate(first, last, email))
            {
                await SayAsync("Users", "A first name, a last name and an email address are all needed — the email is "
                    + "what they sign in with.");
                return;
            }

            if (Services.People.StaffDirectory.PasswordProblem(password, again) is string bad)
            {
                await SayAsync("Users", bad);
                return;
            }

            var (ok, problem) = await Services.People.StaffDirectory.CreateAsync(
                first, last, email, mobile, password);

            await SayAsync("Users", ok ? $"{first.Trim()} can now sign in on any till." : problem);
        }

        /// <summary>Set an existing person's password. ⚠ It takes effect at their NEXT sign-in.</summary>
        private async Task SetPasswordAsync(Plutus.Contracts.Client.EmployeeDto who)
        {
            var required = new IValidator[] { new RequiredValidator() };

            var fields = new ViewElementData[]
            {
                new(1, "New password", "", required, true, true),
                new(2, "New password again", "", required, true, true),
            };

            var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                fields, "Set password", true, who.DisplayName, "Cancel");

            if (answers is null) return;

            answers.TryGetValue(1, out string password);
            answers.TryGetValue(2, out string again);

            if (Services.People.StaffDirectory.PasswordProblem(password, again) is string bad)
            {
                await SayAsync("Users", bad);
                return;
            }

            var (ok, problem) = await Services.People.StaffDirectory.SetPasswordAsync(who, password);

            // ⚠ "NEXT time they sign in" is not padding. Their session is a bearer token with no
            // denylist, so somebody already signed in on another till stays signed in — and a
            // manager who expected otherwise would think the change had not saved.
            await SayAsync("Users", ok
                ? $"Done. {who.DisplayName} uses the new password next time they sign in."
                : problem);
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
        /// <summary>What one automatic roster fetch achieved.</summary>
        /// <param name="Enrolled">False = this till has never been paired, so there is nothing to
        /// fetch from and no amount of retrying will help.</param>
        /// <param name="Count">Operators the till now holds. ⚠ NULL means the server could not be
        /// reached — which is a different problem from a confirmed zero, and the two need different
        /// sentences in front of an operator.</param>
        private readonly record struct RosterRefresh(bool Enrolled, int? Count);

        /// <summary>
        /// Pull this till's staff list, so an empty roster fixes itself instead of becoming an
        /// instruction. Never throws — sign-in must not fail because a refresh did.
        /// </summary>
        private static async Task<RosterRefresh> TryFillRosterAsync()
        {
            try
            {
                var credentials = await Services.Connectivity.SecureDeviceCredentialStore.LoadAsync();

                // ⚠ NO DEVICE IDENTITY is the only state that genuinely means "not paired".
                if (credentials?.DeviceId is not Guid) return new RosterRefresh(false, null);

                var api = await Services.Storage.TillPlacement.TryCreateApiAsync();
                if (api is null) return new RosterRefresh(true, null);

                // ⚠ ENROLLED, BUT DOESN'T KNOW WHICH TILL IT IS — real, and it stranded the first
                // machine to meet it. TillPlacement owns that resolution now (Meta, then the
                // legacy Preferences value, then the server), so this is no longer one of three
                // copies of the same recovery block.
                //
                // Still unknown → the server could not be reached, or it is too old to answer.
                // Either way that is "try again", not "you are not enrolled".
                if (await Services.Storage.TillPlacement.TillIdAsync(api) is not Guid till)
                    return new RosterRefresh(true, null);

                var count = await new Plutus.Client.Core.OperatorSync(
                    api, new Services.Connectivity.FileOperatorStore()).RefreshAsync(till);

                return new RosterRefresh(true, count);
            }
            catch (Exception ex)
            {
                // A failed refresh is not a failed login — the legacy path below still gets its go.
                CrashLog.Write("LoginViewModel.TryFillRosterAsync", ex);
                return new RosterRefresh(true, null);
            }
        }

        /// <summary>
        /// Get this operator a platform token, in the background, without blocking sign-in
        /// (cutover step 19).
        ///
        /// ⚠ Same endpoint as the web till — `POST /api/Auth/Login` — because it has been carrying
        /// that till's sessions for months, which is the evidence it works. A parallel v2 endpoint
        /// would be a second door onto the same lock, and the two would drift.
        ///
        /// ⚠ Every failure is swallowed. Offline, wrong tenant status, deactivated on the platform
        /// but still on this till's roster — none of them may stop somebody serving a customer. The
        /// roster already decided whether they may sign in; this only decides whether the reports
        /// tab has anything to show.
        /// </summary>
        private static void TryFetchOperatorTokenInBackground(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var http = Services.Connectivity.PlutusHttp.TryFor(new Settings().ServerUrlSetting);
                    if (http is null) return;

                    var (_, session) = await new Plutus.Client.Core.PlutusApiClient(http).LoginAsync(email, password);
                    if (session is not null)
                        Services.Connectivity.OperatorSession.Set(session);
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("LoginViewModel.TryFetchOperatorToken", ex);
                }
            });
        }

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

            // ⚠ AND, WHEN ONLINE, A PLATFORM TOKEN FOR THIS PERSON (step 19, binding default 11).
            // The roster is what AUTHENTICATES — offline, against synced PBKDF2 hashes, and that
            // does not change. This is a different need: the till's `perm:*` routes (its own sales
            // list, cash events, reports) are gated on what the OPERATOR may see, and a device
            // token cannot answer that — it identifies the machine, not the person. Without it the
            // till 403s on its own takings.
            //
            // ⚠ Fire-and-forget, and failure is SILENT: a till with no network must still open. The
            // consequence is that report screens are unavailable offline, which is honest — they
            // are server-rendered anyway.
            TryFetchOperatorTokenInBackground(_email_UserId, _password);

            // ⚠ THE SHELL NEEDS A STORE, and this path never gave it one. AppShell builds
            // StoreOptionsView, whose viewmodel dereferences App.GetViewModel().Store in its
            // CONSTRUCTOR — so a correct password threw NullReferenceException while the shell was
            // being assembled, and the operator was shown "Something went wrong signing in".
            //
            // From a shop floor that is indistinguishable from "my password is wrong", which is the
            // worst possible way for it to read: sign-in had actually SUCCEEDED. The legacy path
            // below never hit it because it sets Store from the employee's own row.
            await EnsureStoreAsync();

            // WP5's missing half. ⚠ IN THE BACKGROUND, DELIBERATELY: a first sync pages a whole
            // catalogue, and nobody should stand at a counter watching it before they can serve a
            // customer. The till sells what it already has while the rest arrives.
            Services.Storage.CatalogueSyncService.SyncInBackground();

            // ⚠ The till's one background clock — heartbeat, outbox drain, catalogue, notices, every
            // 60s. Until this call existed every sale this till committed sat in the outbox for
            // ever: `OutboxPusher.DrainAsync` was referenced nowhere in the app, so the sale reached
            // no report, no VAT return and no other till. Idempotent, so signing in again is safe.
            Services.Sync.TillCadence.Start();

            App.Current.MainPage = new AppShell();
            return true;
        }

        /// <summary>
        /// Make sure the app has a store to render, for a till whose staff came from the portal.
        ///
        /// ⚠ A PORTAL-PROVISIONED TILL HAS AN EMPTY LOCAL DATABASE. The `Database` constructor
        /// creates and migrates one on first touch, so the tables exist and every one of them is
        /// empty — including `Stores`. Nothing local can supply this, so it comes from the server:
        /// the device knows its till, `tills/{id}/name` gives the store, `stores/{id}/info` gives
        /// the detail.
        ///
        /// Persisted locally as well as held in memory, because the Store Options screen edits the
        /// row and saves it — an in-memory-only store would look right and fail on the first edit.
        /// Never throws: a till that cannot reach the server still signs in, and the null-guards on
        /// the screens themselves keep the shell standing.
        ///
        /// ⚠ CUTOVER STEP 21 SAYS DELETE THIS, AND IT CANNOT GO YET — recorded here rather than
        /// left as a silent deviation. The reasoning in the plan is right: writing API data into
        /// the legacy `Stores` table is exactly the bridge binding default 9 forbids, and step 20's
        /// `StoreInfoCache` has already replaced it for DISPLAY (Store Options is read-only now and
        /// no longer edits this row, so the sentence above is already out of date).
        ///
        /// What still holds it up is `Store.Id`, not the store's details:
        ///   • `Helpers/Database/Database.cs:43` passes it to the legacy `AppDBContext` — null-safe,
        ///     so this one degrades rather than breaks.
        ///   • `Inventory/Items/AddEditViewModel.cs:330` dereferences `Store.Id` outright and would
        ///     NullReference the moment anyone edited an item.
        ///   • ⚠ `Inventory/Items/ViewAllViewModel.cs:1324` does the SAME, on the stock-adjust path
        ///     — added 2026-08-14. This comment previously named only `AddEditViewModel`, which
        ///     would have let someone delete this method, test the edit screen, and ship a crash on
        ///     the other one. **Two dereferences, not one.**
        ///
        /// Deleting it today would trade a design smell for a crash on screens operators use.
        /// It goes when **step 25** moves inventory off the legacy store — at which point nothing
        /// needs a legacy store id and this method has no remaining callers.
        ///
        /// ⚠ Do NOT unblock this by null-coalescing those two to `0`: that writes stock rows against
        /// store 0, which is a silent data change wearing a null-fix disguise.
        /// </summary>
        private static async Task EnsureStoreAsync()
        {
            try
            {
                if (App.GetViewModel().Store != null) return;

                Enum.TryParse(new Settings().DatabaseProviderSetting, out DatabaseProvider provider);

                using (var db = new Helpers.Database.Database(provider))
                {
                    var existing = db.Get<StoreModel>().FirstOrDefault();
                    if (existing != null) { App.GetViewModel().Store = existing; return; }
                }

                var api = await Services.Storage.TillPlacement.TryCreateApiAsync();
                if (api is null) return;

                // Placement first, so a till that has never learned its store learns it now; the
                // answer is then read from Meta rather than re-derived here.
                await Services.Storage.TillPlacement.RefreshAsync(api);
                if (await Services.Storage.TillPlacement.StoreIdAsync() is not int storeId) return;

                var info = await api.GetStoreInfoAsync(storeId);
                if (info is null) return;

                var store = new StoreModel
                {
                    StoreName = info.Name ?? info.BusinessName ?? "Store",
                    // ⚠ The abbreviation prints on receipts, so it must never be empty.
                    StoreAbbr = (info.Name ?? info.BusinessName ?? "ST").Trim(),
                    VatIN = info.VatNumber ?? string.Empty,
                    ContactNumber = string.Empty,
                    AdLine1 = info.AdLine1 ?? string.Empty,
                    AdLine2 = info.AdLine2 ?? string.Empty,
                    City = info.City ?? string.Empty,
                    PostCode = info.PostCode ?? string.Empty,
                    Country = info.Country ?? string.Empty,
                };

                using (var db = new Helpers.Database.Database(provider))
                {
                    db.Add(store);
                    db.Save();
                    App.GetViewModel().Store = db.Get<StoreModel>().FirstOrDefault() ?? store;
                }
            }
            catch (Exception ex)
            {
                // ⚠ Never block sign-in. A missing store degrades the Store Options screen; a
                // thrown exception here would put the operator back where this whole bug started.
                CrashLog.Write("LoginViewModel.EnsureStoreAsync", ex);
            }
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

        /// <summary>
        /// ⚠ WAS INVERTED — it returned true only when BOTH fields were EMPTY.
        ///
        /// It has never broken sign-in only by accident: this is <c>LoginCommand</c>'s
        /// <c>canExecute</c>, and <c>ChangeCanExecute()</c> is never called anywhere in the class,
        /// so it is evaluated once at bind time (both fields empty → true) and never re-checked.
        /// The button is really gated by the XAML validation group.
        ///
        /// A landmine: the first person to add a <c>ChangeCanExecute()</c> call — the obvious thing
        /// to do when wiring up a "disable while busy" — would permanently disable the login button
        /// on every till, and the cause would be nowhere near the change.
        /// </summary>
        public bool CanLogin() =>
            !string.IsNullOrEmpty(_password) && !string.IsNullOrEmpty(_email_UserId);

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

                // ⚠ THE ROSTER WAS EMPTY — so FETCH IT, rather than sending someone to a settings
                // tab. A freshly enrolled till has nobody on it until something asks, and the first
                // person to meet that was told to "open the Plutus tab and use Sync staff from the
                // portal" and then dropped back on the login screen. That is a shop floor being
                // asked to know how the software is wired.
                //
                // Only fires when there is no roster at all: a WRONG PASSWORD returns from
                // TrySignInFromRosterAsync above and never reaches here, so this cannot become a
                // network round trip on every mistyped login.
                var refresh = await TryFillRosterAsync();
                if (refresh.Count is > 0 && await TrySignInFromRosterAsync()) return;

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
                    // ⚠ SAY WHICH OF THE FOUR THINGS WENT WRONG. "No staff on this till" was true
                    // in every case and useful in none — it sent someone to press a button that,
                    // depending on the cause, either was not needed, could not work, or had already
                    // been tried automatically a second earlier.
                    var (title, message) = refresh switch
                    {
                        { Enrolled: false } => ("This till isn't connected yet",
                            "It hasn't been paired with Plutus, so there are no staff accounts on it.\n\n" +
                            "Open the Plutus tab and enter an enrolment code from the portal."),

                        { Count: 0 } => ("Nobody is assigned to this till",
                            "This till is connected, but the portal has no staff who can use it.\n\n" +
                            "In the portal, give someone a role that includes till permissions — " +
                            "then sign in here again."),

                        { Count: null } => ("Can't reach Plutus",
                            "This till has no staff accounts yet and the staff list couldn't be " +
                            "downloaded.\n\n" +
                            "Check the connection on the Plutus tab, then try again."),

                        _ => ("Can't sign in",
                            "That account isn't on this till's staff list. " +
                            "Check the email address, or ask a manager to check the portal."),
                    };
                    await App.Current.MainPage.DisplayAlert(title, message, "OK");
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
                // ⚠ THIS USED TO VANISH. Logger.LogError goes to OpenTelemetry and Debug.WriteLine
                // goes nowhere on a real till — so ANY fault in here (a locked database, a null
                // field, a migration failure) left the operator tapping Submit while the screen
                // sat still, with no record on the machine to send anyone. The crash log is the
                // file someone can actually attach to an email.
                Services.Analytics.CrashLog.Write("LoginViewModel.ExecuteLoginCommand", ex);
                Logger.LogError(ex);
                Debug.WriteLine(ex.Message);

                await App.Current.MainPage.DisplayAlert(
                    "Couldn't sign in",
                    "Something went wrong signing in. The details are in this till's log file — see the Plutus tab.",
                    "OK");
            }

            finally
            {
                App.SetLoading(false);
            }
        }
        #endregion
    }
}
