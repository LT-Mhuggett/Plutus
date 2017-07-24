using Plutus.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Inventory
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class ItemDescPage : ContentPage
	{
		public ItemDescPage ()
		{
			InitializeComponent ();

            Desc.Text = AddItemPage.item.Desc;
		}

        private void Confirm_Clicked(object sender, EventArgs e)
        {
            AddItemPage.item.Desc = Desc.Text;
            Navigation.PopModalAsync();
        }
    }
}