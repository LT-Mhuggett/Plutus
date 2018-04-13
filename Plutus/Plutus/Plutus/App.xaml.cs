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
using Plutus.Models;
using Microsoft.Azure.Mobile;
using Microsoft.Azure.Mobile.Analytics;
using Microsoft.Azure.Mobile.Crashes;
using Device = Xamarin.Forms.Device;
using Page = Xamarin.Forms.Page;
#if __ANDROID__ || __IOS__
using System.Reflection;
#else
using Windows.ApplicationModel;
#endif

namespace Plutus
{
    public partial class App : Application
    {
        internal static ObservableCollection<EmployeeModel> EmpsLogged = new ObservableCollection<EmployeeModel>();
        internal static EmployeeModel LastAuthUser = new EmployeeModel();
        internal static StoreModel Store = new StoreModel();
        internal static Database DbContext;
        internal static readonly TranslateExtension Translate = new TranslateExtension();
        internal static int TillAmmount;
        internal static string Version;
        internal static DateTime CurrentDateTime { get; private set; }
        internal static bool OneTimeStockWarning { get; set; }

        public App()
        {
            InitializeComponent();
            Device.StartTimer(TimeSpan.FromSeconds(1), () =>
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
#else
            var versionP = Package.Current.Id.Version;
            Version = $"{versionP.Major}.{versionP.Minor}.{versionP.Build}.{versionP.Revision}";
#endif
            MainPage = FileIO.Exists("App.config") && FileIO.Exists("Database.db")
                ?
                new NavigationPage(new LoginPage())
                : FileIO.Exists("Database.db")
                    ? new NavigationPage(OnlyDB())
                    : new NavigationPage(new Pages.FirstTimeStartUp.MainPage());
            DbContext = new Database();
        }

        protected override void OnStart()
        {
            MobileCenter.Start(
                "uwp=6203c60a-2c30-49c5-a80f-fa96367529e7;" + "android=e4899b2e-f595-4bf7-ab33-e173c89fb21f" +
                "ios=59f118ee-1f83-43f9-804d-59242b97f316;", typeof(Analytics), typeof(Crashes));
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

        Page OnlyDB()
        {
            File.Delete(Path.Combine(FileIO.GetLib(), "Database.db"));
            return new Pages.FirstTimeStartUp.MainPage();
        }
    }
}