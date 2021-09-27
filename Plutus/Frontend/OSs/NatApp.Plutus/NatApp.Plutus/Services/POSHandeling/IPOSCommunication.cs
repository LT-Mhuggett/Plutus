using System.Collections.Generic;
using System.Threading.Tasks;

namespace NatApp.Plutus.Services.POSHandeling
{
    public interface IPOSCommunication
    {
        Task<object> SendAndGetResponseAsync(KeyValuePair<string, object> keyValue);
    }
}
