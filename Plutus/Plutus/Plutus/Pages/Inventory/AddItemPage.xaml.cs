using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Models;
using Plugin.Media;
using Plutus.Helpers.Interface;
using ZXing.Mobile;
using I18N_L10N;
using ZXing.Net.Mobile.Forms;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class AddItemPage : ContentPage
	{
        internal static ItemModel Item = new ItemModel();
        internal List<VatModel> Vats;
        internal List<CategoryModel> Cats;
        private ZXingScannerPage _scanPage;

        /// <summary>
        /// Basic constructor for AddItemPage
        /// and initalises Vats from the db
        /// </summary>
        public AddItemPage ()
		{
            InitializeComponent();

            Vats = App.DbContext.Get<VatModel>().ToList();
            foreach( var item in Vats)
            {
                VatPicker.Items.Add(item.Name);
            }
            InitCatPicker();
        }

        /// <summary>
        /// Asks the user where to open camera or local photo storage
        /// call the proprete mwthods
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void Image_Clicked(object sender, EventArgs e)
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
        /// Open Description page and send over current description
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
        /// Test if Id.Text has text in it
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private void Id_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            ImageButton.IsEnabled = !string.IsNullOrWhiteSpace(Id.Text) ? true : false;
        }

        /// <summary>
        /// This verifies all user input and ensures they are all within except boundaries
        /// after that this method also adds the item to DB and saves DB changes
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void AddItem_Clicked(object sender, EventArgs e)
        {
            Item.Id = Id.Text;
            Item.Name = Name.Text;
            Item.VatId = VatPicker.SelectedIndex + 1;
            Item.CatId = CatPicker.SelectedIndex + 1;
            Item.Brand = Brand.Text;

            if (Item.Id == null || Item.Name == null || Item.Brand == null || Item.VatId == 0 || Item.CatId == 0 || string.IsNullOrWhiteSpace(Stock.Text))
            {
                await DisplayAlert(App.Translate.ProvideValue("Oops"), App.Translate.ProvideValue("FieldsFilledInMesg"), App.Translate.ProvideValue("OK"));
                return;
            }

            Item.Cost = (decimal)await Conversions.ToDecimal(Cost.Text, App.Translate.ProvideValue("ValueEnteredWrong"));
            Item.Price = (decimal)await Conversions.ToDecimal(Price.Text, App.Translate.ProvideValue("ValueEnteredWrong"));

            if (Item.Cost.Equals(0) || Item.Price.Equals(0))
                return;

            var temp = (int)await Conversions.ToInterger(Stock.Text, App.Translate.ProvideValue("ValueEnteredWrong"));

            if (temp.Equals(-1))
                return;

            await Navigation.PushModalAsync(new ItemTemplate(Item, 0));
            MessagingCenter.Subscribe<AddItemPage>(this, "Accepted", async (Sender) =>
            {
                App.DbContext.Add(Item);
                var stock = new StockModel
                {
                    ItemId = Item.Id,
                    StoreId = App.Store.Id,
                    Quantity = temp
                };
                App.DbContext.Add(stock);
                if(!App.DbContext.Save())
                {
                    await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                    return;
                }
                await Navigation.PopAsync();
            });
        }

        /// <summary>
        /// On Id focus test if mobile if mobile then use mobile scanner else use standard barcode scanner 
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void Id_Focused(object sender, FocusEventArgs e)
        {
            if (Device.Idiom == TargetIdiom.Desktop) return;
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
                    Id.Text = result.Text;
                });
            };
            await Navigation.PushAsync(_scanPage);
        }

        /// <summary>
        /// If both Vat and Cost are set then calculate recommended price
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        /// <returns>Recomended price as Task</returns>
        private async Task Cost_Vat_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Cost.Text) || VatPicker.SelectedIndex == -1) return;
            foreach (var item in Vats)
            {
                if (item.Id == VatPicker.SelectedIndex + 1)
                {
                    Price.Placeholder = $"{App.Translate.ProvideValue("RecPrice")}: {(decimal)await Conversions.ToDecimal(Cost.Text, App.Translate.ProvideValue("ValueEnteredWrong")) * (decimal)item.Rate}";
                }
            }
        }

        /// <summary>
        /// Initalises Cat Picker from DB
        /// </summary>
        protected void InitCatPicker()
        {
            Cats = App.DbContext.Get<CategoryModel>().ToList();
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
            MessagingCenter.Subscribe<AddItemPage>(this, "ConfCat", async (Sender) =>
            {
                InitCatPicker();
                await Navigation.PopModalAsync();
                //Currently a fix, This works but is a waste of procesor time.
            });
        }

        /// <summary>
        /// Check if item exist on unfocus by check id given against DB 
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void Id_Unfocused(object sender, FocusEventArgs e)
        {
            if (!App.DbContext.IsIdSame<ItemModel, string>(Id.Text)) return;
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemExistMesg"), App.Translate.ProvideValue("OK"));
            Id.Text = null;
        }
    }
}