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

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class AddItemPage : ContentPage
	{
        internal static ItemModel item = new ItemModel();
        internal Database dbContext = new Database();
        internal List<VatModel> vats;

        public AddItemPage ()
		{
			InitializeComponent ();

            vats = dbContext.GetVat();
            foreach( var item in vats)
            {
                VatPicker.Items.Add(item.Name);
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

        private async void Desc_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new NavigationPage(new ItemDescPage()));
        }

        private void Id_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            ImageButton.IsEnabled = !String.IsNullOrWhiteSpace(Id.Text) ? true : false;

        }

        private async void AddItem_Clicked(object sender, EventArgs e)
        {
            item.ItemId = Id.Text;
            item.Name = Name.Text;
            item.VatId = VatPicker.SelectedIndex+1;

            if (item.ItemId == null || item.Name == null || item.VatId == -1)
            {
                await DisplayAlert("OOPS!", "Please check all fields are correct", "OK");
                return;
            }

            item.Cost = ToDecimal(Cost.Text);
            item.Price = ToDecimal(Price.Text);

            if (item.Cost.Equals(0.00) || item.Cost.Equals(0) || item.Price.Equals(0.00) || item.Price.Equals(0))
            {
                return;
            }

            await Navigation.PushModalAsync(new ItemTemplate(item));
            MessagingCenter.Subscribe<AddItemPage>(this, "Accepted", async (Sender) =>
            {
                await Navigation.PopToRootAsync();
            });
        }

        private decimal ToDecimal(string data)
        {
            try
            {
                decimal result = Convert.ToDecimal(data);
                return result;
            }
            catch (FormatException)
            {
                DisplayAlert("OOPS!", "Somthing is wrong with your 'cost' or 'price'", "OK");
                return 0.0m;
            }
            catch (OverflowException)
            {
                DisplayAlert("OOPS!", "Somthing is wrong with your 'cost' or 'price'", "OK");
                return 0.0m;
            }

        }

        private async void Id_Focused(object sender, FocusEventArgs e)
        {
            if (Device.Idiom == TargetIdiom.Desktop) return;
            var scanner = new MobileBarcodeScanner();
            var options = new MobileBarcodeScanningOptions()
            {
                UseNativeScanning = true,
                PureBarcode = true,
                TryInverted = true
            };
            var results = await scanner.Scan(options);

            Id.Text = results.Text;
        }

        private void Cost_Vat_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Cost.Text) || VatPicker.SelectedIndex == -1) return;
            foreach(var item in vats)
            {
                if (item.VatId == VatPicker.SelectedIndex + 1)
                {
                    Price.Placeholder = $"Recommended price: {ToDecimal(Cost.Text) * (decimal)item.Rate}";
                }
            }
        }
    }
}