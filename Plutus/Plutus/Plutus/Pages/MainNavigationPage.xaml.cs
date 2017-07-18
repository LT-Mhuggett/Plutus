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
        public MainNavigationPage (EmployeeModel etemp, StoreModel stemp)
        {
            ToolbarItems.Add(new ToolbarItem { Text = "Users", Icon = "", });

            List<EmployeeModel> empsLogedin = new List<EmployeeModel>();
            empsLogedin.Add(etemp);
            StoreModel store = stemp;
            etemp = null;
            stemp = null;

            InitializeComponent();

            Children.Add(new Inventory.InventoryMangPage());
        }
    }
}