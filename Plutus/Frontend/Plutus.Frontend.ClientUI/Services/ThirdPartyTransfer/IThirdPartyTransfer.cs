using Plutus.Entities.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Services.ThirdPartyTransfer
{
    public interface IThirdPartyTransfer
    {
        Task<Tuple<Queue<object>, int>> GetFoldersForProcessing(object sFolder);

        Task<Tuple<Queue<Item>, Queue<string>>> GetItemsAsync(object sFolder, List<Tax> taxes);

        Task<Store> GetStoreAsync(object storeFile);

        Task<List<Tax>> GetTaxDataAsync(object taxFile);

        Task<List<Tuple<Employee, string>>> GetEmployeesAsync(object sFolder, Queue<string> passwordSalt);
    }
}
