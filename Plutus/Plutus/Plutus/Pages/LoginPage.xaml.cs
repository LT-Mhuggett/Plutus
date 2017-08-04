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
using I18N_L10N;

namespace Plutus.Pages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class LoginPage : ContentPage
	{
        TranslateExtension Translate = new TranslateExtension();

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
            var emp = await EmpLogIn();
            if (emp == null)
                return;

            var store = await StoreGetWithEmp(emp);
            if (store == null)
                return;
            Application.Current.MainPage = new NavigationPage(new MainNavigationPage(emp, CurrenList));
            Loading.TogleLoading(LCV, LAI);
        }

        private async void LoginButton_Clicked_No_List()
        {
            Loading.TogleLoading(LCV, LAI);

            var emp = await EmpLogIn();
            if (emp == null)
                return;

            var store = await StoreGetWithEmp(emp);
            if (store == null)
                return;

            Application.Current.MainPage = new NavigationPage(new MainNavigationPage(emp, store));
            Loading.TogleLoading(LCV, LAI);
        }

        internal async Task<EmployeeModel> EmpLogIn()
        {
            var emp = await App.dbContext.Login(UId.Text, PId.Text);
            if (emp == null)
            {
                Loading.TogleLoading(LCV, LAI);
                await DisplayAlert(Translate.ProvideValue("Hmm"), Translate.ProvideValue("DetailsNotCorrectORUserNotExistMesg"), Translate.ProvideValue("OK"));
                return null;
            }
            return emp;
        }

        internal async Task<StoreModel> StoreGetWithEmp(EmployeeModel emp)
        {
            var store = App.dbContext.GetStore(emp.StoreId);
            if (store == null)
            {
                Loading.TogleLoading(LCV, LAI);
                await DisplayAlert(Translate.ProvideValue("Hmm"), Translate.ProvideValue("StoreNotReachableMesg"), Translate.ProvideValue("OK"));
                return null;
            }
            return store;
        }
    }
}