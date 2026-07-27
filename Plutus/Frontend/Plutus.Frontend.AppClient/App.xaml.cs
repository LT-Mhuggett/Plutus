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
            //Register the syncfusion license
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
                "NDg3MzQ2QDMxMzkyZTMyMmUzMGxFd1VHR3l1ekdldEJSbjQyQ2NRTHhyakorOVZ6cmF6NSszNkNPTmtJNEk9");

            //Set culture for AppResources
            I18N_L10N.I18N_L10N.SetCulture();
            //Create transaleExtension for code behind translation
            TranslateExtension = new TranslateExtension();

            InitializeComponent();

            BindingContext = new AppViewModel();

            if (((AppViewModel)BindingContext).DatabaseProviderSetting == null
                || !Helpers.Database.Database.LocalDbExist())
            {
                MainPage = new Views.FirstTimeStartUp.FTSUMainView();
            }
            else
                MainPage = new LoginView();

            _app = this;
        }

        protected override void OnStart()
        {
            using var activity = Observability.ActivitySource.StartActivity("App.Start");

            IAppState appState = AppServices.Get<IAppState>();
            appState.Init();
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
