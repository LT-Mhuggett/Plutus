using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace NatApp.Plutus.Services.POSHandeling
{
    public interface IPOSCommunication
    {
        Task<bool> OpenCommunicationAsync();
        Task<object> SendAndGetResponseAsync(List<KeyValuePair<string, object>> keyValue);
        Task<bool> CloseCommunicationAsync(string Id);
    }
}
