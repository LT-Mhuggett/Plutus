using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Models;
using Plutus.Helpers;
using System.Reflection;

namespace Plutus
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class ConfigPage : ContentPage
	{
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
                Role = "0",
                LName = LName.Text,
                Password = Password.Text,
                PasswordConf = PasswordConf.Text
            };


            if (Emp.Password != Emp.PasswordConf) {
                DisplayAlert("OOPS!", "Passwords are not the same please try again", "OK");
                return;
            }

            Emp.Salt = Convert.ToBase64String(Helpers.Password.GenerateSalt());
            Emp.HashedPassword = Convert.ToBase64String(Helpers.Password.ComputeHash(Emp.Password, Convert.FromBase64String(Emp.Salt)));

            var FileC = new List<string>();
            FileC.Add("<Local>");
            FileC.Add("<Store>");
            FileC.Add("<StoreName>" + store.StoreName + "</StoreName>");
            FileC.Add("<StoreAbbr>" + store.StoreAbbr + "</StoreAbbr>");
            FileC.Add("</Store>");
            FileC.Add("<Manager>");
            FileC.Add("<FName>" + Emp.FName + "</FName>");
            FileC.Add("<LName>" + Emp.LName + "</LName>");
            FileC.Add("<Salt>" + Emp.Salt + "</Salt>");
            FileC.Add("<PasswordHash>" + Emp.HashedPassword + "</PasswordHash>");
            FileC.Add("</Manager>");
            FileC.Add("</Local>");

            FileIO.Save("App.config", FileC.ToArray());

            Application.Current.MainPage = new NavigationPage(new MainPage());
        }

        private void StoreName_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}