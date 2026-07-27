using Database.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Services.ThirdPartyTransfer
{
    public interface IThirdPartyTransfer
    {
        Task<Tuple<Queue<object>, int>> GetFoldersForProcessing(object sFolder);

        Task<Tuple<Queue<ItemModel>, Queue<string>>> GetItemsAsync(object sFolder, List<TaxModel> taxes);

        Task<StoreModel> GetStoreAsync(object storeFile);

        Task<List<TaxModel>> GetTaxDataAsync(object taxFile);

        Task<List<Tuple<EmployeeModel, string>>> GetEmployeesAsync(object sFolder, Queue<string> passwordSalt);
    }
}
