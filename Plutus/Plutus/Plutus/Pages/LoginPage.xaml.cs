using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Plutus.Data;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class LoginPage : ContentPage
	{
		public LoginPage ()
		{
			InitializeComponent();
		}

        private void LoginButton_Clicked(object sender, EventArgs e)
        {
            var emp = Database.Login(UId.Text, PId.Text);
        }
    }
}