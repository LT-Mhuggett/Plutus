using CustomViews.Structs;
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
        // ⚠ WP-T1 T1.3: `ThemeUnknown`, not grey — "not checked yet" is a real state, and it must
        // read on a dark scheme too. It is NOT a portal slot: a shop must not be able to paint
        // "revoked" the same colour as "connected".
        private Color _connectionColour =
            Helpers.Extensions.XAML.MaterialIconGlyphConverter.ThemeColour("ThemeUnknown", Colors.Gray);
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
                ? await Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(title, "Cancel", null, choices)
                : null;

        /// <summary>
        /// WP8 / step 24 — who works here, add somebody, set a password.
        ///
        /// ⚠⚠ THE FLOW MOVED TO `Services.People.StaffFlow` ON 2026-08-21, because it gained a second
        /// door. Matt, of MAUI's missing app bar: *"It is also missing the users etc."* Until then it
        /// was reachable ONLY from here — so a supervisor who was already signed in had to **sign out
        /// to add somebody**, which the web till has never required: it puts Users behind the 👥 in
        /// the app bar.
        ///
        /// ⚠ THIS DOOR STAYS, and it is not redundant: *"the person who needs it is standing at a till
        /// nobody can get into"*. Both doors, one flow — see `StaffFlow` for the rest of the history.
        ///
        /// ⚠ `async void` on a Command, so it must not let anything escape. `StaffFlow.ShowAsync`
        /// catches internally; this catches again, because only one of them is a guarantee.
        /// </summary>
        private async void ExecuteShowLoggedUsers()
        {
            try
            {
                await Services.People.StaffFlow.ShowAsync();
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("Login.Users", ex);
            }
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

                // ⚠ The verifier store is passed — step 28's server half. See `OperatorSync`.
                var count = await new Plutus.Client.Core.OperatorSync(
                    api,
                    new Services.Connectivity.DbOperatorStore(),
                    new Services.Connectivity.DbDeviceVerifierStore()).RefreshAsync(till);

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

        /// <summary>
        /// Ask the PLATFORM whether this password is right — step 28.
        /// </summary>
        /// <returns>
        /// ⚠⚠ `true` yes, `false` no, and **`null` COULD NOT ASK**. The three are not
        /// interchangeable: `false` is a wrong password, `null` routes to *connect once*. Returning
        /// `false` for an unreachable server would tell an operator with a perfectly good password
        /// that it is wrong, every time the shop's broadband hiccups — and mint nothing, so it would
        /// never recover on its own.
        /// </returns>
        /// <remarks>
        /// ⚠ THIS IS THE SAME CALL `TryFetchOperatorTokenInBackground` MAKES, and that is deliberate
        /// rather than duplication: one is fire-and-forget for the reports tab, this one decides
        /// whether somebody gets in. Sharing the endpoint means there is one definition of "the
        /// platform accepted this password". ⚠ If they ever diverge, this is the one that matters.
        ///
        /// ⚠ A THROW IS `null`, NOT `false` — a DNS failure, a timeout and a TLS error are all
        /// "could not ask".
        /// </remarks>
        private static async Task<bool?> VerifyPasswordOnlineAsync(
            string emailOrId, string password, System.Threading.CancellationToken ct)
        {
            try
            {
                var http = Services.Connectivity.PlutusHttp.TryFor(new Settings().ServerUrlSetting);
                if (http is null) return null;

                var (status, session) = await new Plutus.Client.Core.PlutusApiClient(http)
                    .LoginAsync(emailOrId, password, ct);

                // ⚠⚠ THE STATUS DECIDES, NOT THE NULL SESSION — `LoginAsync`'s own header says so:
                // *"Returns null for a wrong password, a deactivated account, a suspended tenant, or
                // no network — the CALLER must not treat those alike, so it also hands back the
                // status."* Reading only the session would collapse "unreachable" into "wrong
                // password", which is the exact conflation step 28 exists to avoid.
                //
                // ⚠ 401 IS THE ONLY DEFINITE NO. A deactivated account and a suspended tenant also
                // answer 401, and refusing those is correct: this till must not mint a verifier for
                // somebody the platform has just turned off.
                if (status == System.Net.HttpStatusCode.Unauthorized) return false;

                // ⚠ A session is the only definite yes.
                if (session is not null) return true;

                // ⚠⚠ EVERYTHING ELSE IS "COULD NOT ASK" — 503 from the catch above, a 5xx, a proxy
                // page, a 200 with a body this build cannot read. None of them is evidence about the
                // password, and treating them as one would lock out a shop whose server is having a
                // bad minute.
                return null;
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("LoginViewModel.VerifyPasswordOnline", ex);
                return null;
            }
        }

        private async Task<bool> TrySignInFromRosterAsync()
        {
            // ⚠⚠ STEP 28 — ONLINE-FIRST. Three things are handed to `OperatorLogin` that were not
            // before, and each is load-bearing:
            //
            //   · the DEVICE VERIFIER STORE, so a password proved online once works offline for ever
            //     after — without ever holding this shop's PLATFORM credentials to do it;
            //   · an ONLINE CHECK, so an account this till has never seen can still sign in when the
            //     line is up, and mint its verifier while it does;
            //   · nothing else. The roster, the horizons and the grants are untouched.
            //
            // ⚠ `OfflineCredentials`' header is the reason: *"A stolen till holds hashes at
            // PBKDF2-SHA1/101,010 … those are the operators' PLATFORM passwords, and they work on
            // the web till too."* This is what stops a till accumulating them for staff who have
            // never used it.
            var login = new Plutus.Client.Core.OperatorLogin(
                new Services.Connectivity.DbOperatorStore(),
                verifiers: new Services.Connectivity.DbDeviceVerifierStore(),
                verifyOnline: VerifyPasswordOnlineAsync);

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

            // ⚠⚠ `EnsureStoreAsync` IS GONE — cutover step 21, 2026-08-23.
            //
            // It existed to write a legacy `StoreModel` row so `App.GetViewModel().Store` was not
            // null, because `StoreInformationViewModel` once dereferenced it IN ITS CONSTRUCTOR —
            // which `AppShell` runs while being built, so a correct password threw and the operator
            // was told "Something went wrong signing in".
            //
            // ⚠ THAT REASON EXPIRED ON 2026-08-17, when that constructor was made null-safe and the
            // screen stopped reading the local store at all (the platform is the source of truth for
            // store details since step 20). The comment here went on describing the old crash for six
            // days, which is why this note says what changed rather than just deleting the call.
            //
            // ⚠ Every remaining reader of `.Store` handles null: `Database.cs` guards it,
            // `ReceiptReprint` coalesces it, and the two printing paths take it as an argument and
            // already cope — a missing store must never lose a receipt.

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


        /// <summary>Runs the shared probe and paints the result. Swallows everything — a broken
        /// connection check must never be the reason nobody can sign in.</summary>
        private async Task RefreshConnectionAsync()
        {
            if (IsCheckingConnection) return;
            IsCheckingConnection = true;
            ConnectionSummary = "Checking connection…";
            ConnectionDetail = null;
            ConnectionColour = Helpers.Extensions.XAML.MaterialIconGlyphConverter.ThemeColour("ThemeUnknown", Colors.Gray);
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
                ConnectionColour = Helpers.Extensions.XAML.MaterialIconGlyphConverter.ThemeColour("ThemeUnknown", Colors.Gray);
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

                // ⚠ COUNT THE STAFF, don't test for the file. `LocalDbExist()` asks "does a file
                // exist" when the question is "is there anybody to sign in as" — and the Database
                // constructor CREATES and migrates an empty one on first touch. So a single earlier
                // attempt made the file exist, this guard stopped firing, and the honest message
                // reverted to "details not correct" on the very next try. The guard was defeating
                // itself after one use.
                // ⚠⚠ THE LEGACY LOCAL LOGIN IS GONE — Matt, 2026-08-23: *"A till should not
                // authenticate against the legacy DB... a till needs to enrol and sync first."*
                //
                // It signed people in against `EmployeeModel` in the legacy SQLite file, and it fired
                // on exactly ONE condition: `LoginFailure.NoOperators` — a till with no roster at all.
                // Every other outcome, a wrong password included, was answered above and never
                // reached it.
                //
                // ⚠ SO THE ONE BEHAVIOUR REMOVED IS "sign in on a till that has never synced", and
                // that is the point. Those hashes are PBKDF2-**SHA1**/101,010 — which
                // `OfflineCredentials` calls roughly 13x below current OWASP guidance and cannot
                // raise, because the legacy till shares the format — and a never-synced till is also
                // the one a thief has. Step 28 exists to shrink exactly this.
                //
                // ⚠ The four messages below already said the right thing in every case; they were
                // simply gated behind a local-staff count. They are now the whole answer.
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
