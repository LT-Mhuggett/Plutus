using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Plutus.Models;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class ItemTemplate : ContentPage
	{
        internal Database dbContext = new Database();
		public ItemTemplate (ItemModel itemTemp)
		{
			InitializeComponent ();
            

            if (itemTemp.Image == null)
            {
                //ItemImage.Source = "";
            }
            else
            {
                ItemImage.Source = ImageSource.FromStream(() => new MemoryStream(itemTemp.Image));
            }
            ItemName.Text = itemTemp.Name;
            ItemBrand.Text = itemTemp.Brand;
            ItemCat.Text = dbContext.getCatName(itemTemp.CatId);
            ItemDesc.Text = itemTemp.Desc;
            ItemPrice.Text = itemTemp.Price.ToString();
		}

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
            MessagingCenter.Send(new AddItemPage(), "Accepted");
        }

        private async void Edit_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}