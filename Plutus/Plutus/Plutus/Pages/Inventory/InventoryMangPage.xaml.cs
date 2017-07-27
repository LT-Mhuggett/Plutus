using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class InventoryMangPage : ContentPage
	{
		public InventoryMangPage ()
		{
			InitializeComponent ();
		}

        private async void AddItem_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new AddItemPage());
        }

        private async void UpdateItem_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new UpdateItemPage());
        }

        private async void StockUpdate_Clicked(object sender, EventArgs e)
        {
            //await Navigation.PushAsync(new ());
        }
    }
}