using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
#if __ANDROID__

#elif __IOS__

#else
using Windows.Storage;
#endif
namespace Plutus.Helpers
{
    public class Csv
    {
        private List<string> data { get; set; }
#if __ANDROID__

#elif __IOS__

#else
        private StorageFile file { get; set; }
#endif

        public Csv(List<PropertyInfo> hList, List<string> dList , string delimiter = ",")
        {
            var header = $"Id{delimiter}";
            foreach (var hItem in hList)
            {
                header += $"{hItem.Name}";
                if (!hList.LastOrDefault().Equals(hItem))
                {
                    header += $"{delimiter}";
                }
            }

            data = new List<string>
            {
                header
            };
            data.AddRange(dList);
        }

        public Csv(){}

        public async void CreateFile()
        {
#if __ANDROID__
            throw new NotImplementedException();
#elif __IOS__
            throw new NotImplementedException();
#else
            var file = await FileIO.GetFileSavePicker(
                new List<KeyValuePair<string, List<string>>>
                {
                    new KeyValuePair<string, List<string>>("Comma Seperated File", new List<string> {".csv"})
                },
                string.Format("{0}-MassStockUpdate-{1}", App.Store.StoreName,
                    DateTime.Now.ToString(CultureInfo.CurrentCulture))).PickSaveFileAsync();
            await Windows.Storage.FileIO.WriteLinesAsync(file, data);
#endif
        }

        public async Task<List<string>> ValidateHeaderOfFile()
        {
#if __ANDROID__
            throw new NotImplementedException();
#elif __IOS__
            throw new NotImplementedException();
#else
            file = await FileIO.GetFileOpenPicker(new List<string>
            {
                ".csv"
            }).PickSingleFileAsync();
            return await FileIO.GetFirstLineCsvAsync(file);
#endif
        }

        public async Task<List<KeyValuePair<string, string[]>>> ReadFile(bool skipHead = true)
        {
#if __ANDROID__
            throw new NotImplementedException();
#elif __IOS__
            throw new NotImplementedException();
#else
            var csvData = await FileIO.GetAllLinesCsvAsync(file);
            if (csvData == null)
            {

            }

            return skipHead
                ?  csvData
                :  (csvData ?? throw new InvalidOperationException()).Skip(1).ToList();
#endif
        }
    }
}
