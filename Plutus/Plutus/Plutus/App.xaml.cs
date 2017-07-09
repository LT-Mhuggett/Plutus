using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Plutus.Helpers;
using Xamarin.Forms;
using System.IO;
using SQLite;

namespace Plutus
{
	public partial class App : Application
	{
		public App ()
		{
			InitializeComponent();

            if (FileIO.Exists("App.config"))
                MainPage = new NavigationPage(new MainPage());
            else
                MainPage = new NavigationPage(new FirstTimeStartUpPage());
            
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
