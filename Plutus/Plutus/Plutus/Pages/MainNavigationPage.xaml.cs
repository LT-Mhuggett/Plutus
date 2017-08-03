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
        internal static ObservableCollection<EmployeeModel> empsLogged = new ObservableCollection<EmployeeModel>();
        internal static StoreModel store;
        TranslateExtension Translate = new TranslateExtension();

        public MainNavigationPage (EmployeeModel etemp, StoreModel stemp)
        {
            InitPage(etemp, stemp);
        }

        public MainNavigationPage(EmployeeModel etemp, StoreModel stemp, ObservableCollection<EmployeeModel>currentList)
        {
            empsLogged = currentList;
            InitPage(etemp, stemp);
        }

        internal void InitPage(EmployeeModel etemp, StoreModel stemp)
        {
            ToolbarItems.Add(new ToolbarItem
            {
                Text = Translate.ProvideValue("Users"),
                Icon = "",
                Command = new Command(this.ShowLoggedUsers)
            });

            empsLogged.Add(etemp);
            store = stemp;
            etemp = null;
            stemp = null;

            InitializeComponent();
            Title = $"Plutus - {store.StoreName}";

            Children.Add(new Inventory.InventoryMangPage());
        }

        private void ShowLoggedUsers(object obj)
        {
            Navigation.PushModalAsync(new NavigationPage(new UsersLoggedPage(empsLogged)));
        }

        protected override bool OnBackButtonPressed()
        {
            Device.BeginInvokeOnMainThread(async () =>
            {
                bool quit = await DisplayAlert(Translate.ProvideValue("Hmm"), Translate.ProvideValue("Quit?Mesg"), Translate.ProvideValue("Yes"), Translate.ProvideValue("Cancel"));

                if (quit)
                {
                    var closer = DependencyService.Get<ICloseApp>();
                    if (closer != null)
                    {
                        empsLogged = null;
                        closer.CloseApp();
                    }
                }
            });
            return true;
        }
    }
}