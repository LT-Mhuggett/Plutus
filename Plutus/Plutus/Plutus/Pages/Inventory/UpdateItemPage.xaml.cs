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
	public partial class UpdateItemPage : ContentPage
	{
		public UpdateItemPage ()
		{
			InitializeComponent ();
		}

        private void SearchButton_Clicked(object sender, EventArgs e)
        {

        }
    }
}