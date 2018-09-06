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
	            App.DbContext = new Helpers.Database();

	            var fileC = new List<string>
	            {
	                "<Local>",
	                "<Database>",
	                "<Type>Local Database</Type>",
	                "<TypeIndex>1</TypeIndex>",
	                "</Database>",
	                "</Local>"
	            };

	            FileIO.Save("App.config", fileC.ToArray());
	            Application.Current.MainPage = new NavigationPage(new LoginPage());
	            return;
	        }

	        await Application.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"),
	            string.Format(App.Translate.ProvideValue("DbBRFailed"), "Restoring"),
	            App.Translate.ProvideValue("Cancel"));
	    }
	}
}