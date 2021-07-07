using Database.Models;
using NatApp.Plutus.UWP.Implementations.Services;
using NatApp.Plutus.Helpers.FileIO;
using NatApp.Plutus.Helpers.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Xamarin.Forms;
using NatApp.Plutus.Helpers.Validators;
using System.Diagnostics;
using NatApp.Plutus.Services.ThirdPartyTransfer;
using NatApp.Plutus.Helpers.CustomViews;

[assembly: Dependency(typeof(CopperTransferUWP))]
namespace NatApp.Plutus.UWP.Implementations.Services
{
    class CopperTransferUWP : ICopperTransfer
    {
        /// <summary>
        /// Get all folders of interest
        /// </summary>
        /// <remarks>
        /// Order is Misc, Items, Staff
        /// </remarks>
        /// <param name="sFolder">Folder to look in</param>
        /// <returns>Folders of interest</returns>
        public async Task<Tuple<Queue<object>,int>> GetFoldersForProcessing(object sFolder)
        {
            StorageFolder folder = (StorageFolder)sFolder;

            var folders = await folder.GetFoldersAsync();

            Queue<object> folderQueue = new Queue<object>();

            var tempfolder = folders.First(f => f.DisplayName.Equals("Misc"));
            folderQueue.Enqueue(tempfolder);

            tempfolder = folders.First(f => f.DisplayName.Equals("Items"));
            folderQueue.Enqueue(tempfolder);

            tempfolder = folders.First(f => f.DisplayName.Equals("Staff"));
            folderQueue.Enqueue(tempfolder);

            return Tuple.Create(folderQueue, (await tempfolder.GetFilesAsync()).Count());
        }

        /// <summary>
        /// Perform data extraction
        /// </summary>
        /// <param name="file">file to extract data from</param>
        /// <returns>Data and number of records</returns>
        public async Task<Tuple<string[][], int>> ExecuteParsingAsync(object file)
        {
            var fileData = await ParsingCSV.GetDataFromCSV(await (file as StorageFile).OpenStreamForReadAsync(), '&');
            var extraParse = fileData.Select(x => x.Split('=')).ToArray();
            return Tuple.Create(extraParse, fileData.Count());
        }

