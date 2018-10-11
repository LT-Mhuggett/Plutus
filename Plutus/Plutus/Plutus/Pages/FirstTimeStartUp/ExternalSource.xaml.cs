using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plutus.Helpers;
using Plutus.Helpers.Extensions;
using Database.Models;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using FileIO = Plutus.Helpers.FileIO;
#if __ANDROID__ || __IOS__

#else
using Windows.Storage;
#endif

namespace Plutus.Pages.FirstTimeStartUp
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ExternalSource : ContentPage
    {
        public ExternalSource()
        {
            InitializeComponent();
        }

        private async void CTrans_Clicked(object sender, EventArgs e)
        {
#if __ANDROID__
            throw new NotImplementedException();
#elif __IOS__
            throw new NotImplementedException();
#else
            App.AppSettings.DatabaseProvider = "Sqlite";
            App.DbContext = new Helpers.Database(App.AppSettings.DatabaseProvider);

            var folder = await FileIO.GetFolderAsync();

            var itemIssues = new List<StorageFile>();

            MainView.TogleLoading(LCV, LAI);

            var folderList = await folder.GetFoldersAsync();

            var tempFolder = folderList[2];

            App.DbContext.Init(false);
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
                var fileName = file.Name.Replace(".dat", "");
                switch (fileName)
                {
                    case "Company":
                        Tuple<string, string, Type, string, bool, bool>[] viewElementsFullAddress = {
                            Tuple.Create(App.Translate.ProvideValue("EnterCorrectValue"), "Enter Here", typeof(string),
                            "Enter valid value", false, true)
                        };
                        store = new StoreModel()
                        {
                            StoreName = Uri.UnescapeDataString(extraParse.FirstOrDefault(x => x[0].Equals("Name"))?[1]),
                            StoreAbbr = Uri.UnescapeDataString(extraParse.FirstOrDefault(x => x[0].Equals("Name"))?[1])
                                .Trim().Remove(4,
                                    Uri.UnescapeDataString(extraParse.FirstOrDefault(x => x[0].Equals("Name"))?[1])
                                        .Length - 4),
                            FullAddress = Uri.UnescapeDataString(
                                extraParse.FirstOrDefault(x => x[0].Equals("Address"))?[1] == ""
                                    ? (await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                                        App.Translate.ProvideValue("FullAddress"),
                                        viewElementsFullAddress,
                                        App.Translate.ProvideValue("Confirm"))).First() as string
                                    : Uri.UnescapeDataString(
                                        extraParse.FirstOrDefault(x => x[0].Equals("Address"))?[1]))
                        };
                        App.DbContext.Add(store);
                        break;

                    case "Tax":
                        for (var i = 1; i < fileData.Count() - 3; i += 2)
                        {
                            var rate = (await extraParse[i + 1][1].ToDouble("Error") ?? default(double));
                            Tuple<string, string, Type, string, bool, bool>[] viewElementsTax = {
                                Tuple.Create("Tax Name", "Enter Here", typeof(string),
                                App.Translate.ProvideValue("TaxNotCorrectMesg"), false, true)
                            };
                            var name = (await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                                string.Format(App.Translate.ProvideValue("TaxName"), rate),
                                viewElementsTax,
                                App.Translate.ProvideValue("Confirm"))).First() as string;

                            var tax = new TaxModel()
                            {
                                Name = name,
                                Rate = rate / 100 + 1
                            };
                            taxes.Add(tax);
                            App.DbContext.Add(tax);
                        }

                        break;
                }
            }

            tempFolder = folderList[0];
            fileList = await tempFolder.GetFilesAsync();
            //var itemsToCheck = new List<StorageFile>();
            foreach (var file in fileList)
            {
                var fileData = await FileIO.GetStringsFromCsvAsync(file, '&');
                var extraParse = fileData.Select(x => x.Split('=')).ToArray();
                var id = file.Name.Replace(".dat", "");
                /*
                if (!id.IsNumeric())
                {
                    itemsToCheck.Add(file);
                }
                else
                {*/
                try
                {
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
                        Name = Uri.UnescapeDataString(
                            extraParse.FirstOrDefault(x => x[0].Equals("Description"))?[1]),
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
                        ExPrice = Math.Round(value / 100 / (decimal)taxes[taxType].Rate, 2,
                            MidpointRounding.AwayFromZero),
                        Price = Math.Round(value / 100, 2, MidpointRounding.AwayFromZero)

                    };
                    App.DbContext.Add(item);
                }
                catch (Exception)
                {
                    itemIssues.Add(file);
                }
            }
            //}

            tempFolder = folderList[4];
            fileList = await tempFolder.GetFilesAsync();
            foreach (var file in fileList)
            {
                var fileData = await FileIO.GetStringsFromCsvAsync(file, '&');
                var extraParse = fileData.Select(x => x.Split('=')).ToArray();
                var salt = Convert.ToBase64String(Password.GenerateSalt());

                var empBool =
                    await DisplayAlert(App.Translate.ProvideValue("Hmm"),
                        string.Format(App.Translate.ProvideValue("AddEmp_check"),
                            extraParse.FirstOrDefault(x => x[0].Equals("LastName"))?[1],
                            extraParse.FirstOrDefault(x => x[0].Equals("FirstName"))?[1]),
                        App.Translate.ProvideValue("Yes"), App.Translate.ProvideValue("No"));

                if (!empBool) continue;

                Tuple<string, string, Type, string, bool, bool>[] viewElementsEmail = {
                    Tuple.Create(App.Translate.ProvideValue("Email"), "Enter Here",
                        typeof(string), App.Translate.ProvideValue("EmailNotCorrectMesg"), false, true)
                };

                Tuple<string, string, Type, string, bool, bool>[] viewElementsPass = {
                    Tuple.Create("Password", "Enter Here", typeof(string), "", true, true),
                    Tuple.Create("Confirm Password", "Enter Confirmation Here", typeof(string), "", true, true),
                };

                var emp = new EmployeeModel()
                {
                    FName = extraParse.FirstOrDefault(x => x[0].Equals("FirstName"))?[1],
                    LName = extraParse.FirstOrDefault(x => x[0].Equals("LastName"))?[1],
                    Email = extraParse.FirstOrDefault(x => x[0].Equals("Email"))?[1] == ""
                        ? ((await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                            App.Translate.ProvideValue("Email"), viewElementsEmail,
                            App.Translate.ProvideValue("Confirm"))).First() as string).ToLower()
                        : extraParse.FirstOrDefault(x => x[0].Equals("Email"))?[1].ToLower(),
                    Salt = salt,
                    HashedPassword = Convert.ToBase64String(Password.ComputeHash(
                        (await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                            App.Translate.ProvideValue("PassW"), viewElementsPass,
                            App.Translate.ProvideValue("Confirm"))).First() as string,
                        Convert.FromBase64String(salt))),
                    Store = store
                };

                var tempList = App.DbContext.Get<AuthActions>().ToList();
                emp.EmpAuths = new List<Emp_AuthActions>();
                foreach (var item in tempList)
                {
                    var temp =
                        new Emp_AuthActions() { Auth = item, A = true, M = true, R = true, V = true, X = true };
                    emp.EmpAuths.Add(temp);
                }

                emp.Active = true;

                App.DbContext.Add(emp);
            }

            App.DbContext.Save();
            App.DbContext.DetachAllEntities();
            App.DbContext = new Helpers.Database(App.AppSettings.DatabaseProvider);

            if (itemIssues.Count > 0)
            {
                var itemsText = "";
                foreach (var item in itemIssues)
                {
                    itemsText += $"  \u2022 {item.DisplayName}\n";
                }
                await DisplayAlert(App.Translate.ProvideValue("Hmm"), String.Format(App.Translate.ProvideValue("FailedToAddItem"), itemsText), App.Translate.ProvideValue("OK"));
            }

            var printerMgr = new PosPrinterManager();
            var printerList = await printerMgr.GetPrinterList();

            if (printerList.Count > 0)
            {
                var result = await App.Current.MainPage.DisplayActionSheet(App.Translate.ProvideValue("PrinterList_"), App.Translate.ProvideValue("Cancel"), null, printerList.Keys.ToArray());
                if (result != App.Translate.ProvideValue("Cancel"))
                {
                    App.AppSettings.PrinterLogicalName = result;
                }
                else
                {
                    await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Warning"), App.Translate.ProvideValue("NoPrinter"), App.Translate.ProvideValue("Cancel"));
                }
            }
            else
            {
                await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Warning"), App.Translate.ProvideValue("NoPrinter"), App.Translate.ProvideValue("Cancel"));
            }

            Application.Current.MainPage = new NavigationPage(new LoginPage());
            MainView.TogleLoading(LCV, LAI);
#endif
        }
    }
}