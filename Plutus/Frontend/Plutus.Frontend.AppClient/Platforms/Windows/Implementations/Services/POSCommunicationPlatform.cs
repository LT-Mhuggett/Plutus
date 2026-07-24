using Plutus.Frontend.AppClient.Services.POSHandeling;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.Platforms.Windows.Implementations.Services
{
    public class POSCommunicationPlatform : IPOSCommunication
    {
        /// <summary>
        /// Despite the AppService-flavored name, this was always an in-process call straight into
        /// POSManager - no Windows.ApplicationModel.AppService IPC was actually wired up - so it
        /// carries over unchanged beyond the namespace/DI registration mechanism.
        /// </summary>
        public async Task<object> SendAndGetResponseAsync(KeyValuePair<string, object> keyValue)
        {
            return await Platforms.Windows.WindowsApp.POSManager.POSCommandSelection(keyValue.Key, keyValue.Value);
        }
    }
}
