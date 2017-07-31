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
using System.Collections.ObjectModel;

namespace Plutus.Pages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class LoginPage : ContentPage
	{
        internal static Database dbContext = new Database();

        public LoginPage ()
		{
			InitializeComponent ();

            LoginButton.Clicked += delegate { LoginButton_Clicked_No_List(); };
		}

        public LoginPage(ObservableCollection<EmployeeModel> CurrenList)
        {
            InitializeComponent();

            LoginButton.Clicked += delegate { LoginButton_Clicked_List(CurrenList); };
        }

        private async void LoginButton_Clicked_List(ObservableCollection<EmployeeModel> CurrenList)
        {
            //must add check for if the user is already logged in during debuging this not a problem and is more of a convenience for testing
            Loading.TogleLoading(LCV, LAI);
            var emp = await dbContext.Login(UId.Text, PId.Text);
            if (emp == null)
            {
                Loading.TogleLoading(LCV, LAI);
                await DisplayAlert("Hmm...", "We can't find any user with those details\nPlease try again.", "OK");
                return;
            }
            var store = dbContext.GetStore(emp.StoreId);
            if (store == null)
            {
                Loading.TogleLoading(LCV, LAI);
                await DisplayAlert("Hmm...", "There has been a problem on our end, please check the database for corruption.", "OK");
                return;
            }
            Application.Current.MainPage = new NavigationPage(new MainNavigationPage(emp, store, CurrenList));
            Loading.TogleLoading(LCV, LAI);
        }

        private async void LoginButton_Clicked_No_List()
        {
            Loading.TogleLoading(LCV, LAI);
            var emp = await dbContext.Login(UId.Text, PId.Text);
            if (emp == null)
            {
                Loading.TogleLoading(LCV, LAI);
                await DisplayAlert("Hmm...", "We can't find any user with those details\nPlease try again.", "OK");
                return;
            }
            var store = dbContext.GetStore(emp.StoreId);
            if (store == null)
            {
                Loading.TogleLoading(LCV, LAI);
                await DisplayAlert("Hmm...", "There has been a problem on our end, please check the database for corruption.", "OK");
                return;
            }
            Application.Current.MainPage = new NavigationPage(new MainNavigationPage(emp, store));
            Loading.TogleLoading(LCV, LAI);
        }

        /*
        private async Task LoginButton_Clicked(object sender, EventArgs e)
        {
            
        }*/
    }
}