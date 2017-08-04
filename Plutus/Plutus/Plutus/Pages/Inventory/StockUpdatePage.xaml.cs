using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using I18N_L10N;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class StockUpdatePage : ContentPage
	{
        public StockUpdatePage ()
		{
			InitializeComponent ();
		}

        private async void Id_Unfocused(object sender, FocusEventArgs e)
        {
            if (!App.dbContext.IsIdSame(Id.Text))
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemNonExistMesg"), App.Translate.ProvideValue("OK"));
                Id.Text = null;
            }
        }

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            StockModel stock = new StockModel
            {
                StoreId = App.Store.StoreId,
                ItemId = Id.Text,
                Quantity = Convert.ToInt16(Quantity.Text)
            };

            App.dbContext.UpdateStock(stock);
            App.dbContext.Save();
            await Navigation.PopAsync();
        }

        private void Id_Focused(object sender, FocusEventArgs e)
        {
            if (Device.Idiom == TargetIdiom.Desktop) return;
            Scanner.ShowScanner(false, Id);
        }
    }
}