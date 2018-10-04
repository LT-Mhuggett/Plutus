using System.Threading.Tasks;
using Plutus.Helpers;
using Database.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Collections.ObjectModel;
using System.Linq;
using Plutus.Helpers.Extensions;

namespace Plutus.Pages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class LoginPage : ContentPage
	{
#region NoUserLogin
        /// <summary>
        /// Basic Constructor for the LoginPage object
        /// </summary>
        public LoginPage ()
		{
			InitializeComponent ();

            LoginButton.Clicked += delegate { LoginButton_Clicked_No_List(); };
            PId.Completed += delegate { LoginButton_Clicked_No_List(); };
        }

        /// <summary>
        /// This is the Login method it calls EmpLogIn and StoreGetWithEmp
        /// it then switchs the App MainPage to MainNavigationPage()
        /// </summary>
        private void LoginButton_Clicked_No_List()
        {
            MainView.TogleLoading(LCV, LAI);
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
                MainView.TogleLoading(LCV, LAI);
            });
        }
#endregion

#region MultiUserLogin
        /// <summary>
        /// Multi-User Constructor for LoginPage object
        /// </summary>
        /// <param name="currenList">This is the current Loged in user list</param>
        public LoginPage(ObservableCollection<EmployeeModel> currenList)
        {
            InitializeComponent();

            LoginButton.Clicked += delegate { LoginButton_Clicked_List(currenList); };
            PId.Completed += delegate { LoginButton_Clicked_List(currenList); };
        }

        /// <summary>
        /// This is the Login method it calls EmpLogIn
        /// it then switchs the App MainPage to MainNavigationPage()
        /// </summary>
        /// <param name="currenList">Current Logged in user list</param>
        private void LoginButton_Clicked_List(ObservableCollection<EmployeeModel> currenList)
        {
            MainView.TogleLoading(LCV, LAI);
            Device.BeginInvokeOnMainThread(async () =>
            {
                //must add check for if the user is already logged in during debuging this not a problem and is more of a convenience for testing
                
                var emp = await EmpLogIn();
                if (emp == null)
                    return;
                else if (!emp.Active)
                    return;
                Application.Current.MainPage = new NavigationPage(new MainNavigationPage(emp, currenList));
                MainView.TogleLoading(LCV, LAI);
            });
        }
        #endregion

        /// <summary>
        /// Checks if the details entered for emp are correct
        /// </summary>
        /// <returns>Employee object or null </returns>
        internal async Task<EmployeeModel> EmpLogIn()
        {
            var emp = await App.DbContext.Login(UId.Text, PId.Text);
            if (emp != null) return emp;
            MainView.TogleLoading(LCV, LAI);
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DetailsNotCorrectORUserNotExistMesg"), App.Translate.ProvideValue("OK"));
            return null;
        }

        /// <summary>
        /// Gets the store assoiated with the employee
        /// </summary>
        /// <param name="emp">Emp object from EmpLogIn</param>
        /// <returns>Store object or null</returns>
        internal async Task<StoreModel> StoreGetWithEmp(EmployeeModel emp)
        {
            var store = App.DbContext.Get<StoreModel>()
                .SingleOrDefault(s => s.Id.Equals(emp.StoreId));
            if (store != null) return store;
            MainView.TogleLoading(LCV, LAI);
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("StoreNotReachableMesg"), App.Translate.ProvideValue("OK"));
            return null;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            UId.SetFocusAfterDelay(1);
        }
    }
}