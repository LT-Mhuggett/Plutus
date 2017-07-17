using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Plutus.Data;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class LoginPage : ContentPage
	{
		public LoginPage ()
		{
			InitializeComponent();

            Database dbContext = new Database();
        }

        private void LoginButton_Clicked(object sender, EventArgs e)
        {
            var emp = Database.Login(UId.Text, PId.Text);
            if(emp == null)
            {
                DisplayAlert("Hmm...", "We can't find any user with those details\nPlease try again.", "OK");
                return;
            }
            var store = Database.getStore(emp.StoreId);
            if (store == null)
            {
                DisplayAlert("Hmm...", "There has been a problem on our end, please check the database for corruption.", "OK");
                return;
            }
            Application.Current.MainPage = new NavigationPage(new MainPage(emp, store));
        }
    }
}