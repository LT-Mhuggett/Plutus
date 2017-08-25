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

        /// <summary>
        /// Basic Constructor for ItemDescPage
        /// sets description as tempDesc
        /// </summary>
        /// <param name="tempDesc">current Description for item</param>
		public ItemDescPage (string tempDesc)
		{
			InitializeComponent ();
            Desc.Text = tempDesc;
		}


        /// <summary>
        /// Updates description with new one the user wrote
        /// then pop this page and call the MessagingCenter Methods 
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that object called with method</param>
        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            description = Desc.Text;
            MessagingCenter.Send(new AddItemPage(), "DescDone");
            MessagingCenter.Send(new UpdateItemPage(), "DescDone");
            await Navigation.PopModalAsync();
        }

        /// <summary>
        /// Unsubscribe from all MessagingCenter Subcription
        /// </summary>
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<AddItemPage>(new AddItemPage(), "DescDone");
            MessagingCenter.Unsubscribe<UpdateItemPage>(new UpdateItemPage(), "DescDone");
        }
    }
}