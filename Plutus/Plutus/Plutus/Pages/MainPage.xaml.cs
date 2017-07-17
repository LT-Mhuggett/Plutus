using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Plutus.Models;

namespace Plutus.Pages
{
	public partial class MainPage : ContentPage
	{
		public MainPage(EmployeeModel etemp, StoreModel stemp)
		{
            ToolbarItems.Add(new ToolbarItem { Text = "Users", Icon = "", });

            List<EmployeeModel> empsLogedin = new List<EmployeeModel>();
            empsLogedin.Add(etemp);
            StoreModel store = stemp;
            etemp = null;
            stemp = null;

            InitializeComponent();
		}
	}
}
