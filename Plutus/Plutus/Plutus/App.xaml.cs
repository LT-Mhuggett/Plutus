#define DEBUG
using System;
using System.Collections.Generic;
using Plutus.Helpers;
using Xamarin.Forms;
using System.IO;
using Plutus.Pages;
using I18N_L10N;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Database.Models;/*
using Microsoft.Azure.Mobile;
using Microsoft.Azure.Mobile.Analytics;
using Microsoft.Azure.Mobile.Crashes;*/
using Plutus.Helpers.Interface;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Crashes;
using Microsoft.AppCenter.Distribute;
#if __ANDROID__ || __IOS__
using System.Reflection;
#elif WINDOWS_UWP
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
#endif

namespace Plutus
{
    public partial class App : Application
    {
        /**
         * App Globals 
         */
        //App Settings Context
        internal static AppSettings AppSettings = new AppSettings();
        //Current Logged Users
        internal static ObservableCollection<EmployeeModel> EmpsLogged = new ObservableCollection<EmployeeModel>();
        //Last Authorized User
        internal static EmployeeModel LastAuthUser = new EmployeeModel();
        //Current Store
        internal static StoreModel Store = new StoreModel();
        //App DB Context
        internal static Helpers.Database DbContext;
        //I8N_L10N
        internal static readonly TranslateExtension Translate = new TranslateExtension();
        //Till Monatary Total
        internal static int TillAmmount;
        //Current Version
        internal static string Version;
        internal static DateTime CurrentDateTime { get; private set; }
        internal static bool OneTimeStockWarning { get; set; }

        public App()
        {
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("MjU2NzZAMzEzNjJlMzMyZTMwU2llYmNZU3Nic0R2Z1IxQ3J0bi9LRjhYVHpQaDBCdnhEdEdDakc4WTdsbz0=");
            InitializeComponent();
            Xamarin.Forms.Device.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                CurrentDateTime = DateTime.Now;
                return true;
            });

            OneTimeStockWarning = false;

            //refresh all app files without data wipe or app delete
            //File.Delete(Path.Combine(FileIO.GetLib(), "App.config"));
            //File.Delete(Path.Combine(FileIO.GetLib(), "Database.db"));
#if __ANDROID__ || __IOS__
            Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
#elif WINDOWS_UWP
            var versionP = Package.Current.Id.Version;
            Version = $"{versionP.Major}.{versionP.Minor}.{versionP.Build}.{versionP.Revision}";
#endif
            if (AppSettings.DatabaseProvider == null)
            {
                MainPage = new NavigationPage(new Pages.FirstTimeStartUp.MainPage());
                return;
            }
            MainPage = new NavigationPage(new LoginPage());
            DbContext = new Helpers.Database(AppSettings.DatabaseProvider);
            
        }

        protected override async void OnStart()
        {
            AppCenter.LogLevel = LogLevel.Verbose;
            AppCenter.Start("uwp=85e2fee4-7bf1-4180-872a-040e636a3a60;" +
                "android={b20338a6-19b9-4f57-a923-c4efec1fa0a1}" +
                "ios={81d0ebb5-e7cf-40b2-bcdf-b8f5f13f65dc}", typeof(Analytics), typeof(Crashes), typeof(Distribute));
            /*
            MobileCenter.Start(
                "uwp=6203c60a-2c30-49c5-a80f-fa96367529e7;" + "android=e4899b2e-f595-4bf7-ab33-e173c89fb21f" +
                "ios=59f118ee-1f83-43f9-804d-59242b97f316;", typeof(Analytics), typeof(Crashes));*/
#if WINDOWS_UWP
            if (ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0))
            {
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
            }
#endif
        }

        protected override void OnSleep()
        {
            if (Pages.Till.MainPage.StoredTrans == null) return;
            foreach (var tempTran in Pages.Till.MainPage.StoredTrans)
            {
                var items = new List<SavedItemModel>();
                foreach (var tempItem in tempTran.Value.Item2)
                {
                    var item = new SavedItemModel
                    {
                        Item = tempItem,
                        Amount = tempItem.Amount
                    };
                    items.Add(item);
                }

                var tran = new SavedTransactionModel
                {
                    Name = tempTran.Value.Item1,
                    SavedItems = items
                };
                DbContext.Add(tran);
            }
            if (!DbContext.Save())
            {
                Debug.WriteLine("Save Failed On Close/Sleep!");
            }
        }

        protected override void OnResume()
        {
            // Handle when your app resumes
        }
    }
}