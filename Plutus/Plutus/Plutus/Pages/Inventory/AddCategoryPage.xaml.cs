using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Plutus.Helpers.Extensions;
using Database.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class AddCategoryPage : ContentPage
	{
        /// <summary>
        /// Basic constructor for AddCategoryPage
        /// </summary>
        public AddCategoryPage ()
		{
			InitializeComponent();
		}

        /// <summary>
        /// This method adds the new category to the DB and then returns to the page that called it
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender used</param>
        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            CategoryModel category = new CategoryModel {
                Name = Name.Text,
                Description=Description.Text
            };

            MainPage.InventDbContext.Add(category);
            if(!MainPage.InventDbContext.Save())
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                return;
            }
            await Navigation.PopModalAsync();
            MessagingCenter.Send(new AddItemPage(), "ConfCat");
            MessagingCenter.Send(new UpdateItemPage(), "ConfCat");
        }

        /// <summary>
        /// Cancel adding a new category, return to previous page by popping current page
        /// </summary>
        /// <param name="sender">object that called the method</param>
        /// <param name="e">Event that the sender used</param>
        private async void Cancel_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        /// <summary>
        /// Unsubscribe to all MessageingCenter subscriptions
        /// </summary>
        protected override void OnDisappearing()
        {
            MessagingCenter.Unsubscribe<AddItemPage>(new AddItemPage(), "ConfCat");
            MessagingCenter.Unsubscribe<UpdateItemPage>(new UpdateItemPage(), "ConfCat");
            MessagingCenter.Unsubscribe<AddItemPage>(new AddItemPage(), "Accepted");
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Name.SetFocusAfterDelay(1);
        }
    }
}