using I18N_L10N.Extensions;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Services.Loading;
using Plutus.Frontend.AppClient.ViewModels;
using Plutus.Frontend.AppClient.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient
{
    public partial class App : Application
    {
        private static bool IsLoading;
        internal static TranslateExtension TranslateExtension = null;
#pragma warning disable IDE1006 // Naming Styles
        private static App _app;
#pragma warning restore IDE1006 // Naming Styles

        public App()
        {
            // ⚠ FIRST LINE, on purpose. The crashes worth catching are the ones during start-up, and
            // a handler installed after them records nothing. Everything else in this app logs to an
            // OTLP endpoint and a console — neither of which exists when someone double-clicks the
            // exe in a shop and it disappears.
            Services.Analytics.CrashLog.Install();

            // ⚠⚠ SYNCFUSION IS GONE FROM THIS APP ENTIRELY (2026-08-20). There is no licence
            // registration here any more, no `ConfigureSyncfusionCore` in `MauiProgram`, and no
            // Syncfusion package reference in the csproj — all ten came out together with the two
            // legacy report screens and the Excel export they used.
            //
            // ⚠ THE ORDER MATTERED AND WAS FOLLOWED: screens → `ExcelHandling.cs` → packages →
            // registration. Removing the registration while a licensed control still existed in the
            // assembly would have turned a dormant screen into a **trial-dialog** screen — a modal with
            // no way back, on a shop floor, which is the worst failure this app can have. Deleting the
            // screens first is what discharged that hazard; the key had nothing left to license.
            //
            // ⚠⚠ DO NOT ADD A SYNCFUSION CONTROL BACK. Matt, 2026-08-10: *"I am not going to renew
            // Syncfusion, it seems like it can be replaced."* **No key is coming**, keys are
            // version-specific, and the failure is a modal on the shop floor rather than a build error.
            // The old key sat in this file out of date on purpose for exactly that reason; it is now
            // deleted along with everything that needed it. See `Build/To do/Shrink MAUI Build.md` §4.

            //Set culture for AppResources
            I18N_L10N.I18N_L10N.SetCulture();
            //Create transaleExtension for code behind translation
            TranslateExtension = new TranslateExtension();

            InitializeComponent();

            BindingContext = new AppViewModel();

            // ⚠ BEFORE ANYONE SIGNS IN, on purpose. A till sitting on its login screen after close
            // still holds the day's sales in its outbox, and they must drain whether or not anybody
            // is at the counter. Starting this only at sign-in would strand a full day's takings
            // overnight and until somebody happened to sign back in. It is idempotent, and it does
            // nothing at all on a till that has not been enrolled.
            // ⚠ THE ROSTER IS RE-READ ON EVERY BEAT, and a revoked operator is put out of the till
            // (2026-08-11, Matt: *"As part of the heartbeat, the re-read of permissions needs to
            // happen. If a user is disabled, the user needs immediately logging out"*). These two
            // delegates are how the cadence asks and tells without taking a dependency on the UI —
            // the same shape as `BasketIsOpen`.
            //
            // ⚠ Wired HERE, beside `Start()`, because the cadence runs before anybody signs in and
            // must keep running after they are signed out. Hanging them off a screen would mean the
            // check stops working the moment that screen is gone — which is exactly when it fires.
            Services.Sync.TillCadence.SignedInOperatorId =
                () => GetViewModel()?.SignedInOperator?.UserId;
            Services.Sync.TillCadence.OperatorRevoked = ForceSignOut;

            // ⚠⚠ AND WHEN THE PLATFORM REVOKES THE TILL ITSELF (WP4, step 21). Device tokens have no
            // server-side denylist, so a stolen till keeps selling for up to 12h unless the beat
            // asks and acts. Same door as an operator revocation — the till goes back to the login
            // screen, where `TillConnectionCheck` then refuses to let anybody in — but a DIFFERENT
            // message, because "your account is disabled" would send somebody hunting for a working
            // login on a till that is finished.
            Services.Sync.TillCadence.DeviceRevoked = ForceSignOut;

            Services.Sync.TillCadence.Start();

            // Where a till starts (2026-08-08, Matt: "When you have enrolled a till, what is the
            // point of seeing the Connect to Plutus tab? You should just get a log in screen").
            //
            // ⚠ ENROLMENT is the question now, not whether a local database exists. A
            // portal-provisioned till has a device credential and NO local database — under the old
            // test it was sent to first-run setup for ever, however many times it enrolled.
            //
            // Connect-to-Plutus stays reachable as a tab inside the shell, which is where it belongs
            // once the till is working: diagnostics, not a doorway.
            // ⚠⚠ THE CACHED THEME GOES ON BEFORE THE FIRST SCREEN (step 22 / FE10). Without this the
            // till paints the stock palette, then repaints in the shop's colours a moment later — the
            // flash the web till avoids by applying its cache in `main.tsx` before React mounts.
            // ⚠ Fire-and-forget and self-marshalling: colours must never delay a till starting, and a
            // theme that cannot be read is a till in the stock palette, not a till that will not open.
            _ = Services.Theming.Theming.ApplyCachedAsync();

            // ⚠⚠ ENROLMENT ALONE DECIDES NOW — L5, 2026-08-23. This used to be
            // `enrolled || hasLegacyDb`, sending a till that merely HAD a legacy database to the
            // login screen. That was right while the legacy local login existed; Matt removed it the
            // same day (*"a till needs to enrol and sync first"*), so such a till would now be shown
            // a sign-in screen that cannot sign anybody in — the worst of both, since first-run is
            // where it would actually enrol.
            //
            // ⚠ `Database.LocalDbExist()` was the last caller of the legacy DB helper, and the last
            // code in this app that touched `Database.db` at all.
            var enrolled = Services.Connectivity.SecureDeviceCredentialStore.IsEnrolled();

            MainPage = enrolled
                ? new LoginView()
                : new Views.FirstTimeStartUp.FTSUMainView();

            _app = this;
        }

        protected override void OnStart()
        {
            using var activity = Observability.ActivitySource.StartActivity("App.Start");

            IAppState appState = AppServices.Get<IAppState>();
            appState.Init();

            // ⚠ RE-LEARN WHERE THIS TILL IS, on every start, in the background.
            //
            // Two reasons, and the second is the one that bites. A till can be MOVED between stores
            // in the portal, and its prices, receipts and themes must follow it — placement is not
            // a fact you learn once at enrolment. And every till enrolled before 2026-08-09 was
            // never told its store or business at all, because enrolment bypassed EnrolmentFlow;
            // those tills SELF-HEAL here rather than needing a re-enrolment that would mint a
            // second device row for a machine that is already correctly paired.
            //
            // Background and swallowing its own failures: nothing about a placement refresh should
            // delay a sign-in screen or stop a shop trading.
            Services.Storage.TillPlacement.RefreshInBackground();
#if DEBUG
            appState.SetAppLogLevel(AppLogLevel.Verbose);
#else
            appState.SetAppLogLevel(AppLogLevel.Info);
#endif
        }

        protected override void OnSleep()
        {
            // Best-effort flush/shutdown - mobile apps can be killed without a clean shutdown, so
            // export batching intervals should stay short regardless of whether this ever runs.
            Observability.TracerProvider?.Dispose();
        }

        protected override void OnResume()
        {
            // Handle when your app resumes
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static AppViewModel GetViewModel()
        {
            return (AppViewModel)_app.BindingContext;
        }

        /// <summary>
        /// Put the signed-in operator out of the till, and tell them why.
        ///
        /// ⚠ Matt, 2026-08-11: *"If a user is disabled, the user needs immediately logging out with
        /// an information message saying 'Your account has been disabled, please speak to your
        /// manager'."* This is that. It is raised from the 60s heartbeat, so it fires within a
        /// minute of the change being made in the portal.
        ///
        /// ⚠ THE MESSAGE NAMES THE ACCOUNT, NOT THE TILL. "You have been logged out" sends somebody
        /// to reboot the machine; "your account has been disabled" sends them to the person who can
        /// actually help.
        ///
        /// ⚠ THE BASKET IS ABANDONED, and that is the correct trade — but it is a real cost, so it
        /// is stated rather than glossed. A revoked operator continuing to ring a sale is an
        /// unattributable sale; the goods are still on the counter and can be rung again by
        /// somebody who is allowed to. ⚠ Nothing already COMMITTED is touched: the outbox holds
        /// completed sales and keeps draining, because the money moved whether or not this person
        /// is still employed.
        ///
        /// ⚠ ON THE UI THREAD, and defensively: this arrives from a background timer, and an
        /// exception escaping here would take the app down from a thread with no handler.
        /// </summary>
        internal static void ForceSignOut(string message)
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    // ⚠ IDEMPOTENT. The beat fires every 60 seconds and the roster will keep
                    // saying the same thing until somebody else signs in — without this guard the
                    // operator would be shown the message again every minute, on top of a login
                    // screen they are already looking at.
                    if (GetViewModel()?.SignedInOperator is null) return;

                    GetViewModel().SignedInOperator = null;

                    // ⚠ The PLATFORM token goes too. It is a bearer token with no server-side
                    // denylist, so leaving it in memory would let a revoked operator's session keep
                    // reaching `perm:*` endpoints until it expired on its own.
                    Services.Connectivity.OperatorSession.Clear();

                    // ⚠ The login screen FIRST, then the message. A dialog raised over a page that
                    // is about to be replaced is the shape that produced the COMException which
                    // closed the till at the payment prompt.
                    var login = new LoginView();
                    _app.MainPage = login;

                    await login.DisplayAlert("Signed out", message, "OK");
                }
                catch (System.Exception ex)
                {
                    Services.Analytics.CrashLog.Write("App.ForceSignOut", ex);
                }
            });
        }

        #region Global Loading Methods
        /// <summary>
        /// Sets loading attribute directly and triggers the correct UI response
        /// </summary>
        /// <param name="isLoading">value to set loading indicator</param>
        internal protected static void SetLoading(bool isLoading)
        {
            if (isLoading == IsLoading)
                return;
            IsLoading = isLoading;
            TriggerLoadingUI();
        }

        /// <summary>
        /// Toggle loading attribute and trigger the correct UI response
        /// </summary>
        internal protected static void ToggleLoading()
        {
            IsLoading = !IsLoading;
            TriggerLoadingUI();
        }

        /// <summary>
        /// Select the correct UI response using the current loading attribute's value
        /// </summary>
        private protected static void TriggerLoadingUI()
        {
            if (IsLoading)
            {
                AppServices.Get<ILoadingViewService>().ShowLoadingPage();
            }
            else
            {
                GetViewModel().CurrentLoadingItem = "";
                AppServices.Get<ILoadingViewService>().HideLoadingPage();

            }
        }
        #endregion
    }
}
