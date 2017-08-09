using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Models;
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
        public MainNavigationPage (EmployeeModel etemp, StoreModel stemp)
        {
            App.Store = stemp;
            InitPage(etemp);
        }

        public MainNavigationPage(EmployeeModel etemp, ObservableCollection<EmployeeModel>currentList)
        {
            App.EmpsLogged = currentList;
            InitPage(etemp);
        }

        internal void InitPage(EmployeeModel etemp)
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
        }

        private void ShowLoggedUsers(object obj)
        {
            Navigation.PushModalAsync(new NavigationPage(new UsersLoggedPage()));
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                var quit = await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("Quit?Mesg"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

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