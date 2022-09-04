using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Helpers;
using Plutus.Frontend.ClientUI.Pages;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Application = Microsoft.Maui.Controls.Application;

namespace Plutus.Frontend.ClientUI
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            //Setup current culture and Translation
            MainPage = new NavigationPage(ServiceHelper.GetService<LoginPage>());
        }

        protected override void OnStart()
        {
            base.OnStart();

            AppCenter.LogLevel = LogLevel.Verbose;
            AppCenter.Start("ios=81d0ebb5-e7cf-40b2-bcdf-b8f5f13f65dc;android=b20338a6-19b9-4f57-a923-c4efec1fa0a1;windowsdesktop=85e2fee4-7bf1-4180-872a-040e636a3a60", typeof(Analytics), typeof(Crashes));

            var appState = ServiceHelper.GetService<IAppState>();
            appState.Init();
#if DEBUG
            appState.SetAppLogLevel(AppLogLevel.Verbose);
#else
            appState.SetAppLogLevel(AppLogLevel.Info);
#endif
        }
    }
}
