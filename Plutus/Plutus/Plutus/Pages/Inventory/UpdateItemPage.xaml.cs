using Plutus.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plugin.Media;
using System.IO;
using Plutus.Helpers.Interface;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class UpdateItemPage : ContentPage
	{
        internal Database dbContext = new Database();
        internal ItemModel item;
        internal List<VatModel> vats;
        internal List<CategoryModel> cats;
        public UpdateItemPage ()
		{
			InitializeComponent ();

            vats = dbContext.GetVat();
            foreach (var item in vats)
            {
                VatPicker.Items.Add(item.Name);
            }
            InitCatPicker();
        }

        private void Item_Search(object sender, EventArgs e)
        {
            ItemSearch.Unfocus();
        }

        private async void ItemSearch_Unfocused(object sender, FocusEventArgs e)
        {
            List<ItemModel> items = dbContext.GetItem(ItemSearch.Text);
            if(items.Count == 0)
            {
                await DisplayAlert("Hmm...", "Can't find a item with that ID or Name", "OK");
                return;
            }
            else if(items.Count == 1)
            {
                item = items.LastOrDefault();
            }
            else
            {

            }
            Populate();
        }

        private void Populate()
        {
            Name.Text = item.Name;
            if (item.Image == null)
            {
                //ItemImage.Source = "";
            }
            else
            {
                Pic.Source = ImageSource.FromStream(() => new MemoryStream(item.Image));
            }
            Brand.Text = item.Brand;
            CatPicker.SelectedIndex = item.CatId - 1;
            Cost.Text = Convert.ToString(item.Cost);
            VatPicker.SelectedIndex = item.VatId - 1;
            Price.Text = Convert.ToString(item.Price);
            Stock.Text = Convert.ToString(item.Stock);
            ItemDetails.IsVisible = true;
        }

        private async void Desc_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new NavigationPage(new ItemDescPage()));
        }

        private async Task Cost_Vat_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Cost.Text) || VatPicker.SelectedIndex == -1) return;
            foreach (var item in vats)
            {
                if (item.VatId == VatPicker.SelectedIndex + 1)
                {
                    Price.Placeholder = $"Recommended price: {await ToDecimal(Cost.Text) * (decimal)item.Rate}";
                }
            }
        }

        private async Task<decimal> ToDecimal(string data)
        {
            try
            {
                decimal result = Convert.ToDecimal(data);
                return result;
            }
            catch (FormatException)
            {
                await DisplayAlert("OOPS!", "Somthing is wrong with your 'cost' or 'price'", "OK");
                return 0.0m;
            }
            catch (OverflowException)
            {
                await DisplayAlert("OOPS!", "Somthing is wrong with your 'cost' or 'price'", "OK");
                return 0.0m;
            }

        }

        protected void InitCatPicker()
        {
            cats = dbContext.GetCats();
            CatPicker.Items.Clear();
            foreach (var item in cats)
            {
                CatPicker.Items.Add(item.Name);
            }
            CatPicker.Items.Add("'Create new Category'");
        }

        private async void CatPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CatPicker.SelectedIndex == CatPicker.Items.Count - 1)
            {
                await Navigation.PushModalAsync(new AddCategoryPage());
                MessagingCenter.Subscribe<UpdateItemPage>(this, "ConfCat", async (Sender) =>
                {
                    InitCatPicker();
                    await Navigation.PopModalAsync();
                    //Currently a fix, This works but is a waste of procesor time.
                });
            }
        }

        private async void Image_Clicked(object sender, EventArgs e)
        {
            string action;
            await CrossMedia.Current.Initialize();

            if (Camera.IsCameraAval())
            {
                action = await DisplayActionSheet("Picture", "Cancel", null, "Camera", "Photo Roll");
            }
            else
            {
                action = await DisplayActionSheet("Picture", "Cancel", null, "Photo Roll");
            }


            switch (action)
            {
                case "Camera":
                    Camera.getPhoto(item, Pic);
                    break;
                case "Photo Roll":
                    Stream stream = await DependencyService.Get<IPicturePicker>().GetImageStreamAsync();
                    if (stream != null)
                    {
                        Pic.Source = ImageSource.FromStream(() => stream);
                        item.Image = Camera.StreamToArray(stream);
                    }
                    break;
                case "Cancel":
                    break;
            }
        }
    }
}