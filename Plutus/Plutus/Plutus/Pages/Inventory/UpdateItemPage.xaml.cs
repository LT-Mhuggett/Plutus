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

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class UpdateItemPage : ContentPage
	{
        internal ItemModel item;
        internal ItemModel changeItem = new ItemModel();
        internal List<VatModel> vats;
        internal List<CategoryModel> cats;
        internal int count = 0;

        public UpdateItemPage ()
		{
			InitializeComponent ();

            vats = App.dbContext.GetVat();
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



        private void ItemSearch_Focused(object sender, FocusEventArgs e)
        {
            if (Device.Idiom == TargetIdiom.Desktop) return;
            Scanner.ShowScanner(false, ItemSearch);
        }

        private async void ItemSearch_Unfocused(object sender, FocusEventArgs e)
        {
            if ((count & 1) == 0)
            {
                List<ItemModel> items = App.dbContext.GetItem(ItemSearch.Text);
                if (items.Count == 0)
                {
                    await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("ItemNotFoundMesg"), App.Translate.ProvideValue("OK"));
                    count--;
                    return;
                }
                else if (items.Count == 1)
                {
                    item = items.LastOrDefault();
                    changeItem.ItemId = item.ItemId;
                    changeItem.Name = item.Name;
                    changeItem.Image = item.Image;
                    changeItem.Desc = item.Desc;
                    changeItem.Brand = item.Brand;
                    changeItem.CatId = item.CatId;
                    changeItem.VatId = item.VatId;
                    changeItem.Cost = item.Cost;
                    changeItem.Price = item.Price;
                    changeItem.Stock = item.Stock;
                    changeItem.Transactions = item.Transactions;

                    Populate();
                }
                else
                {
                    var tempPage = new CarouselPage()
                    {
                        Title = App.Translate.ProvideValue("SResults")
                    };
                    foreach (ItemModel item in items)
                    {
                        tempPage.Children.Add(new ItemTemplate(item, true));
                    }

                    await Navigation.PushModalAsync(tempPage);

                    MessagingCenter.Subscribe<UpdateItemPage, ItemModel>(this, "SearchSelected", async (Sender, arg) =>
                    {
                        item = arg;
                        Populate();
                        ItemSearch.Text = item.ItemId;
                        await Navigation.PopModalAsync();
                    });
                }
            }
            count++;
        }

        private void Populate()
        {
            count = 0;
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
            await Navigation.PushModalAsync(new NavigationPage(new ItemDescPage(item.Desc)));
            MessagingCenter.Subscribe<AddItemPage>(this, "DescDone", (Sender) => {
                item.Desc = ItemDescPage.description;
            });
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
                MessagingCenter.Subscribe<UpdateItemPage>(this, "ConfCat", async (Sender) =>
                {
                    InitCatPicker();
                    await Navigation.PopModalAsync();
                    //Currently a fix, This works but is a waste of procesor time.
                });
            }
        }

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

            Dictionary<string, Action> actionDic = new Dictionary<string, Action> {
                { App.Translate.ProvideValue("Camera"), () => Camera.getPhoto(item, Pic)},
                { App.Translate.ProvideValue("PRoll"), () => GetImageRoll() },
                { App.Translate.ProvideValue("Cancel"), () => Console.WriteLine("Escaped!") }
            };

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

        private async Task Confirm_Clicked(object sender, EventArgs e)
        {
            changeItem.Name = Name.Text;
            changeItem.Brand = Brand.Text;
            changeItem.CatId = CatPicker.SelectedIndex + 1;
            changeItem.Cost = await Conversions.ToDecimal(Cost.Text);
            changeItem.Price = await Conversions.ToDecimal(Price.Text);
            changeItem.VatId = VatPicker.SelectedIndex + 1;

            if (changeItem.Name==item.Name&&changeItem.Brand==item.Brand&&changeItem.CatId==item.CatId&&changeItem.Cost==item.Cost&&changeItem.Desc==item.Desc&&changeItem.Image==item.Image&&changeItem.Price==item.Price&&changeItem.VatId==item.VatId)
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("NoChangeMadeMesg"), App.Translate.ProvideValue("OK"));
                return;
            }

            await Navigation.PushModalAsync(new ItemTemplate(changeItem, false));
            MessagingCenter.Subscribe<UpdateItemPage>(this, "Accepted", async (Sender) =>
            {
                App.dbContext.UpdateItem(changeItem);
                App.dbContext.Save();
                await Navigation.PopAsync();
            });
        }
    }
}