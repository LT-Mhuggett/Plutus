using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Plutus.Helpers;
using Xamarin.Forms;

namespace Plutus
{
	public partial class App : Application
	{
		public App ()
		{
            SaveAndLoad.Save("test", "test123");
			InitializeComponent();

			MainPage = new Plutus.MainPage();
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
