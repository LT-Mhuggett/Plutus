using NatApp.Plutus.UWP.Implementations.Services;
using NatApp.Plutus.Services.POSHandeling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Windows.Foundation.Collections;
using System.Diagnostics;
using Windows.ApplicationModel.AppService;

[assembly: Dependency(typeof(POSCommunicationUWP))]
namespace NatApp.Plutus.UWP.Implementations.Services
{
    public class POSCommunicationUWP : IPOSCommunication
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyValue"></param>
        /// <returns></returns>
        public async Task<object> SendAndGetResponseAsync(KeyValuePair<string, object> keyValue)
        {
            return await App.POSManager.POSCommandSelection(keyValue.Key, keyValue.Value);
        }
    }
}
