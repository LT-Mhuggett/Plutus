using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Models;
using Plutus.Helpers;

namespace Plutus
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class ConfigPage : ContentPage
	{
        private byte[] salt;
        private byte[] hash;
		public ConfigPage ()
		{
			InitializeComponent();
		}

        private void Create_Clicked(object sender, EventArgs e)
        {
            StoreModel store = new StoreModel() {
                StoreName = StoreName.Text,
                StoreAbbr = StoreAbbr.Text
            };

            EmployeeModel Emp = new EmployeeModel()
            {
                FName = FName.Text,
                LName = LName.Text,
                Password = Password.Text,
                PasswordConf = PasswordConf.Text
            };


            if (Emp.Password != Emp.PasswordConf) {
                DisplayAlert("OOPS!", "Passwords are not the same please try again", "OK");
                return;
            }
            salt = Helpers.Password.GenerateSalt();
            hash = Helpers.Password.ComputeHash(Emp.Password, salt);

            Emp.salt = Convert.ToBase64String(salt);
            Emp.HashedPassword = Convert.ToBase64String(hash);
        }
    }
}