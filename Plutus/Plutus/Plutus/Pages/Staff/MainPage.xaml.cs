using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages.Staff
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class MainPage : ContentPage
	{
		public MainPage ()
		{
			InitializeComponent ();
		}

        private async void AddEmp_Clicked(object sender, EventArgs e)
        {
            if (Authorisation.IsAuthorised("StaffARU"))
            {
                await Navigation.PushAsync(new AddEmployeePage());
                return;
            }
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
        }

        private async void DeactivateEmp_Clicked(object sender, EventArgs e)
        {
            if (Authorisation.IsAuthorised("StaffARU"))
            {

                return;
            }
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
        }

        private async void EmpAccessRights_Clicked(object sender, EventArgs e)
        {
            if (Authorisation.IsAuthorised("StaffARU"))
            {

                return;
            }
            await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("AuthDeniedMesg"), App.Translate.ProvideValue("OK"));
        }
    }
}