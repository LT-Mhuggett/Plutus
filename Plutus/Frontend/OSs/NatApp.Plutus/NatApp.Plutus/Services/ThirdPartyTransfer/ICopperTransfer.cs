using Database.Models;
using System;
using System.Collections.Generic;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace NatApp.Plutus.Services.ThirdPartyTransfer
{
    public interface ICopperTransfer : IThirdPartyTransfer
    {
        Task<Tuple<StoreModel, List<TaxModel>>> GetStoreAndTaxDataAsync(object sFolder);

        Task<Tuple<string[][], int>> ExecuteParsingAsync(object file);
    }
}
