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

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class AddItemPage : ContentPage
	{
        internal static ItemModel item = new ItemModel();
        internal List<VatModel> vats;
        internal List<CategoryModel> cats;

        public AddItemPage ()
		{
            InitializeComponent();

            vats = App.dbContext.GetVat();
            foreach( var item in vats)
            {
                VatPicker.Items.Add(item.Name);
            }
            InitCatPicker();
        }

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

            Dictionary<string, Action> actionDic = new Dictionary<string, Action>();
            actionDic.Add(App.Translate.ProvideValue("Camera"), () => Camera.getPhoto(item, Pic));
            actionDic.Add(App.Translate.ProvideValue("PRoll"), () => GetImageRoll());
            actionDic.Add(App.Translate.ProvideValue("Cancel"), ()=>Console.WriteLine("Escaped!"));

            Action actionCall = actionDic[action];
            actionCall();
        }

        public async void GetImageRoll()
        {
            Stream stream = await DependencyService.Get<IPicturePicker>().GetImageStreamAsync();
            if (stream != null)
            {
                Pic.Source = ImageSource.FromStream(() => stream);
                item.Image = Camera.StreamToArray(stream);
            }
        }

        private async void Desc_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new NavigationPage(new ItemDescPage(item.Desc)));
            MessagingCenter.Subscribe<AddItemPage>(this, "DescDone", (Sender) => {
                item.Desc = ItemDescPage.description;
            });
        }

        private void Id_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            ImageButton.IsEnabled = !String.IsNullOrWhiteSpace(Id.Text) ? true : false;
        }

        private async void AddItem_Clicked(object sender, EventArgs e)
        {
            item.ItemId = Id.Text;
            item.Name = Name.Text;
            item.VatId = VatPicker.SelectedIndex + 1;
            item.CatId = VatPicker.SelectedIndex + 1;
            item.Brand = Brand.Text;

            if (item.ItemId == null || item.Name == null || item.Brand == null || item.VatId == 0 || item.CatId == 0 || string.IsNullOrWhiteSpace(Stock.Text))
            {
                await DisplayAlert(App.Translate.ProvideValue("Oops"), App.Translate.ProvideValue("FieldsFilledInMesg"), App.Translate.ProvideValue("OK"));
                return;
            }

            item.Cost = await Conversions.ToDecimal(Cost.Text);
            item.Price = await Conversions.ToDecimal(Price.Text);

            if (item.Cost.Equals(0.00) || item.Cost.Equals(0) || item.Price.Equals(0.00) || item.Price.Equals(0))
                return;

            var temp = await Conversions.ToInterger(Stock.Text);

            if (temp.Equals(-1))
                return;

            await Navigation.PushModalAsync(new ItemTemplate(item, false));
            MessagingCenter.Subscribe<AddItemPage>(this, "Accepted", async (Sender) =>
            {
                App.dbContext.Add(item);
                StockModel stock = new StockModel
                {
                    ItemId = item.ItemId,
                    StoreId = App.Store.StoreId,
                    Quantity = temp
                };
                App.dbContext.Add(stock);
                App.dbContext.Save();

                await Navigation.PopAsync();
            });
        }

        private void Id_Focused(object sender, FocusEventArgs e)
        {
            if (Device.Idiom == TargetIdiom.Desktop) return;
            Scanner.ShowScanner(false, Id);
        }

        private async Task Cost_Vat_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Cost.Text) || VatPicker.SelectedIndex == -1) return;
            foreach (var item in vats)
            {
                if (item.VatId == VatPicker.SelectedIndex + 1)
                {
                    Price.Placeholder = $"{App.Translate.ProvideValue("RecPrice")}: {await Conversions.ToDecimal(Cost.Text) * (decimal)item.Rate}";
                }
            }
        }

        protected void InitCatPicker()
        {
            cats = App.dbContext.GetCats();
            CatPicker.Items.Clear();
            foreach (var item in cats)
            {
                CatPicker.Items.Add(item.Name);
            }
            CatPicker.Items.Add(App.Translate.ProvideValue("CreateNCate"));
        }

        private async void CatPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CatPicker.SelectedIndex == CatPicker.Items.Count - 1)
            {
                await Navigation.PushModalAsync(new AddCategoryPage());
                MessagingCenter.Subscribe<AddItemPage>(this, "ConfCat", async (Sender) =>
                {
                    InitCatPicker();
                    await Navigation.PopModalAsync();
                    //Currently a fix, This works but is a waste of procesor time.
                });
            }
        }

        private async void Id_Unfocused(object sender, FocusEventArgs e)
        {
            if (App.dbContext.IsIdSame(Id.Text))
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemExistMesg"), App.Translate.ProvideValue("OK"));
                Id.Text = null;
            }
        }
    }
}