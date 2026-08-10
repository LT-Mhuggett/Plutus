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

            // ⚠ THE SYNCFUSION LICENCE KEY, AND IT IS OUT OF DATE ON PURPOSE. Matt, 2026-08-10:
            // *"I am not going to renew Syncfusion, it seems like it can be replaced."* Keys are
            // version-specific and this one predates the 34.1.32 packages, so any licensed control
            // that renders puts a modal in front of the page — which is how `SalesReportsView`
            // became a screen with no way out.
            //
            // ⚠ AS OF THIS COMMIT NO SYNCFUSION CONTROL IS ON ANY SCREEN AN OPERATOR CAN REACH.
            // The quantity box, the alterations picker, the item list and the discount multi-select
            // are plain MAUI. What is left is the two HIDDEN legacy report screens and the XlsIO
            // export they use — see `Build/legacy-removal.md` (L4) and `syncfusion-footprint.md`.
            // The registration stays only until those go, because removing it while a licensed
            // control still exists in the assembly is worse, not better: it turns a dormant screen
            // into a trial-dialog screen.
            //
            // ⚠ DO NOT ADD A SYNCFUSION CONTROL TO A LIVE SCREEN. There is no key that will license
            // it, and the failure is a modal on the shop floor, not a build error.
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
                "NDg3MzQ2QDMxMzkyZTMyMmUzMGxFd1VHR3l1ekdldEJSbjQyQ2NRTHhyakorOVZ6cmF6NSszNkNPTmtJNEk9");

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
            var enrolled = Services.Connectivity.SecureDeviceCredentialStore.IsEnrolled();
            var hasLegacyDb = ((AppViewModel)BindingContext).DatabaseProviderSetting != null
                              && Helpers.Database.Database.LocalDbExist();

            MainPage = enrolled || hasLegacyDb
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
