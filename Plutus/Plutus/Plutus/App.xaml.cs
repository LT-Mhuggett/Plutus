#define DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Plutus.Helpers;
using Xamarin.Forms;
using System.IO;
using Plutus.Pages;
using I18N_L10N;

namespace Plutus
{
    public partial class App : Application
	{
		public App ()
		{
		    InitializeComponent();

            //refresh all app files without data wipe or app delete
            //File.Delete(Path.Combine(FileIO.GetLib(), "App.config"));
            //File.Delete(Path.Combine(FileIO.GetLib(), "Database.db"));

            new I18N_L10N.I18N_L10N();

            MainPage = FileIO.Exists("App.config")&&FileIO.Exists("Database.db")?
                new NavigationPage(new LoginPage()):
                FileIO.Exists("Database.db")?
                    throw new NotImplementedException(): 
                    new NavigationPage(new FirstTimeStartUpPage());
		}

		protected override void OnStart ()
		{
			// Handle when your app starts
		}

		protected override void OnSleep ()
		{
			// Handle when your app sleeps
		}

		protected override void OnResume ()
		{
			// Handle when your app resumes
		}
	}
}
