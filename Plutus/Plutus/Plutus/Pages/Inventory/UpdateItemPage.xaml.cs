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
using I18N_L10N;
using ZXing.Net.Mobile.Forms;
using ZXing.Mobile;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class UpdateItemPage : ContentPage
	{
        internal ItemModel Item;
        internal ItemModel ChangeItem = new ItemModel();
        internal List<VatModel> Vats;
        internal List<CategoryModel> Cats;
        private ZXingScannerPage _scanPage;

        /// <summary>
        /// Basic constructor for UpdateItemPage
        /// </summary>
        public UpdateItemPage ()
		{
			InitializeComponent ();

            Vats = App.DbContext.GetVat();
            foreach (var item in Vats)
            {
                VatPicker.Items.Add(item.Name);
            }
            InitCatPicker();
        }

        /// <summary>
        /// On item search complete run ItemSearchComplete
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private void ItemSearchCompleted(object sender, EventArgs e)
        {
            ItemSearchComplete();
        }

        /// <summary>
        /// Get all items that fit the search critaria if no items match throw warning, 
        /// if 1 item matches then run Populate,
        /// if more than one exist then initalise a carouselPage using the ItemTemplate page
        /// wait for selected item then run populate 
        /// </summary>
        private async void ItemSearchComplete()
        {
            var items = App.DbContext.GetItem(ItemSearch.Text);
            switch (items.Count)
            {
                case 0:
                    await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemNotFoundMesg"), App.Translate.ProvideValue("OK"));
                    return;
                case 1:
                    Item = items.LastOrDefault();
                    ChangeItem.ItemId = Item.ItemId;
                    ChangeItem.Name = Item.Name;
                    ChangeItem.Image = Item.Image;
                    ChangeItem.Desc = Item.Desc;
                    ChangeItem.Brand = Item.Brand;
                    ChangeItem.CatId = Item.CatId;
                    ChangeItem.VatId = Item.VatId;
                    ChangeItem.Cost = Item.Cost;
                    ChangeItem.Price = Item.Price;
                    ChangeItem.Stock = Item.Stock;
                    ChangeItem.Transactions = Item.Transactions;

                    Populate();
                    break;
                default:
                    var tempPage = new CarouselPage()
                    {
                        Title = App.Translate.ProvideValue("SResults")
                    };
                    foreach (var item in items)
                    {
                        tempPage.Children.Add(new ItemTemplate(item, 1));
                    }

                    await Navigation.PushModalAsync(tempPage);

                    MessagingCenter.Subscribe<UpdateItemPage, ItemModel>(this, "SearchSelected", async (Sender, arg) =>
                    {
                        Item = arg;
                        Populate();
                        ItemSearch.Text = Item.ItemId;
                        await Navigation.PopModalAsync();
                    });
                    break;
            }
        }

        /// <summary>
        /// display all item information
        /// </summary>
        private void Populate()
        {
            Name.Text = Item.Name;
            if (Item.Image == null)
            {
                //ItemImage.Source = "";
            }
            else
            {
                Pic.Source = ImageSource.FromStream(() => new MemoryStream(Item.Image));
            }
            Brand.Text = Item.Brand;
            CatPicker.SelectedIndex = Item.CatId - 1;
            Cost.Text = Convert.ToString(Item.Cost);
            VatPicker.SelectedIndex = Item.VatId - 1;
            Price.Text = Convert.ToString(Item.Price);
            Stock.Text = Convert.ToString(Item.Stock);
            ItemDetails.IsVisible = true;
        }

        /// <summary>
        /// Call ItemDescPage to change the items description
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void Desc_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new NavigationPage(new ItemDescPage(Item.Desc)));
            MessagingCenter.Subscribe<AddItemPage>(this, "DescDone", (Sender) => {
                Item.Desc = ItemDescPage.description;
            });
        }

        /// <summary>
        /// If both Vat and Cost are set then calculate recommended price
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        /// <returns></returns>
        private async Task Cost_Vat_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Cost.Text) || VatPicker.SelectedIndex == -1) return;
            foreach (var item in Vats)
            {
                if (item.VatId == VatPicker.SelectedIndex + 1)
                {
                    Price.Placeholder = $"{App.Translate.ProvideValue("RecPrice")}: {await Conversions.ToDecimal(Cost.Text) * (decimal)item.Rate}";
                }
            }
        }

        /// <summary>
        /// Initalises Cat Picker from DB
        /// </summary>
        protected void InitCatPicker()
        {
            Cats = App.DbContext.GetCats();
            CatPicker.Items.Clear();
            foreach (var item in Cats)
            {
                CatPicker.Items.Add(item.Name);
            }
            CatPicker.Items.Add(App.Translate.ProvideValue("CreateNCate"));
        }

        /// <summary>
        /// Check if Cat picker selectedindex is last index then open add category page
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void CatPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CatPicker.SelectedIndex != CatPicker.Items.Count - 1) return;
            await Navigation.PushModalAsync(new AddCategoryPage());
            MessagingCenter.Subscribe<UpdateItemPage>(this, "ConfCat", async (Sender) =>
            {
                InitCatPicker();
                await Navigation.PopModalAsync();
                //Currently a fix, This works but is a waste of procesor time.
            });
        }

        /// <summary>
        /// Asks the user where to open camera or local photo storage
        /// call the proprete mwthods
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        /// <returns></returns>
        private async Task Image_Clicked(object sender, EventArgs e)
        {
            string action;
            await CrossMedia.Current.Initialize();

            if (Camera.IsCameraAval())
            {
                action = await DisplayActionSheet(App.Translate.ProvideValue("Image"), App.Translate.ProvideValue("Cancel"), null, App.Translate.ProvideValue("Camera"), App.Translate.ProvideValue("PRoll"));
            }
            else
            {
                action = await DisplayActionSheet(App.Translate.ProvideValue("Image"), App.Translate.ProvideValue("Cancel"), null, App.Translate.ProvideValue("PRoll"));
            }

            var actionDic = new Dictionary<string, Action> {
                { App.Translate.ProvideValue("Camera"), () => Camera.getPhoto(Item, Pic)},
                { App.Translate.ProvideValue("PRoll"), GetImageRoll },
                { App.Translate.ProvideValue("Cancel"), () => Console.WriteLine("Escaped!") }
            };

            var actionCall = actionDic[action];
            actionCall();
        }

        /// <summary>
        /// Get image from photo library
        /// </summary>
        public async void GetImageRoll()
        {
            var stream = await DependencyService.Get<IPicturePicker>().GetImageStreamAsync();
            if (stream == null) return;
            Pic.Source = ImageSource.FromStream(() => stream);
            Item.Image = Camera.StreamToArray(stream);
        }

        /// <summary>
        /// Ensures all data values are set and in boudaries and then updates the item in the DB
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        /// <returns></returns>
        private async Task Confirm_Clicked(object sender, EventArgs e)
        {
            ChangeItem.Name = Name.Text;
            ChangeItem.Brand = Brand.Text;
            ChangeItem.CatId = CatPicker.SelectedIndex + 1;
            ChangeItem.Cost = await Conversions.ToDecimal(Cost.Text);
            ChangeItem.Price = await Conversions.ToDecimal(Price.Text);
            ChangeItem.VatId = VatPicker.SelectedIndex + 1;

            if (ChangeItem.Name==Item.Name&&ChangeItem.Brand==Item.Brand&&ChangeItem.CatId==Item.CatId&&ChangeItem.Cost==Item.Cost&&ChangeItem.Desc==Item.Desc&&ChangeItem.Image==Item.Image&&ChangeItem.Price==Item.Price&&ChangeItem.VatId==Item.VatId)
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("NoChangeMadeMesg"), App.Translate.ProvideValue("OK"));
                return;
            }

            await Navigation.PushModalAsync(new ItemTemplate(ChangeItem, 0));
            MessagingCenter.Subscribe<UpdateItemPage>(this, "Accepted", async (Sender) =>
            {
                App.DbContext.UpdateItem(ChangeItem);
                App.DbContext.Save();
                await Navigation.PopAsync();
            });
        }

        /// <summary>
        /// Open mobile scanner
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void ScanButt_Clicked(object sender, EventArgs e)
        {
            var opt = new MobileBarcodeScanningOptions
            {
                UseNativeScanning = true,
                TryHarder = true,
                TryInverted = true
            };
            _scanPage = new ZXingScannerPage(opt, null);
            _scanPage.OnScanResult += (result) =>
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    Navigation.PopAsync();
                    ItemSearch.Text = result.Text;
                    ItemSearchComplete();
                });
            };
            await Navigation.PushAsync(_scanPage);
        }
    }
}