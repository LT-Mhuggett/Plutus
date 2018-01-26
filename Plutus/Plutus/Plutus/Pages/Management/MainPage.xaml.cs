using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Management
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
		public MainPage ()
		{
			InitializeComponent ();
		}

	    private void CancelEmpCheck_Clicked(object sender, EventArgs e)
	    {
	        EId.Text = null;
	        VerifyId.IsVisible = false;
	        MPage.IsEnabled = true;
	    }

        private void DisMgnt_Clicked(object sender, EventArgs e)
        {
            Action action = async () => await App.Current.MainPage.Navigation.PushAsync(new Discount.MainPage());
            Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Management", "X", action );
        }
    }
}