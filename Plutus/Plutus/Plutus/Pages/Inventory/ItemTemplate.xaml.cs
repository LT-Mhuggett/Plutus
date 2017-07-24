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
        internal static ItemModel item;
        internal Database dbContext = new Database();
		public ItemTemplate (ItemModel itemtemp)
		{
			InitializeComponent ();

            item = itemtemp;
            itemtemp = null;

            if (item.Image == null)
            {
                throw new NotImplementedException();
                //ItemImage.Source = "";
            }
            else
            {
                ItemImage.Source = ImageSource.FromStream(() => new MemoryStream(item.Image));
            }
            ItemName.Text = item.Name;
            ItemDesc.Text = item.Desc;
            ItemPrice.Text = item.Price.ToString();
            //ItemId.Source = 
		}

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            dbContext.Add(item);
            dbContext.Save();
            await Navigation.PopModalAsync();
            MessagingCenter.Send(new AddItemPage(), "Accepted");
        }

        private async void Edit_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}