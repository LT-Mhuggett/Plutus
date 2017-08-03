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
        Database dbContext = new Database();
        TranslateExtension Tranlate = new TranslateExtension();
        public StockUpdatePage ()
		{
			InitializeComponent ();
		}

        private async void Id_Unfocused(object sender, FocusEventArgs e)
        {
            if (!dbContext.IsIdSame(Id.Text))
            {
                await DisplayAlert(Tranlate.ProvideValue("Hmm"), Tranlate.ProvideValue("ItemNonExistMesg"), Tranlate.ProvideValue("OK"));
                Id.Text = null;
            }
        }

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            StockModel stock = new StockModel
            {
                StoreId = MainNavigationPage.store.StoreId,
                ItemId = Id.Text,
                Quantity = Convert.ToInt16(Quantity.Text)
            };

            dbContext.UpdateStock(stock);
            dbContext.Save();
            await Navigation.PopAsync();
        }

        private void Id_Focused(object sender, FocusEventArgs e)
        {
            if (Device.Idiom == TargetIdiom.Desktop) return;
            Scanner.ShowScanner(false, Id);
        }
    }
}