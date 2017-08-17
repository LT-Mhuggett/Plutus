using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Helpers;

namespace Plutus.Pages.Staff
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class AddEmployeePage : ContentPage
	{
        public static EmployeeModel NewEmployee = new EmployeeModel();
		public AddEmployeePage ()
		{
			InitializeComponent ();
            NewEmployee.Actions = new List<AuthActions>();
		}

        private async void Ri_Re_Clicked(object sender, EventArgs e)
        {
            await Navigation.PushModalAsync(new NavigationPage(new RiRePage()));
        }

        private async void Create_Clicked(object sender, EventArgs e)
        {
            Loading.TogleLoading(LCV, LAI);
            if (string.IsNullOrEmpty(Password.Text) || string.IsNullOrEmpty(PasswordConf.Text))
            {
                Loading.TogleLoading(LCV, LAI);
                await DisplayAlert(App.Translate.ProvideValue("Oops"), App.Translate.ProvideValue("SetPassWMesg"), App.Translate.ProvideValue("OK"));
                return;
            }
            if (Password.Text != PasswordConf.Text || Password.Text.Length <= 6)
            {
                Loading.TogleLoading(LCV, LAI);
                await DisplayAlert(App.Translate.ProvideValue("Oops"), App.Translate.ProvideValue("PassWNotSameMesg"), App.Translate.ProvideValue("OK"));
                return;
            }

            var Salt = Convert.ToBase64String(Helpers.Password.GenerateSalt());
            var HashedPassword = Convert.ToBase64String(await Task.Run(() => Helpers.Password.ComputeHash(Password.Text, Convert.FromBase64String(Salt))));

            NewEmployee.FName = !String.IsNullOrEmpty(FName.Text) ? FName.Text : null;
            NewEmployee.NIN = /*Validate.IsNinValid(Nin.Text)?Nin.Text:null*/Nin.Text;
            NewEmployee.LName = !String.IsNullOrEmpty(LName.Text) ? LName.Text : null;
            NewEmployee.AdLine1 = !String.IsNullOrEmpty(AdLine1.Text) ? AdLine1.Text : null;
            NewEmployee.AdLine2 = AdLine2.Text;
            NewEmployee.City = !String.IsNullOrEmpty(City.Text) ? City.Text : null;
            NewEmployee.Country = !String.IsNullOrEmpty(Country.Text) ? Country.Text : null;
            NewEmployee.PostCode = Validate.IsPostCodeValid(PostCode.Text) ? PostCode.Text : null;
            NewEmployee.Email = Validate.IsEmailValid(Email.Text) ? Email.Text.ToLower() : null;
            NewEmployee.Mobile = Validate.IsPhoneNumberValid(Mobile.Text) ? Mobile.Text : null;
            NewEmployee.Salt = Salt;
            NewEmployee.HashedPassword = HashedPassword;
            NewEmployee.Store = App.Store;
            NewEmployee.ContractedHours = await Conversions.ToInterger(ContrHours.Text);
            NewEmployee.Wage = await Conversions.ToDecimal(Wage.Text);
            NewEmployee.Active = true;

            if (NewEmployee.FName == null || NewEmployee.LName == null || NewEmployee.NIN == null || NewEmployee.AdLine1 == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(0);
                return;
            }
            else if (NewEmployee.PostCode == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(2);
                return;
            }
            else if (NewEmployee.Email == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(1);
                return;
            }
            else if (NewEmployee.Mobile == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(3);
                return;
            }
            else if (NewEmployee.Wage == 0.0m)
            {
                Loading.TogleLoading(LCV, LAI);
                return;
            }
            else if (NewEmployee.ContractedHours < 0)
            {
                Loading.TogleLoading(LCV, LAI);
                return;
            }

            App.DbContext.Add(NewEmployee);
            App.DbContext.Save();
            Loading.TogleLoading(LCV, LAI);
            await Navigation.PopAsync();
        }

        private string Error(int tester)
        {
            var message = "";
            switch (tester)
            {
                case 0:
                    message = App.Translate.ProvideValue("FieldsFilledInMesg");
                    break;
                case 1:
                    message = App.Translate.ProvideValue("EmailNotCorrectMesg");
                    break;
                case 2:
                    message = App.Translate.ProvideValue("PCNotCorrectMesg");
                    break;
                case 3:
                    message = App.Translate.ProvideValue("PhoneNumNotCorrectMesg");
                    break;
                case 4:
                    message = App.Translate.ProvideValue("DatabaseNotSelectedMesg");
                    break;
                default:
                    break;
            }
            DisplayAlert(App.Translate.ProvideValue("Oops"), message, App.Translate.ProvideValue("OK"));
            return null;
        }
    }
}