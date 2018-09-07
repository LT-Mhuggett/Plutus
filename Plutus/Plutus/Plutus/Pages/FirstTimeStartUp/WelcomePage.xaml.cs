using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.FirstTimeStartUp
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class WelcomePage : ContentPage
	{
		public WelcomePage ()
		{
			InitializeComponent ();
		}

        
	    private async void Restore_Clicked(object sender, EventArgs e)
	    {
	        App.DbContext = null;
            
	        var transfSucc = await FileIO.Restore();

	        if (transfSucc)
	        {
	            await Application.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Success"),
	                string.Format(App.Translate.ProvideValue("DbBRSucc"), "Restored"),
	                App.Translate.ProvideValue("Cancel"));
                App.AppSettings.DatabaseProvider = "Sqlite";
	            App.DbContext = new Helpers.Database(App.AppSettings.DatabaseProvider);
                Application.Current.MainPage = new NavigationPage(new LoginPage());
	            return;
	        }

	        await Application.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"),
	            string.Format(App.Translate.ProvideValue("DbBRFailed"), "Restoring"),
	            App.Translate.ProvideValue("Cancel"));
	    }
	}
}