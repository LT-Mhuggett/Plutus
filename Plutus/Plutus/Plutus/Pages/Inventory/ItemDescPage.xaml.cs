using Plutus.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class ItemDescPage : ContentPage
	{
        internal static string description;
		public ItemDescPage (string tempDesc)
		{
			InitializeComponent ();
            Desc.Text = tempDesc;
		}

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            description = Desc.Text;
            MessagingCenter.Send(new AddItemPage(), "DescDone");
            MessagingCenter.Send(new UpdateItemPage(), "DescDone");
            await Navigation.PopModalAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<AddItemPage>(new AddItemPage(), "DescDone");
            MessagingCenter.Unsubscribe<UpdateItemPage>(new UpdateItemPage(), "DescDone");
        }
    }
}