using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Database.Models;
using Plutus.Helpers.Extensions;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Plutus.Helpers;

namespace Plutus.Pages.Staff
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class AddEmployeePage : ContentPage
	{
        public EmployeeModel Emp = new EmployeeModel();
        public bool IsNew;

        /// <summary>
        /// Basic constructor AddEmployeePage
        /// </summary>
		public AddEmployeePage ()
		{
			InitializeComponent ();
            IsNew = true;

		    BindingContext = Emp;
		}

        public AddEmployeePage(EmployeeModel tempEmp)
        {
            InitializeComponent();

            Emp = tempEmp;
            IsNew = false;

            BindingContext = Emp;
            Create.Text = "Change Details";
        }

        /// <summary>
        /// Push the Rights and Restrictions page
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void Ri_Re_Clicked(object sender, EventArgs e)
        {
            if (Emp.Email != null)
            {
                await Navigation.PushModalAsync(new NavigationPage(new RiRePage(Emp)));
            }
        }

        /// <summary>
        /// Ensure all values are set and in boundaries
        /// then save the Employee model to the DB
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void Create_Clicked(object sender, EventArgs e)
        {
            MainView.TogleLoading(LCV, LAI);
            if (Password.IsVisible && PasswordConf.IsVisible)
            {
                if (string.IsNullOrEmpty(Password.Text) || string.IsNullOrEmpty(PasswordConf.Text))
                {
                    MainView.TogleLoading(LCV, LAI);
                    await DisplayAlert(App.Translate.ProvideValue("Oops"), App.Translate.ProvideValue("SetPassWMesg"), App.Translate.ProvideValue("OK"));
                    return;
                }
                if (Password.Text != PasswordConf.Text || Password.Text.Length <= 6)
                {
                    MainView.TogleLoading(LCV, LAI);
                    await DisplayAlert(App.Translate.ProvideValue("Oops"), App.Translate.ProvideValue("PassWNotSameMesg"), App.Translate.ProvideValue("OK"));
                    return;
                }

                var Salt = Convert.ToBase64String(Helpers.Password.GenerateSalt());
                var HashedPassword = Convert.ToBase64String(await Task.Run(() => Helpers.Password.ComputeHash(Password.Text, Convert.FromBase64String(Salt))));

                Emp.Salt = Salt;
                Emp.HashedPassword = HashedPassword;
            }

            Emp.FName = !String.IsNullOrEmpty(FName.Text) ? FName.Text : null;
            Emp.NIN = /*Validate.IsNinValid(Nin.Text)?Nin.Text:null*/Nin.Text;
            Emp.LName = !String.IsNullOrEmpty(LName.Text) ? LName.Text : null;
            if (ManAd.IsVisible)
            {
                Emp.AdLine1 = !String.IsNullOrEmpty(AdLine1.Text) ? AdLine1.Text : null;
                Emp.AdLine2 = AdLine2.Text;
                Emp.City = !String.IsNullOrEmpty(City.Text) ? City.Text : null;
                Emp.Country = !String.IsNullOrEmpty(Country.Text) ? Country.Text : null;
                Emp.PostCode = Validate.IsPostCodeValid(PostCode.Text) ? PostCode.Text : null;
            }
            else
            {
                Emp.FullAddress = !String.IsNullOrEmpty(FullAddress.Text) ? FullAddress.Text : null;
            }
            Emp.Email = Validate.IsEmailValid(Email.Text) ? Email.Text.ToLower() : null;
            Emp.Mobile = Validate.IsPhoneNumberValid(Mobile.Text) ? Mobile.Text : null;
            Emp.Store = App.Store;
            Emp.ContractedHours = (int)await ContrHours.Text.ToInterger(App.Translate.ProvideValue("ValueEnteredWrong"));
            Emp.Wage = (decimal)await Wage.Text.ToDecimal(App.Translate.ProvideValue("ValueEnteredWrong"));
            Emp.Active = true;

            if (Emp.FName == null || Emp.LName == null || Emp.NIN == null || Emp.AdLine1 == null && Emp.FullAddress == null)
            {
                MainView.TogleLoading(LCV, LAI);
                Error(0);
                return;
            }
            else if (Emp.PostCode == null)
            {
                MainView.TogleLoading(LCV, LAI);
                Error(2);
                return;
            }
            else if (Emp.Email == null)
            {
                MainView.TogleLoading(LCV, LAI);
                Error(1);
                return;
            }
            else if (Emp.Mobile == null)
            {
                MainView.TogleLoading(LCV, LAI);
                Error(3);
                return;
            }
            else if (Emp.Wage == -0.1m)
            {
                MainView.TogleLoading(LCV, LAI);
                return;
            }
            else if (Emp.ContractedHours < 0)
            {
                MainView.TogleLoading(LCV, LAI);
                return;
            }

            if (IsNew)
            {
                App.DbContext.Add(Emp);
                if (!App.DbContext.Save())
                {
                    await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                    MainView.TogleLoading(LCV, LAI);
                    return;
                }
            }
            MainView.TogleLoading(LCV, LAI);
            await Navigation.PopAsync();
        }

        /// <summary>
        /// This method handles Error messages
        /// </summary>
        /// <param name="tester">Value to be used to select message</param>
        /// <returns>null</returns>
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

        private void ToMan(object sender, EventArgs e)
        {
            AutoAd.IsVisible = false;
            ManAd.IsVisible = true;
        }

        protected override void OnDisappearing()
        {
            Emp = null;
            App.DbContext.RevertDbContextChanges();
            base.OnDisappearing();
        }
    }
}