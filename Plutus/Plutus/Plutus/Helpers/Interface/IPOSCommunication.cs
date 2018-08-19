using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Helpers.Interface
{
    interface IPOSCommunication
    {
        Task<bool> OpenCommunicationAsync();

        Task<object> SendAndGetReponseAsync(List<KeyValuePair<string, object>> keyValue);

        Task<bool> CloseCommunicationAsync();
    }
}
