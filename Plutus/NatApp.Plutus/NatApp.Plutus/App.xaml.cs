using I18N_L10N.Extensions;
using NatApp.Plutus.Services.Loading;
using NatApp.Plutus.ViewModels;
using NatApp.Plutus.Views;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System;
using NatApp.Plutus.Models;
using Plugin.Iconize;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]
namespace NatApp.Plutus
{
    public partial class App : Application
    {
        private static bool _isLoading;
        internal static TranslateExtension translateExtension = null;
        private static App _app;

        public App()
        {
            //Register the syncfusion license
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
                "MTE1NjUwQDMxMzcyZTMyMmUzMFpiVU9HUExTczJEejQ5SFpGSk14ckxYWnUzNW9hN3ZadnV1UlNNcTgwdEU9");
            
            //Set culture for AppResources
            I18N_L10N.I18N_L10N.SetCulture();
            //Create transaleExtension for code behind translation
            translateExtension = new TranslateExtension();

            Iconize.With(new Plugin.Iconize.Fonts.MaterialModule());

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
            //AppCenter.LogLevel = LogLevel.Verbose;
            //AppCenter.Start("ios=81d0ebb5-e7cf-40b2-bcdf-b8f5f13f65dc;android=b20338a6-19b9-4f57-a923-c4efec1fa0a1;uwp=85e2fee4-7bf1-4180-872a-040e636a3a60", typeof(Analytics), typeof(Crashes));
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
            if (isLoading == _isLoading)
                return;
            _isLoading = isLoading;
            TriggerLoadingUI();
        }

        /// <summary>
        /// Toggle loading attribute and trigger the correct UI response
        /// </summary>
        internal protected static void ToggleLoading()
        {
            _isLoading = !_isLoading;
            TriggerLoadingUI();
        }

        /// <summary>
        /// Select the correct UI response using the current loading attribute's value
        /// </summary>
        private protected static void TriggerLoadingUI()
        {
            if (_isLoading)
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
