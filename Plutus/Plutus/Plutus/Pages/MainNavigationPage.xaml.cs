using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Database.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Collections.ObjectModel;
using I18N_L10N;
using Plutus.Helpers.Interface;

namespace Plutus.Pages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainNavigationPage : TabbedPage
    {

        /// <summary>
        /// This constructs the MainNavigationPage and sets the App.Store to the correct store
        /// also class the InitPage method with etemp object
        /// </summary>
        /// <param name="etemp">Employee object from LoginPage</param>
        /// <param name="stemp">Store object from LoginPage</param>
        public MainNavigationPage (EmployeeModel etemp, StoreModel stemp)
        {
            App.Store = stemp;
            InitPageAsync(etemp);
        }

        /// <summary>
        /// This Constructs the MainNavigationPAge and sets the App.EmpsLogged to the current Logged in users list
        /// then the InitPage method with etemp object
        /// </summary>
        /// <param name="etemp">Employee object from LoginPage</param>
        /// <param name="currentList">Emps List from before login attempt</param>
        public MainNavigationPage(EmployeeModel etemp, ObservableCollection<EmployeeModel>currentList)
        {
            App.EmpsLogged = currentList;
            InitPageAsync(etemp);
        }

        /// <summary>
        /// This adds a users toolbaritem with command ShowLoggedUsers
        /// then it trys to add etemp to App.EmpsLogged, if App.EmpsLogged is null then
        /// it is re-initalised and etemp is added
        /// </summary>
        /// <param name="etemp">Employee object from LoginPage</param>
        internal async void InitPageAsync(EmployeeModel etemp)
        {
            ToolbarItems.Add(new ToolbarItem
            {
                Text = App.Translate.ProvideValue("Users"),
                Icon = "",
                Command = new Command(this.ShowLoggedUsers)
            });

            try
            {
                App.EmpsLogged.Add(etemp);
            }
            catch (Exception e)
            {
                App.EmpsLogged = new ObservableCollection<EmployeeModel>
                {
                    etemp
                };
#if DEBUG
                Console.WriteLine($"Exception Employee Log null. Error:{e}");
#endif
            }

            InitializeComponent();
            Title = $"Plutus - {App.Store.StoreName}";
            
            Children.Add(new Till.MainPage());
            Children.Add(new Inventory.MainPage());
            Children.Add(new Reports.MainPage());
            Children.Add(new Management.MainPage());
            Children.Add(new Staff.MainPage());
            Children.Add(new Admin.MainPage());
        }

        /// <summary>
        /// Shows Users List page
        /// </summary>
        /// <param name="obj">sender object</param>
        private void ShowLoggedUsers(object obj)
        {
            Navigation.PushModalAsync(new NavigationPage(new UsersLoggedPage()));
        }

        /// <summary>
        /// This helps to stop hadware back button mistake press, it will ask the user if they want to quit the app
        /// and if they select user then the app in destroyed
        /// </summary>
        /// <returns>a boolean on wehter back button was pressed</returns>
        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                var quit = await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("Quit_Mesg"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

                if (!quit) return;
                var closer = DependencyService.Get<ICloseApp>();
                if (closer == null) return;
                App.EmpsLogged = null;
                closer.CloseApp();
            });
            return true;
        }
    }
}