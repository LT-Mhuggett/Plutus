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
        internal ItemModel item;
		public ItemTemplate (ItemModel itemTemp, bool isSearch)
		{
			InitializeComponent ();

            if (isSearch)
            {
                Confirm.IsVisible = false;
                Edit.IsVisible = false;
                Select.IsVisible = true;
            }

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
            ItemCat.Text = App.dbContext.GetCatName(itemTemp.CatId);
            ItemDesc.Text = itemTemp.Desc;
            ItemPrice.Text = itemTemp.Price.ToString();
            item = itemTemp;
		}

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
            MessagingCenter.Send(new AddItemPage(), "Accepted");
            MessagingCenter.Send(new UpdateItemPage(), "Accepted");
        }

        private async void Edit_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        private void Select_Clicked(object sender, EventArgs e)
        {
            MessagingCenter.Send(new UpdateItemPage(), "SearchSelected", item);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<AddItemPage>(new AddItemPage(), "Accepted");
            MessagingCenter.Unsubscribe<UpdateItemPage>(new UpdateItemPage(), "Accepted");
        }
    }
}