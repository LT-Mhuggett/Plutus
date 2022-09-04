using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Services.POSHandeling
{
    public interface IPOSCommunication
    {
        Task<object> SendAndGetResponseAsync(KeyValuePair<string, object> keyValue);
    }
}
