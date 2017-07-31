using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Collections.ObjectModel;

namespace Plutus.Pages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainNavigationPage : TabbedPage
    {
        internal static ObservableCollection<EmployeeModel> empsLogged = new ObservableCollection<EmployeeModel>();
        internal static StoreModel store;

        public MainNavigationPage (EmployeeModel etemp, StoreModel stemp)
        {
            ToolbarItems.Add(new ToolbarItem {
                Text = "Users",
                Icon = "",
                Command =new Command(this.ShowLoggedUsers)
            });

            empsLogged.Add(etemp);
            store = stemp;
            etemp = null;
            stemp = null;

            InitializeComponent();
            Title = $"Plutus - {store.StoreName}";

            Children.Add(new Inventory.InventoryMangPage());
        }

        public MainNavigationPage(EmployeeModel etemp, StoreModel stemp, ObservableCollection<EmployeeModel>currentList)
        {
            ToolbarItems.Add(new ToolbarItem
            {
                Text = "Users",
                Icon = "",
                Command = new Command(this.ShowLoggedUsers)
            });

            empsLogged = currentList;

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
            Navigation.PushModalAsync(new UsersLoggedPage(empsLogged));
        }
    }
}