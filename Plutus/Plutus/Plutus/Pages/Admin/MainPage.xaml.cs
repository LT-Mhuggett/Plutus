using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dropbox.Api;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Helpers;

namespace Plutus.Pages.Admin
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
		public MainPage ()
		{
			InitializeComponent ();
		}

        private void Backup_Clicked(object sender, EventArgs e)
        {

            //Authorisation.CheckAuthentication(VerifyId, MPage, EId, Confirm, "Report", "V", action);
        }

        private void CancelEmpCheck_Clicked(object sender, EventArgs e)
        {
            EId.Text = null;
            VerifyId.IsVisible = false;
            MPage.IsEnabled = true;
        }
    }
}