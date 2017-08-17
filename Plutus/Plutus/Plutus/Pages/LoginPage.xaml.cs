using System.Threading.Tasks;
using Plutus.Helpers;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Collections.ObjectModel;

namespace Plutus.Pages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class LoginPage : ContentPage
	{
        public LoginPage ()
		{
			InitializeComponent ();

            LoginButton.Clicked += delegate { LoginButton_Clicked_No_List(); };
            PId.Completed += delegate { LoginButton_Clicked_No_List(); };
        }

        public LoginPage(ObservableCollection<EmployeeModel> currenList)
        {
            InitializeComponent();

            LoginButton.Clicked += delegate { LoginButton_Clicked_List(currenList); };
            PId.Completed += delegate { LoginButton_Clicked_List(currenList); };
        }

        private void LoginButton_Clicked_List(ObservableCollection<EmployeeModel> currenList)
        {
            Loading.TogleLoading(LCV, LAI);
            Device.BeginInvokeOnMainThread(async () =>
            {
                //must add check for if the user is already logged in during debuging this not a problem and is more of a convenience for testing
                
                var emp = await EmpLogIn();
                if (emp == null)
                    return;
                else if (!emp.Active)
                    return;

                var store = await StoreGetWithEmp(emp);
                if (store == null)
                    return;
                Application.Current.MainPage = new NavigationPage(new MainNavigationPage(emp, currenList));
                Loading.TogleLoading(LCV, LAI);
            });
        }

        private void LoginButton_Clicked_No_List()
        {
            Loading.TogleLoading(LCV, LAI);
            Device.BeginInvokeOnMainThread(async () =>
            {
                var emp = await EmpLogIn();
                if (emp == null)
                    return;
                else if (!emp.Active)
                    return;

                var store = await StoreGetWithEmp(emp);
                if (store == null) return;

                Application.Current.MainPage = new NavigationPage(new MainNavigationPage(emp, store));
                Loading.TogleLoading(LCV, LAI);
            });
        }

	    internal async Task<EmployeeModel> EmpLogIn()
        {
            var emp = await App.DbContext.Login(UId.Text, PId.Text);
            if (emp != null) return emp;
            Loading.TogleLoading(LCV, LAI);
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DetailsNotCorrectORUserNotExistMesg"), App.Translate.ProvideValue("OK"));
            return null;
        }

        internal async Task<StoreModel> StoreGetWithEmp(EmployeeModel emp)
        {
            var store = App.DbContext.GetStore(emp.StoreId);
            if (store != null) return store;
            Loading.TogleLoading(LCV, LAI);
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("StoreNotReachableMesg"), App.Translate.ProvideValue("OK"));
            return null;
        }
    }
}