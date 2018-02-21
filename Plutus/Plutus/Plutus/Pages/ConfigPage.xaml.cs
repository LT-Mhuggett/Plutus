using Plutus.Helpers;
using Plutus.Models;
using Plutus.Helpers.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if __ANDROID__
#elif __IOS__
#else
using Windows.Storage;
#endif
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using FileIO = Plutus.Helpers.FileIO;

namespace Plutus.Pages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ConfigPage : ContentPage
    {
        /// <summary>
        /// Basic Constructor for the ConfigPage object
        /// </summary>
        public ConfigPage()
        {
            InitializeComponent();
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

            if (Email.Text != EmailConf.Text)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(1);
                return;
            }

            var Salt = Convert.ToBase64String(Helpers.Password.GenerateSalt());
            var HashedPassword = Convert.ToBase64String(await Task.Run(() =>
                Helpers.Password.ComputeHash(Password.Text, Convert.FromBase64String(Salt))));

            var store = new StoreModel()
            {
                StoreName = !String.IsNullOrEmpty(StoreName.Text) ? StoreName.Text : null,
                StoreAbbr = !String.IsNullOrEmpty(StoreAbbr.Text) ? StoreAbbr.Text : null,
                RecMarkup = !String.IsNullOrEmpty(StoreRM.Text)
                    ? (decimal?) await StoreRM.Text.ToInterger(App.Translate.ProvideValue("RecMarkupError")) / 100 + 1
                    : null,
                AdLine1 = AutoLayoutS.IsVisible
                    ? null
                    : !String.IsNullOrEmpty(StoreAdLine1.Text)
                        ? StoreAdLine1.Text
                        : null,
                AdLine2 = AutoLayoutS.IsVisible ? null : StoreAdLine2.Text,
                City = AutoLayoutS.IsVisible ? null : !String.IsNullOrEmpty(StoreCity.Text) ? StoreCity.Text : null,
                Country = AutoLayoutS.IsVisible
                    ? null
                    : !String.IsNullOrEmpty(StoreCountry.Text)
                        ? StoreCountry.Text
                        : null,
                PostCode = AutoLayoutS.IsVisible
                    ? null
                    : Validate.IsPostCodeValid(StorePostCode.Text)
                        ? StorePostCode.Text
                        : null,
                FullAddress = AutoLayoutS.IsVisible
                    ? StoreAddressPicker.SelectedIndex < 0
                        ? null
                        : StoreAddressPicker.SelectedItem.ToString()
                    : null
            };
            if (store.StoreName == null || store.StoreAbbr == null || store.RecMarkup == null ||
                store.FullAddress == null && store.AdLine1 == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(0);
                return;
            }
            if (store.PostCode == null && store.FullAddress == null)
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
                PostCode =
                    AutoLayoutP.IsVisible ? null : Validate.IsPostCodeValid(PostCode.Text) ? PostCode.Text : null,
                FullAddress =
                    AutoLayoutP.IsVisible
                        ? PersonAddressPicker.SelectedIndex < 0
                            ? null
                            : PersonAddressPicker.SelectedItem.ToString()
                        : null,
                Email = Validate.IsEmailValid(Email.Text) ? Email.Text.ToLower() : null,
                Mobile = Validate.IsPhoneNumberValid(Mobile.Text) ? Mobile.Text : null,
                Salt = Salt,
                HashedPassword = HashedPassword
            };
            if (emp.FName == null || emp.LName == null || emp.NIN == null ||
                emp.FullAddress == null && emp.AdLine1 == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(0);
                return;
            }
            if (emp.PostCode == null && emp.FullAddress == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(2);
                return;
            }
            if (emp.Email == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(1);
                return;
            }
            if (emp.Mobile == null)
            {
                Loading.TogleLoading(LCV, LAI);
                Error(3);
                return;
            }

            var fileC = new List<string>
            {
                "<Local>",
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

            if (DatabasePicker.SelectedIndex != 0) return;
            App.Store = store;
            App.DbContext.Init();
            App.DbContext.Add(store);
            emp.StoreId = store.Id;

            var tempList = App.DbContext.Get<AuthActions>().ToList();
            emp.EmpAuths = new List<Emp_AuthActions>();
            foreach (var item in tempList)
            {
                Emp_AuthActions temp =
                    new Emp_AuthActions() {Auth = item, A = true, M = true, R = true, V = true, X = true};
                emp.EmpAuths.Add(temp);
            }

            emp.Active = true;
            App.DbContext.Add(emp);
            if (!App.DbContext.Save())
            {
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), App.Translate.ProvideValue("DbIssue"),
                    App.Translate.ProvideValue("OK"));
                return;
            }
            Application.Current.MainPage = new NavigationPage(new MainNavigationPage(emp, store));
            Loading.TogleLoading(LCV, LAI);
        }

        /// <summary>
        /// This method is used as the Error message dealer
        /// </summary>
        /// <param name="tester">Value that is used to select the correct message</param>
        /// <returns></returns>
        private void Error(int tester)
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
            foreach (var item in addressList)
                PersonAddressPicker.Items.Add(item);
            ManLayoutP.IsVisible = !ManLayoutP.IsVisible;
            AutoLayoutP.IsVisible = !AutoLayoutP.IsVisible;
            PersonAddressPicker.Focus();
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

        private async void Restore_Clicked(object sender, EventArgs e)
        {
            App.DbContext = null;
            var TransfSucc = await FileIO.Restore();

            if (TransfSucc)
            {
                await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Success"),
                    string.Format(App.Translate.ProvideValue("DbBRSucc"), "Restored"),
                    App.Translate.ProvideValue("Cancel"));
                App.DbContext = new Database();

                var fileC = new List<string>
                {
                    "<Local>",
                    "<Database>",
                    "<Type>" + DatabasePicker.SelectedItem + "</Type>",
                    "<TypeIndex>" + DatabasePicker.SelectedIndex + "</TypeIndex>",
                    "</Database>",
                    "</Local>"
                };

                FileIO.Save("App.config", fileC.ToArray());
                App.Current.MainPage = new NavigationPage(new LoginPage());
                return;
            }
            await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"),
                string.Format(App.Translate.ProvideValue("DbBRFailed"), "Restoring"),
                App.Translate.ProvideValue("Cancel"));
        }

        private void SameAdButt_Clicked(object sender, EventArgs e)
        {
            AdLine1.Text = !String.IsNullOrEmpty(StoreAdLine1.Text) ? StoreAdLine1.Text : null;
            AdLine2.Text = StoreAdLine2.Text;
            City.Text = !String.IsNullOrEmpty(StoreCity.Text) ? StoreCity.Text : null;
            Country.Text = !String.IsNullOrEmpty(StoreCountry.Text) ? StoreCountry.Text : null;
            PostCode.Text = Validate.IsPostCodeValid(StorePostCode.Text) ? StorePostCode.Text : null;
            if (AutoLayoutS.IsVisible)
            {
                PersonAddressPicker.Items.Add(StoreAddressPicker.SelectedItem.ToString());
                PersonAddressPicker.SelectedIndex = 0;
                AutoLayoutP.IsVisible = true;
                ManLayoutP.IsVisible = false;
            }
            else
            {
                AdLine1.Text = !String.IsNullOrEmpty(StoreAdLine1.Text) ? StoreAdLine1.Text : null;
                AdLine2.Text = StoreAdLine2.Text;
                City.Text = !String.IsNullOrEmpty(StoreCity.Text) ? StoreCity.Text : null;
                Country.Text = !String.IsNullOrEmpty(StoreCountry.Text) ? StoreCountry.Text : null;
                PostCode.Text = Validate.IsPostCodeValid(StorePostCode.Text) ? StorePostCode.Text : null;
            }

        }
        private async void CTrans_Clicked(object sender, EventArgs e)
        {
#if __ANDROID__
            throw new NotImplementedException();
#elif __IOS__
            throw new NotImplementedException();
#else
            var folder = await FileIO.GetFolderAsync();

            Loading.TogleLoading(LCV, LAI);

            var folderList = await folder.GetFoldersAsync();

            var tempFolder = folderList[2];

            App.DbContext.Init();
            var store = new StoreModel();

            var taxes = new List<TaxModel>();
            var cat = new CategoryModel()
            {
                Name = "NOT EXIST",
                Description = "Created when migrating software and category was none existent"
            };
            var fileList = await tempFolder.GetFilesAsync();
            foreach (var file in fileList)
            {
                var fileData = await FileIO.GetStringsFromCsvAsync(file, '&');
                var extraParse = fileData.Select(x => x.Split('=')).ToArray();
                var name = file.Name.Replace(".dat", "");
                switch (name)
                {
                    case "Company":
                        store = new StoreModel()
                        {
                            StoreName = Uri.UnescapeDataString(extraParse.FirstOrDefault(x => x[0].Equals("Name"))?[1]),
                            StoreAbbr = Uri.UnescapeDataString(extraParse.FirstOrDefault(x => x[0].Equals("Name"))?[1])
                                .Trim().Remove(4,
                                    Uri.UnescapeDataString(extraParse.FirstOrDefault(x => x[0].Equals("Name"))?[1])
                                        .Length-4),
                            FullAddress = Uri.UnescapeDataString(
                                extraParse.FirstOrDefault(x => x[0].Equals("Address"))?[1] == ""
                                    ? await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                                        App.Translate.ProvideValue("FullAddress"),
                                        App.Translate.ProvideValue("EnterCorrectValue"),
                                        App.Translate.ProvideValue("Confirm"),
                                        App.Translate.ProvideValue("EmailNotCorrectMesg"),
                                        false)
                                    : Uri.UnescapeDataString(
                                        extraParse.FirstOrDefault(x => x[0].Equals("Address"))?[1]))
                        };
                        App.DbContext.Add(store);
                        break;

                    case "Tax":
                        for (var i = 1; i < fileData.Count() - 3; i += 2)
                        {
                            var tax = new TaxModel()
                            {
                                Name = Uri.UnescapeDataString(extraParse[i][1]),
                                Rate = (await extraParse[i + 1][1].ToDouble("Error") ?? default(double))/100+1
                            };
                            taxes.Add(tax);
                            App.DbContext.Add(tax);
                        }
                        break;
                }
            }

            tempFolder = folderList[0];
            fileList = await tempFolder.GetFilesAsync();
            foreach (var file in fileList)
            {
                var fileData = await FileIO.GetStringsFromCsvAsync(file, '&');
                var extraParse = fileData.Select(x => x.Split('=')).ToArray();
                var id = file.Name.Replace(".dat", "");
                if (!id.IsNumeric())
                {
                    var itemBool =
 await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("IsItem"), Uri.UnescapeDataString(id), Uri.UnescapeDataString(extraParse.FirstOrDefault(x => x[0].Equals("Description"))?[1])), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("No"));

                    if (!itemBool) continue;
                }
                var value =
                    // ReSharper disable once PossibleNullReferenceException
                    await extraParse.FirstOrDefault(x => x[0].Equals("Value"))?[1]?.ToDecimal("Error") ??
                    default(decimal);
                var taxType =
                    // ReSharper disable once PossibleNullReferenceException
                    await extraParse.FirstOrDefault(x => x[0].Equals("TaxRate"))?[1]?.ToInterger("Error") ??
                    default(int);
                if (taxType == 0 || taxType == 1)
                    taxType = 2;
                else if (taxType == 2)
                    taxType = 0;
                else if (taxType == 3)
                    taxType = 2;
                var item = new ItemModel()
                {
                    Id = id,
                    Name = Uri.UnescapeDataString(extraParse.FirstOrDefault(x => x[0].Equals("Description"))?[1]),
                    Vat = taxes[taxType],
                    Brand = "NOT EXIST",
                    Cat = cat,
                    Cost = 0.00m,
                    /*
                     * if price is excluding vat
                     *
                       ExPrice = value / 100,
                       Price = value / 100 * (decimal) taxes[taxType - 1].Rate                    
                     * if price is including vat
                     */
                    ExPrice = value/100 / (decimal) taxes[taxType].Rate,
                    Price = value/100
                    
                };
                App.DbContext.Add(item);
            }

            tempFolder = folderList[4];
            fileList = await tempFolder.GetFilesAsync();
            foreach (var file in fileList)
            {
                var fileData = await FileIO.GetStringsFromCsvAsync(file, '&');
                var extraParse = fileData.Select(x => x.Split('=')).ToArray();
                var salt = Convert.ToBase64String(Helpers.Password.GenerateSalt());

                var empBool =
 await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("AddEmp_check"), extraParse.FirstOrDefault(x => x[0].Equals("LastName"))?[1], extraParse.FirstOrDefault(x => x[0].Equals("FirstName"))?[1]), App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("No"));

                if (!empBool) continue;

                var emp = new EmployeeModel()
                {
                    FName = extraParse.FirstOrDefault(x => x[0].Equals("FirstName"))?[1],
                    LName = extraParse.FirstOrDefault(x => x[0].Equals("LastName"))?[1],
                    Email = extraParse.FirstOrDefault(x => x[0].Equals("Email"))?[1] == ""
                        ? await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                            App.Translate.ProvideValue("Email"), App.Translate.ProvideValue("EnterCorrectValue"),
                            App.Translate.ProvideValue("Confirm"), App.Translate.ProvideValue("EmailNotCorrectMesg"),
                            false)
                        : extraParse.FirstOrDefault(x => x[0].Equals("Email"))?[1],
                    Salt = salt,
                    HashedPassword = Convert.ToBase64String(Helpers.Password.ComputeHash(
                        await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                            App.Translate.ProvideValue("PassW"), App.Translate.ProvideValue("EnterCorrectValue"),
                            App.Translate.ProvideValue("Confirm"), "Not Valid", true),
                        Convert.FromBase64String(salt))),
                    Store = store
                };

                var tempList = App.DbContext.Get<AuthActions>().ToList();
                emp.EmpAuths = new List<Emp_AuthActions>();
                foreach (var item in tempList)
                {
                    Emp_AuthActions temp =
                        new Emp_AuthActions() { Auth = item, A = true, M = true, R = true, V = true, X = true };
                    emp.EmpAuths.Add(temp);
                }

                emp.Active = true;

                App.DbContext.Add(emp);
            }
            App.DbContext.Save();
            var fileC = new List<string>
            {
                "<Local>",
                "<Database>",
                "<Type>Local Database</Type>",
                "<TypeIndex>1</TypeIndex>",
                "</Database>",
                "</Local>"
            };
            FileIO.Save("App.config", fileC.ToArray());
            Application.Current.MainPage = new NavigationPage(new LoginPage());
            Loading.TogleLoading(LCV, LAI);
#endif
        }
    }
}