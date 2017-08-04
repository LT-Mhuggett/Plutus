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
            stemp = null;
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

            App.EmpsLogged.Add(etemp);
            etemp = null;

            InitializeComponent();
            Title = $"Plutus - {App.Store.StoreName}";

            Children.Add(new Inventory.InventoryMangPage());
        }

        private void ShowLoggedUsers(object obj)
        {
            Navigation.PushModalAsync(new NavigationPage(new UsersLoggedPage()));
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                bool quit = await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("Quit?Mesg"), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("Cancel"));

                if (quit)
                {
                    var closer = DependencyService.Get<ICloseApp>();
                    if (closer != null)
                    {
                        App.EmpsLogged = null;
                        closer.CloseApp();
                    }
                }
            });
            return true;
        }
    }
}