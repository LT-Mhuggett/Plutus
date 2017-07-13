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
using Plutus.Data;
using Xamarin.Forms.Maps;

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
            if (Password.Text != PasswordConf.Text||Password.Text.Length<=3)
            {
                DisplayAlert("OOPS!", "Passwords are not the same\nor are not long enough\nPlease try again", "OK");
                return;
            }

            var Salt = Convert.ToBase64String(Helpers.Password.GenerateSalt());
            var HashedPassword = Convert.ToBase64String(Helpers.Password.ComputeHash(Password.Text, Convert.FromBase64String(Salt)));

            var store = new StoreModel() {
                StoreName = StoreName.Text,
                StoreAbbr = StoreAbbr.Text,
                AdLine1 = AutoLayoutS.IsVisible ? null : StoreAdLine1.Text,
                AdLine2 = AutoLayoutS.IsVisible ? null : StoreAdLine2.Text,
                City = AutoLayoutS.IsVisible ? null : StoreCity.Text,
                Country = AutoLayoutS.IsVisible ? null : StoreCountry.Text,
                PostCode = AutoLayoutS.IsVisible ? null : StorePostCode.Text,
                FullAddress = AutoLayoutS.IsVisible ? StoreAddressPicker.SelectedItem.ToString() : null
            };

            var emp = new EmployeeModel()
            {
                FName = FName.Text,
                Role = "0",
                LName = LName.Text,
                AdLine1 = AutoLayoutP.IsVisible ? null : AdLine1.Text,
                AdLine2 = AutoLayoutP.IsVisible ? null : AdLine2.Text,
                City = AutoLayoutP.IsVisible ? null : City.Text,
                Country = AutoLayoutP.IsVisible ? null : Country.Text,
                PostCode = AutoLayoutP.IsVisible ? null : PostCode.Text,
                FullAddress = AutoLayoutP.IsVisible ? PersonAddressPicker.SelectedItem.ToString() : null,
                Email = Email.Text,
                Mobile = Mobile.Text,
                Salt = Salt,
                HashedPassword = HashedPassword
            };

            var fileC = new List<string>
            {
                "<Local>",
                "<Store>",
                "<StoreName>" + store.StoreName + "</StoreName>",
                "<StoreAbbr>" + store.StoreAbbr + "</StoreAbbr>",
                "</Store>",
                "<StoreOwner>",
                "<FName>" + emp.FName + "</FName>",
                "<LName>" + emp.LName + "</LName>",
                "<Salt>" + emp.Salt + "</Salt>",
                "<PasswordHash>" + emp.HashedPassword + "</PasswordHash>",
                "</StoreOwner>",
                "<Database>",
                "<Type>" + DatabasePicker.SelectedItem + "</Type>",
                "<TypeIndex>" + DatabasePicker.SelectedIndex + "</TypeIndex>",
                "</Database>",
                "</Local>"
            };

            FileIO.Save("App.config", fileC.ToArray());
            if (DatabasePicker.SelectedIndex == 0)
            {
                Database dbContext = new Database();
                dbContext.AddStore(store);
                emp.StoreIdFK = store.StoreId;
                dbContext.AddEmployee(emp);
                Application.Current.MainPage = new NavigationPage(new MainPage());
            }
        }
        
	    private async void AutoFillStore_OnClicked(object sender, EventArgs e)
	    {
	        List<string> addressList = await Location.ReverseGeocde();
	        if (addressList.Count == 0)
	        {
	            await DisplayAlert("OOPS!", "Something went wrong!", "OK");
	            return;
	        }
	        else
	        {
	            foreach (var item in addressList)
	                StoreAddressPicker.Items.Add(item);
	            ManLayoutS.IsVisible = !ManLayoutS.IsVisible;
	            AutoLayoutS.IsVisible = !AutoLayoutS.IsVisible;
	            StoreAddressPicker.Focus();
	        }
	    }

	    private async void AutoFillPerson_OnClicked(object sender, EventArgs e)
	    {
	        List<string> addressList = await Location.ReverseGeocde();
	        if (addressList.Count == 0)
	        {
	            await DisplayAlert("OOPS!", "Something went wrong!", "OK");
	            return;
	        }
	        else
	        {
	            foreach (var item in addressList)
	                PersonAddressPicker.Items.Add(item);
	            ManLayoutP.IsVisible = !ManLayoutP.IsVisible;
	            AutoLayoutP.IsVisible = !AutoLayoutP.IsVisible;
	            PersonAddressPicker.Focus();
	        }
	    }

        private void SEnterManually_OnClicked(object sender, EventArgs e)
	    {
	        ManLayoutS.IsVisible = !ManLayoutS.IsVisible;
	        AutoLayoutS.IsVisible = !AutoLayoutS.IsVisible;
            StoreAddressPicker.Items.Clear();
	        StoreAdLine1.Focus();
	    }

	    private void PEnterManually_OnClicked(object sender, EventArgs e)
	    {
	        ManLayoutP.IsVisible = !ManLayoutP.IsVisible;
	        AutoLayoutP.IsVisible = !AutoLayoutP.IsVisible;
            PersonAddressPicker.Items.Clear();
	        AdLine1.Focus();
	    }

	    
	}
}