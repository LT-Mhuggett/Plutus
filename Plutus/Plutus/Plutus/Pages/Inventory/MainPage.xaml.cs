using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
		public MainPage ()
		{
			InitializeComponent ();
		}

        private async void AddItem_Clicked(object sender, EventArgs e)
        {
            if (Authorisation.IsAuthorised("ItemARU"))
            {
                await Navigation.PushAsync(new AddItemPage());
                return;
            }
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
        }

        private async void UpdateItem_Clicked(object sender, EventArgs e)
        {
            if (Authorisation.IsAuthorised("ItemARU"))
            {
                await Navigation.PushAsync(new UpdateItemPage());
                return;
            }
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
        }

        private async void StockUpdate_Clicked(object sender, EventArgs e)
        {
            if (Authorisation.IsAuthorised("StockU"))
            {
                await Navigation.PushAsync(new StockUpdatePage());
                return;
            }
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
        }
    }
}