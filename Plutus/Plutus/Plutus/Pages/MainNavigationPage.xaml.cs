using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainNavigationPage : TabbedPage
    {
        internal static List<EmployeeModel> empsLogged = new List<EmployeeModel>();
        internal static StoreModel store;
        public MainNavigationPage (EmployeeModel etemp, StoreModel stemp)
        {
            ToolbarItems.Add(new ToolbarItem { Text = "Users", Icon = "", });
            
            empsLogged.Add(etemp);
            store = stemp;
            etemp = null;
            stemp = null;

            InitializeComponent();
            Title = $"Plutus - {store.StoreName}";

            Children.Add(new Inventory.InventoryMangPage());
        }
    }
}