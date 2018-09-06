using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Plutus.Helpers.Interface;
using Plutus.UWP.Implementations;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;
using Xamarin.Forms;

[assembly: Dependency(typeof(POSCommunicationImplementation))]
namespace Plutus.UWP.Implementations
{
    public class POSCommunicationImplementation : IPOSCommunication
    {
        public async Task<bool> CloseCommunicationAsync(string Id)
        {
            /*
            throw new NotImplementedException();
            */
            ValueSet valueSet = new ValueSet();
            valueSet.Add($"{Id}.closeCommunication", "null");
            if(App.connection == null) return true;
            AppServiceResponse serviceResponse = await App.connection.SendMessageAsync(valueSet);
            if(Boolean.Parse(serviceResponse.Message["response"] as string))
            {
                App.appServiceDefferal.Complete();
                return true;
            }
            else
            {
                return false;
            }
        }

        public static async Task<bool> CloseServiceAsync(string Id)
        {
            ValueSet valueSet = new ValueSet();
            valueSet.Add($"{Id}.endProcess", "null");
            if (App.connection == null) return true;
            AppServiceResponse serviceResponse = await App.connection.SendMessageAsync(valueSet);
            if (Boolean.Parse(serviceResponse.Message["response"] as string))
            {
                App.appServiceDefferal.Complete();
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task<bool> OpenCommunicationAsync()
        {
            /*
            throw new NotImplementedException();
            */
            try
            {
                await Windows.ApplicationModel.FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                await Task.Delay(3000);
                return true;
            }
            catch(Exception e)
            {
                Debug.WriteLine("Rebuild the solution and make sure the BackgroundProcess is in your AppX folder");
                Debug.WriteLine(e);
                return false;
            }
        }
        
        public async Task<object> SendAndGetReponseAsync(List<KeyValuePair<string, object>> keyValue)
        {
            /*
            return App.POSIntegratorObj.LocalReceive(keyValue).Value;
            */
            ValueSet valueSet = new ValueSet();
            keyValue.ForEach(kV => valueSet.Add(kV));
            if (App.connection == null) return "App Closed!";
            AppServiceResponse serviceResponse = await App.connection.SendMessageAsync(valueSet);
            return serviceResponse.Message["response"];
        }
    }
}
