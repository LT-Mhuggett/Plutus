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
using Plutus.Helpers.Extensions;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class AddItemPage : ContentPage
	{
	    private static ItemModel _item = new ItemModel();
	    private readonly List<TaxModel> _vats;
	    private List<CategoryModel> _cats;
        private ZXingScannerPage _scanPage;

        /// <summary>
        /// Basic constructor for AddItemPage
        /// and initalises Vats from the db
        /// </summary>
        public AddItemPage ()
		{
            InitializeComponent();

            _vats = MainPage.InventDbContext.Get<TaxModel>().ToList();
            foreach( var item in _vats)
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
                { App.Translate.ProvideValue("Camera"), () => Camera.getPhoto(_item, Pic)},
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
            _item.Image = Camera.StreamToArray(stream);
        }

        /// <summary>
        /// Open Description page and send over current description
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender called</param>
        private async void Desc_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new NavigationPage(new ItemDescPage(_item.Desc)));
            MessagingCenter.Subscribe<AddItemPage>(this, "DescDone", (Sender) => {
                _item.Desc = ItemDescPage.description;
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
            _item.Id = Id.Text;
            _item.Name = Name.Text;
            _item.VatId = VatPicker.SelectedIndex + 1;
            _item.CatId = CatPicker.SelectedIndex + 1;
            _item.Brand = Brand.Text;

            if (_item.Id == null || _item.Name == null || _item.Brand == null || _item.VatId == 0 || _item.CatId == 0 || string.IsNullOrWhiteSpace(Stock.Text))
            {
                await DisplayAlert(App.Translate.ProvideValue("Oops"), App.Translate.ProvideValue("FieldsFilledInMesg"), App.Translate.ProvideValue("OK"));
                return;
            }

            _item.Cost = (decimal) await Cost.Text.ToDecimal(App.Translate.ProvideValue("ValueEnteredWrong"));
            _item.Price = (decimal) await Price.Text.ToDecimal(App.Translate.ProvideValue("ValueEnteredWrong"));
            _item.ExPrice = (decimal) await ExPrice.Text.ToDecimal(App.Translate.ProvideValue("ValueEnteredWrong"));

            if (_item.Cost.Equals(0) || _item.Price.Equals(0))
                return;

            var temp = (int)await Stock.Text.ToInterger(App.Translate.ProvideValue("ValueEnteredWrong"));

            if (temp.Equals(-1))
                return;

            await Navigation.PushModalAsync(new ItemTemplate(_item, 0));
            MessagingCenter.Subscribe<AddItemPage>(this, "Accepted", async (Sender) =>
            {
                MessagingCenter.Unsubscribe<AddItemPage>(this, "Accepted");
                MainPage.InventDbContext.Add(_item);
                var stock = new StockModel
                {
                    ItemId = _item.Id,
                    StoreId = App.Store.Id,
                    Quantity = temp
                };
                MainPage.InventDbContext.Add(stock);
                if(!MainPage.InventDbContext.Save())
                {
                    await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                    return;
                }
                _item = new ItemModel();
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
	    private async void Cost_Vat_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
	    {
	        if (string.IsNullOrWhiteSpace(Cost.Text) || VatPicker.SelectedIndex == -1) return;
	        foreach (var item in _vats)
	        {
	            if (item.Id != VatPicker.SelectedIndex + 1) continue;
	            ExPrice.Placeholder =
	                $"{App.Translate.ProvideValue("RecPriceExVat")}: {(decimal) await Cost.Text.ToDecimal(App.Translate.ProvideValue("ValueEnteredWrong")) * App.Store.RecMarkup:0.00}";
	            Price.Text = ExPrice.Text != null
	                ? $"{(decimal) await ExPrice.Text.ToDecimal(App.Translate.ProvideValue("valueEnteredWrong")) * (decimal) item.Rate:0.00}"
	                : $"{(decimal) await Cost.Text.ToDecimal(App.Translate.ProvideValue("ValueEnteredWrong")) * App.Store.RecMarkup * (decimal) item.Rate:0.00}";
	        }
	    }

	    /// <summary>
        /// Initalises Cat Picker from DB
        /// </summary>
        private void InitCatPicker()
        {
            _cats = MainPage.InventDbContext.Get<CategoryModel>().ToList();
            CatPicker.Items.Clear();
            foreach (var item in _cats)
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
                CatPicker.SelectedIndex = CatPicker.Items.Count-2;
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
            if (!MainPage.InventDbContext.IsIdSame<ItemModel, string>(Id.Text)) return;
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemExistMesg"), App.Translate.ProvideValue("OK"));
            Id.Text = null;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Id.SetFocusAfterDelay(1);
        }
    }
}