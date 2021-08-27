using I18N_L10N.Extensions;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using NatApp.Plutus.Services.Analytics;
using NatApp.Plutus.Services.Loading;
using NatApp.Plutus.ViewModels;
using NatApp.Plutus.Views;
using Plugin.Iconize;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]
namespace NatApp.Plutus
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

            Iconize.With(new Plugin.Iconize.Fonts.MaterialModule());

            InitializeComponent();

            DependencyService.Get<AppState>();
            DependencyService.Get<Logger>();

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
            AppCenter.LogLevel = LogLevel.Verbose;
            AppCenter.Start("ios=81d0ebb5-e7cf-40b2-bcdf-b8f5f13f65dc;android=b20338a6-19b9-4f57-a923-c4efec1fa0a1;uwp=85e2fee4-7bf1-4180-872a-040e636a3a60", typeof(Analytics), typeof(Crashes));

            IAppState appState = DependencyService.Get<IAppState>();
            appState.Init();
#if DEBUG
            appState.SetAppLogLevel(AppLogLevel.Verbose);
#else
            appState.SetAppLogLevel(AppLogLevel.Info);
#endif
        }

        protected override void OnSleep()
        {
            // Handle when your app sleeps
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
                DependencyService.Get<ILoadingViewService>().ShowLoadingPage();
            }
            else
            {
                GetViewModel().CurrentLoadingItem = "";
                DependencyService.Get<ILoadingViewService>().HideLoadingPage();

            }
        }
        #endregion
    }
}