        /// <summary>
        /// Get all items
        /// </summary>
        /// <param name="sFolder">Items folder</param>
        /// <param name="taxes">list of tax types</param>
        /// <returns>items associated with their tax bracket</returns>
        public async Task<Tuple<Queue<ItemModel>, Queue<string>>> GetItemsAsync(object sFolder, List<TaxModel> taxes)
        {
            StorageFolder folder = (StorageFolder)sFolder;
            var itemFiles = await folder.GetFilesAsync();
            var itemQueue = new Queue<ItemModel>();
            var itemIssues = new Queue<string>();
#if DEBUG
            var debugItemsListNonConversion = new List<Tuple<string, string, int, decimal>>();
#endif

            var cat = new CategoryModel()
            {
                Name = "NOT EXIST",
                Description = "Created when migrating software and category was none existent"
            };

            foreach (var itemFile in itemFiles)
            {
                var completeParsing = await ExecuteParsingAsync(itemFile);
                var id = itemFile.DisplayName;
                try
                {
                    if (!id.IsNumeric())
                    {
                        itemIssues.Enqueue(id);
                        continue;
                    }


                    var value = Convert.ToDecimal(completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Value"))?[1]);
                    var taxType = Convert.ToInt32(completeParsing.Item1.FirstOrDefault(x => x[0].Equals("TaxRate"))?[1]);

#if DEBUG
                    debugItemsListNonConversion.Add(
                        Tuple.Create(
                            id, 
                            Uri.UnescapeDataString(completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Description"))?[1]),
                            taxType,
                            value));
#endif

                    if (taxType == 0 || taxType == 1 || taxType == 3)
                        taxType = 2;
                    else if (taxType == 2)
                        taxType = 0;
                    var item = new ItemModel()
                    {
                        Id = id,
                        Name = Uri.UnescapeDataString(completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Description"))?[1]),
                        Vat = taxes[taxType],
                        Cat = cat,
                        Cost = 0.0m,
                        ExPrice = Math.Round(value / 100 / (decimal)taxes[taxType].Rate, 2, MidpointRounding.AwayFromZero),
                        Price = Math.Round(value / 100, 2, MidpointRounding.AwayFromZero)
                    };
                    itemQueue.Enqueue(item);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    itemIssues.Enqueue(itemFile.DisplayName);
                }
            }
#if DEBUG
            var debugFolder = ApplicationData.Current.LocalFolder;
            var debugFile = await debugFolder.CreateFileAsync("dumpForItemsAfterConversion.txt", CreationCollisionOption.GenerateUniqueName);
            await FileIO.WriteTextAsync(debugFile, Newtonsoft.Json.JsonConvert.SerializeObject(itemQueue));
            var debugFile2 = await debugFolder.CreateFileAsync("dumpForItemsNonConversion.txt", CreationCollisionOption.GenerateUniqueName);
            await FileIO.WriteTextAsync(debugFile2, Newtonsoft.Json.JsonConvert.SerializeObject(debugItemsListNonConversion));
#endif
            return Tuple.Create(itemQueue, itemIssues);
        }

        /// <summary>
        /// Get Store and Tax types
        /// </summary>
        /// <param name="sFolder">Misc folder</param>
        /// <returns>Store and Tax types</returns>
        public async Task<Tuple<StoreModel, List<TaxModel>>> GetStoreAndTaxDataAsync(object sFolder)
        {
            StorageFolder folder = (StorageFolder)sFolder;
            var miscFiles = await folder.GetFilesAsync();
            StoreModel store = new StoreModel();
            List<TaxModel> taxes = new List<TaxModel>();

            foreach (var miscFile in miscFiles)
            {
                var fileName = miscFile.DisplayName;
                switch (fileName)
                {
                    case "Company":
                        store  = await GetStoreAsync(miscFile);
                        break;
                    case "Tax":
                        taxes = await GetTaxDataAsync(miscFile);
                        break;
                }
            }
            return Tuple.Create(store, taxes);
        }

        /// <summary>
        /// Get Store data
        /// </summary>
        /// <param name="storeFile">Store file</param>
        /// <returns>Store</returns>
        public async Task<StoreModel> GetStoreAsync(object storeFile)
        {
            var completeParsing = await ExecuteParsingAsync(storeFile);

            var viewElementsFullAddress = new Queue<Tuple<string, string, IEnumerable<IValidator>, bool, bool>>();
            viewElementsFullAddress.Enqueue(
                Tuple.Create("FullAddress".Translate(),
                    string.Format("EnterHere".Translate(), "FullAddress".Translate()),
                    new IValidator[] { new RequiredValidator() }.AsEnumerable(),
                    false, true));
            return new StoreModel
            {
                StoreName = Uri.UnescapeDataString(completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Name"))?[1]),
                StoreAbbr = Uri.UnescapeDataString(completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Name"))?[1])
                    .Trim().Remove(4,
                        Uri.UnescapeDataString(completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Name"))?[1])
                            .Length - 4),
                FullAddress = Uri.UnescapeDataString(
                    completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Address"))?[1] == "" ?
                    (await InputAlertHelper.LaunchInputAlertAsync(viewElementsFullAddress,
                        "Confirm".Translate(),
                        false)).First() as string :
                    Uri.UnescapeDataString(completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Address"))?[1]))
            };
        }

        /// <summary>
        /// Get Taxes data
        /// </summary>
        /// <param name="taxFile">Tax file</param>
        /// <returns>Taxes</returns>
        public async Task<List<TaxModel>> GetTaxDataAsync(object taxFile)
        {
            var completeParsing = await ExecuteParsingAsync(taxFile);
            var taxes = new List<TaxModel>();
            for (var i = 1; i < completeParsing.Item2 - 3; i += 2)
            {
                var rate = double.Parse(completeParsing.Item1[i + 1][1]);
                var viewElementsTax = new Queue<Tuple<string, string, IEnumerable<IValidator>, bool, bool>>();
                viewElementsTax.Enqueue(
                    Tuple.Create("TaxName".Translate(),
                        string.Format("EnterHere".Translate(), "TaxName".Translate()),
                        new IValidator[] { new RequiredValidator() }.AsEnumerable(),
                        false, true));
                var tax = new TaxModel
                {
                    Name = (await InputAlertHelper.LaunchInputAlertAsync(
                        viewElementsTax,
                        "Confirm".Translate(),
                        false,
                        string.Format("TaxNameArg".Translate(), rate))).First() as string,
                    Rate = rate / 100 + 1
                };
                taxes.Add(tax);
            }
            return taxes;
        }

        /// <summary>
        /// Get all employees
        /// </summary>
        /// <param name="sFolder">Staff folder</param>
        /// <returns>Employees</returns>
        public async Task<List<Tuple<EmployeeModel, string>>> GetEmployeesAsync(object sFolder, Queue<string> passwordSalt)
        {
            StorageFolder folder = (StorageFolder)sFolder;
            var data = new List<Tuple<EmployeeModel, string>>();
            foreach (var file in await folder.GetFilesAsync())
            {
                var completeParsing = await ExecuteParsingAsync(file);
                var salt = passwordSalt.Dequeue();
                var empBool =
                    await Application.Current.MainPage.DisplayAlert(
                        "Hmm".Translate(),
                        string.Format("AddArg".Translate(),
                            string.Format("EmpFormat".Translate(),
                                completeParsing.Item1.FirstOrDefault(x => x[0].Equals("LastName"))?[1],
                                completeParsing.Item1.FirstOrDefault(x => x[0].Equals("FirstName"))?[1])),
                        "Yes".Translate(), "No".Translate());
                if (!empBool) continue;

                var viewElementsEmpE = new Queue<Tuple<string, string, IEnumerable<IValidator>, bool, bool>>();
                var viewElementsEmpP = new Queue<Tuple<string, string, IEnumerable<IValidator>, bool, bool>>();

                viewElementsEmpE.Enqueue(
                    Tuple.Create("EMail".Translate(),
                        string.Format("EnterHere".Translate(), "EMail".Translate()),
                        new IValidator[]
                        {
                            new RequiredValidator(),
                            new EmailValidator()
                        }.AsEnumerable(),
                        false, true)
                    );
                viewElementsEmpP.Enqueue(
                    Tuple.Create("Password".Translate(),
                        string.Format("EnterHere".Translate(), "Password".Translate()),
                        new IValidator[]
                        {
                            new RequiredValidator(),
                            new PasswordValidator()
                        }.AsEnumerable(),
                        true, true)
                    );
                viewElementsEmpP.Enqueue(
                    Tuple.Create("ConfPassword".Translate(),
                        string.Format("EnterHere".Translate(), "ConfPassword".Translate()),
                        new IValidator[]
                        {
                            new RequiredValidator(),
                            new PasswordConfValidator()
                        }.AsEnumerable(),
                        true, true)
                    );
                var emp = new EmployeeModel()
                {
                    FName = completeParsing.Item1.FirstOrDefault(x => x[0].Equals("FirstName"))?[1],
                    LName = completeParsing.Item1.FirstOrDefault(x => x[0].Equals("LastName"))?[1],
                    Email = completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Email"))?[1] == "" ?
                        ((await InputAlertHelper.LaunchInputAlertAsync(
                            viewElementsEmpE,
                            "Confirm".Translate(),
                            false)).First() as string).ToLower()
                        : completeParsing.Item1.FirstOrDefault(x => x[0].Equals("Email"))?[1].ToLower(),
                    Salt = salt
                };
                var password = (await InputAlertHelper.LaunchInputAlertAsync(
                    viewElementsEmpP,
                    "Confirm".Translate(),
                    false)).First() as string;
                data.Add(Tuple.Create(emp, password));
            }
            return data;
        }
    }
}
