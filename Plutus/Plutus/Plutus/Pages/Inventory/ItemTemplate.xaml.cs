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
        #region MainBasket
        /// <summary>
        /// Basic constructor for ItemTemplate
        /// Sets specific view for the action the page is doing
        /// initalise item with itemTemp
        /// </summary>
        /// <param name="itemTemp">The item to be displayed</param>
        /// <param name="intializer">To set the correct view for the page</param>
        public ItemTemplate (ItemModel itemTemp, byte intializer)
		{
			InitializeComponent ();

            switch (intializer)
            {
                case 0:
                    break;
                case 1:
                    Confirm.IsVisible = false;
                    Edit.IsVisible = false;
                    Select.IsVisible = true;
                    break;
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
            ItemCat.Text = App.DbContext.GetCatName(itemTemp.CatId);
            ItemDesc.Text = itemTemp.Desc;
            ItemPrice.Text = itemTemp.Price.ToString();
            item = itemTemp;
		}
        #endregion

        #region BasketConstructor
        /// <summary>
        /// Constructor for ItemTemplate
        /// Sets specific view for the action the page is doing
        /// initalise item with itemTemp
        /// </summary>
        /// <param name="itemTemp">Item to Dsiplay</param>
        public ItemTemplate(Basket itemTemp)
        {
            InitializeComponent();

            Confirm.IsVisible = false;
            Edit.IsVisible = false;
            Close.IsVisible = true;

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
            ItemCat.Text = App.DbContext.GetCatName(itemTemp.CatId);
            ItemDesc.Text = itemTemp.Desc;
            ItemPrice.Text = itemTemp.Price.ToString();
            item = itemTemp;
        }
#endregion

        /// <summary>
        /// Confirm item inforamtion correct
        /// reply with MessagingCenter
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
            AddItemPage.FinalizeDBActions(AddItemPage.Instance);
            MessagingCenter.Send(new UpdateItemPage(), "Accepted");
        }

        /// <summary>
        /// pop current page for editing of item information
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void Edit_Closed_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        /// <summary>
        /// Send selected item back to searching page
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private void Select_Clicked(object sender, EventArgs e)
        {
            MessagingCenter.Send(new UpdateItemPage(), "SearchSelected", item);
        }

        /// <summary>
        /// Unsubscribe from all MessagingCenter Subscriptions
        /// </summary>
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<AddItemPage>(new AddItemPage(), "Accepted");
            MessagingCenter.Unsubscribe<UpdateItemPage>(new UpdateItemPage(), "Accepted");
            MessagingCenter.Unsubscribe<UpdateItemPage>(new UpdateItemPage(), "SearchSelected");
        }
    }
}