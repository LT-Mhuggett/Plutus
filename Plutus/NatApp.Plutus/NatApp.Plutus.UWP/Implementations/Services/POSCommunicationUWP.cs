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
        /// <param name="Id"></param>
        /// <returns></returns>
        public async Task<bool> CloseCommunicationAsync(string Id)
        {
            ValueSet valueSet = new ValueSet();
            valueSet.Add($"{Id}.closeCommunication", "null");
            if (App._appServiceConnection == null) return true;
            AppServiceResponse serviceResponce = await App._appServiceConnection.SendMessageAsync(valueSet);
            if (bool.Parse(serviceResponce.Message["response"] as string))
            {
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        public static async Task<bool> CloseCommunicationStaticAsync(string Id)
        {
            ValueSet valueSet = new ValueSet();
            valueSet.Add($"{Id}.closeCommunication", "null");
            if (App._appServiceConnection == null) return true;
            AppServiceResponse serviceResponce = await App._appServiceConnection.SendMessageAsync(valueSet);
            if (bool.Parse(serviceResponce.Message["response"] as string))
            {
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        public static async Task<bool> CloseServiceAsync(string Id)
        {
            ValueSet valueSet = new ValueSet();
            valueSet.Add($"{Id}.endProcess", "null");
            if (App._appServiceConnection == null) return true;
            AppServiceResponse serviceResponce = await App._appServiceConnection.SendMessageAsync(valueSet);
            if (bool.Parse(serviceResponce.Message["response"] as string))
            {
                App._appServiceDeferral.Complete();
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<bool> OpenCommunicationAsync()
        {
            try
            {
                await Windows.ApplicationModel.FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                await Task.Delay(3000);
                return true;
            }
            catch(Exception ex)
            {
                Debug.WriteLine("Rebuild the solution and make sure the BackgroundProcess is in the AppX folder");
                Debug.WriteLine(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyValue"></param>
        /// <returns></returns>
        public async Task<object> SendAndGetResponseAsync(List<KeyValuePair<string, object>> keyValue)
        {
            ValueSet valueSet = new ValueSet();
            keyValue.ForEach(kV => valueSet.Add(kV));
            if (App._appServiceConnection == null) return "App Closed!";
            AppServiceResponse serviceResponse = await App._appServiceConnection.SendMessageAsync(valueSet);
            return serviceResponse.Message["response"];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyValue"></param>
        /// <returns></returns>
        public static async Task<object> SendAndGetResponseStaticAsync(List<KeyValuePair<string, object>> keyValue)
        {
            ValueSet valueSet = new ValueSet();
            keyValue.ForEach(kV => valueSet.Add(kV));
            if (App._appServiceConnection == null) return "App Closed!";
            AppServiceResponse serviceResponse = await App._appServiceConnection.SendMessageAsync(valueSet);
            return serviceResponse.Message["response"];
        }
    }
}
