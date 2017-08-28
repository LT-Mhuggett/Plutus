using Plutus.Helpers;
using Plutus.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Plutus.Pages
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class ConfigPage : ContentPage
	{
        /// <summary>
        /// Basic Constructor for the ConfigPage object
        /// </summary>
		public ConfigPage ()
		{
			InitializeComponent ();
		}

        /// <summary>
        /// This method checks all user inputs and then calls methods to encrypt passwords
        /// all values if correct and then placed inside a object of the model required.
        /// then an App.Config file is created with the baskic required information on launch, the
        /// database is then created and the values are inputted.
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void Create_Clicked(object sender, EventArgs e)
        {
            Loading.TogleLoading(LCV, LAI);
            if (string.IsNullOrEmpty(Password.Text) || string.IsNullOrEmpty(PasswordConf.Text))
            {
                Loading.TogleLoading(LCV, LAI);
                Error(6);
                return;
            }
            if (Password.Text != PasswordConf.Text || Password.Text.Length <= 6)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(7);
                return;
            }

            var Salt = Convert.ToBase64String(Helpers.Password.GenerateSalt());
            var HashedPassword = Convert.ToBase64String(await Task.Run(() => Helpers.Password.ComputeHash(Password.Text, Convert.FromBase64String(Salt))));

            var store = new StoreModel()
            {
                StoreName = !String.IsNullOrEmpty(StoreName.Text) ? StoreName.Text : null,
                StoreAbbr = !String.IsNullOrEmpty(StoreAbbr.Text) ? StoreAbbr.Text : null,
                AdLine1 = AutoLayoutS.IsVisible ? null : !String.IsNullOrEmpty(StoreAdLine1.Text) ? StoreAdLine1.Text : null,
                AdLine2 = AutoLayoutS.IsVisible ? null : StoreAdLine2.Text,
                City = AutoLayoutS.IsVisible ? null : !String.IsNullOrEmpty(StoreCity.Text) ? StoreCity.Text : null,
                Country = AutoLayoutS.IsVisible ? null : !String.IsNullOrEmpty(StoreCountry.Text) ? StoreCountry.Text : null,
                PostCode = AutoLayoutS.IsVisible ? null : Validate.IsPostCodeValid(StorePostCode.Text) ? StorePostCode.Text : null,
                FullAddress = AutoLayoutS.IsVisible ? StoreAddressPicker.SelectedIndex < 0 ? null : StoreAddressPicker.SelectedItem.ToString() : null
            };
            if (store.StoreName == null || store.StoreAbbr == null || store.FullAddress == null && store.AdLine1 == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(0);
                return;
            }
            else if (store.PostCode == null && store.FullAddress == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(2);
                return;
            }

            var emp = new EmployeeModel()
            {
                FName = !String.IsNullOrEmpty(FName.Text) ? FName.Text : null,
                NIN = /*Validate.IsNinValid(Nin.Text)?Nin.Text:null*/Nin.Text,
                LName = !String.IsNullOrEmpty(LName.Text) ? LName.Text : null,
                AdLine1 = AutoLayoutP.IsVisible ? null : !String.IsNullOrEmpty(AdLine1.Text) ? AdLine1.Text : null,
                AdLine2 = AutoLayoutP.IsVisible ? null : AdLine2.Text,
                City = AutoLayoutP.IsVisible ? null : !String.IsNullOrEmpty(City.Text) ? City.Text : null,
                Country = AutoLayoutP.IsVisible ? null : !String.IsNullOrEmpty(Country.Text) ? Country.Text : null,
                PostCode = AutoLayoutP.IsVisible ? null : Validate.IsPostCodeValid(PostCode.Text) ? PostCode.Text : null,
                FullAddress = AutoLayoutP.IsVisible ? PersonAddressPicker.SelectedIndex < 0 ? null : PersonAddressPicker.SelectedItem.ToString() : null,
                Email = Validate.IsEmailValid(Email.Text) ? Email.Text.ToLower() : null,
                Mobile = Validate.IsPhoneNumberValid(Mobile.Text) ? Mobile.Text : null,
                Salt = Salt,
                HashedPassword = HashedPassword
            };
            if (emp.FName == null || emp.LName == null || emp.NIN == null || emp.FullAddress == null && emp.AdLine1 == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(0);
                return;
            }
            else if (emp.PostCode == null && emp.FullAddress == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(2);
                return;
            }
            else if (emp.Email == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(1);
                return;
            }
            else if (emp.Mobile == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(3);
                return;
            }

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
            if (DatabasePicker.SelectedIndex < 0)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(4);
                return;
            }
            if (DatabasePicker.SelectedIndex == 0)
            {
                App.DbContext.Init();
                App.DbContext.Add(store);
                emp.StoreId = store.StoreId;
                var Actions = App.DbContext.GetAllActions();
                emp.Actions = new List<AuthActions>();
                foreach (var item in Actions)
                {
                    emp.Actions.Add(item);
                }
                emp.Active = true;
                App.DbContext.Add(emp);
                if (!await App.DbContext.Save())
                {
                    await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"), App.Translate.ProvideValue("OK"));
                    return;
                }
                Application.Current.MainPage = new NavigationPage(new MainNavigationPage(emp, store));
                Loading.TogleLoading(LCV, LAI);
            }
        }

        /// <summary>
        /// This method is used as the Error message dealer
        /// </summary>
        /// <param name="tester">Value that is used to select the correct message</param>
        /// <returns></returns>
        public void Error(int tester)
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
                case 5:
                    message = App.Translate.ProvideValue("SomthingWentWrongMesg");
                    break;
                case 6:
                    message = App.Translate.ProvideValue("SetPassWMesg");
                    break;
                case 7:
                    message = App.Translate.ProvideValue("PassWNotSameMesg");
                    break;
                default:
                    break;
            }
            DisplayAlert(App.Translate.ProvideValue("Oops"), message, App.Translate.ProvideValue("OK"));
        }

        /// <summary>
        /// This gets possible addresses based on geolocation services on the device
        /// if there are possible addresses the manual input fields are hidden and the auto address is shown
        /// and focused.
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void AutoFillStore_OnClicked(object sender, EventArgs e)
        {
            List<string> addressList = await Location.ReverseGeocde();
            if (addressList.Count == 0)
            {
                Error(5);
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

        /// <summary>
        /// This gets possible addresses based on geolocation services on the device
        /// if there are possible addresses the manual input fields are hidden and the auto address is shown
        /// and focused.
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private async void AutoFillPerson_OnClicked(object sender, EventArgs e)
        {
            List<string> addressList = await Location.ReverseGeocde();
            if (addressList.Count == 0)
            {
                Error(5);
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

        /// <summary>
        /// This hides the auto layour and shows the manual layout
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void SEnterManually_OnClicked(object sender, EventArgs e)
        {
            ManLayoutS.IsVisible = !ManLayoutS.IsVisible;
            AutoLayoutS.IsVisible = !AutoLayoutS.IsVisible;
            StoreAddressPicker.Items.Clear();
            StoreAdLine1.Focus();
        }

        /// <summary>
        /// This hides the auto layour and shows the manual layout
        /// </summary>
        /// <param name="sender">Object that sent called the method</param>
        /// <param name="e">Event that the object called</param>
        private void PEnterManually_OnClicked(object sender, EventArgs e)
        {
            ManLayoutP.IsVisible = !ManLayoutP.IsVisible;
            AutoLayoutP.IsVisible = !AutoLayoutP.IsVisible;
            PersonAddressPicker.Items.Clear();
            AdLine1.Focus();
        }
    }
}