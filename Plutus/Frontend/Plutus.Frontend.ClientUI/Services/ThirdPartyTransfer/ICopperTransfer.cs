using Plutus.Entities.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Services.ThirdPartyTransfer
{
    public interface ICopperTransfer : IThirdPartyTransfer
    {
        Task<Tuple<Store, List<Tax>>> GetStoreAndTaxDataAsync(object sFolder);

        Task<Tuple<string[][], int>> ExecuteParsingAsync(object file);
    }
}
