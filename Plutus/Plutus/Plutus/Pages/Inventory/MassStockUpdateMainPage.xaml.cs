using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Plutus.Helpers;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MassStockUpdateMainPage : ContentPage
    {
        CustomPages.SelectMultipleBasePage<PropertyInfo> multiSelectPage;

		public MassStockUpdateMainPage ()
		{
			InitializeComponent ();
		}
        
	    private async void GenMass_Clicked(object sender, EventArgs e)
	    {
	        var properties = new List<PropertyInfo>();
#if __ANDROID__ || __IOS__
            properties.AddRange(typeof(ItemModel).GetProperties());
#else
	        properties.AddRange(typeof(ItemModel).GetTypeInfo().DeclaredProperties);
#endif
	        properties.RemoveAll(item =>
	            item.Name.Equals("Id") || item.Name.Equals("Image") || item.Name.Equals("VatId") ||
	            item.Name.Equals("CatId") || item.Name.Equals("DisItems") || item.Name.Equals("Transactions") ||
	            item.Name.Equals("Refunds") || item.Name.Equals("Amount") || item.Name.Equals("GroupKey"));
	        multiSelectPage =
	            new Pages.CustomPages.SelectMultipleBasePage<PropertyInfo>(properties)
	            {
	                Title = "Check all Item properties you wish to mass edit"
	            };
            
	        await Navigation.PushModalAsync(multiSelectPage);
	        MessagingCenter.Subscribe<App>((App) Application.Current, "SelectedItems", (senderSub) =>
	            {
	                var selectedProps = multiSelectPage.GetSelection();
	                if (selectedProps.Count <= 0) return;
	                var items = MainPage.InventDbContext.Get<ItemModel>().Select(i => i.Id).ToList();
	                var csv = new Csv(selectedProps, items);
	                csv.CreateFile();
	            });
	    }

	    private async void ImportMass_Clicked(object sender, EventArgs e)
	    {
	        var properties = new List<PropertyInfo>();
#if __ANDROID__ || __IOS__
            properties.AddRange(typeof(ItemModel).GetProperties());
#else
	        properties.AddRange(typeof(ItemModel).GetTypeInfo().DeclaredProperties);
#endif
	        var csv = new Csv();

	        var header = await csv.ValidateHeaderOfFile();
	        var validHeaders = new List<string>();
	        foreach (var field in header)
	        {
	            foreach (var prop in properties)
	            {
	                if (prop.Name == field)
	                {
	                    validHeaders.Add(field);
	                }
	            }
	        }

	        if (header.Count != validHeaders.Count)
	        {
	            await DisplayAlert("Warning", "Data not Matched!!", "OK");
	            return;
	        }

	        var readLines = await csv.ReadFile();
	        MainPage.InventDbContext.SaveKVPAsync<ItemModel, string>(readLines.Skip(1).ToList(), validHeaders);

	    }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            MessagingCenter.Unsubscribe<App>((App) Application.Current, "SelectedItems");
        }
    }
}