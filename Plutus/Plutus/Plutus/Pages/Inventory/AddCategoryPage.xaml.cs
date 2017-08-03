using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class AddCategoryPage : ContentPage
	{
        internal Database dbContext = new Database();
        public AddCategoryPage ()
		{
			InitializeComponent();
		}

        private async void Confirm_Clicked(object sender, EventArgs e)
        {
            CategoryModel category = new CategoryModel {
                Name = Name.Text,
                Description=Description.Text
            };

            dbContext.Add(category);
            dbContext.Save();
            await Navigation.PopModalAsync();
            MessagingCenter.Send(new AddItemPage(), "ConfCat");
            MessagingCenter.Send(new UpdateItemPage(), "ConfCat");
        }

        private async void Cancel_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        protected override void OnDisappearing()
        {
            MessagingCenter.Unsubscribe<AddItemPage>(new AddItemPage(), "ConfCat");
            MessagingCenter.Unsubscribe<UpdateItemPage>(new UpdateItemPage(), "ConfCat");
            MessagingCenter.Unsubscribe<AddItemPage>(new AddItemPage(), "Accepted");
        }
    }
}