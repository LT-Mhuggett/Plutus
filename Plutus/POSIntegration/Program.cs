using System;
using System.Threading;
using Windows.ApplicationModel.AppService;
using System.Diagnostics;
using System.Linq;
using Windows.Foundation.Collections;
using System.Collections.Generic;
using POSIntegration.POS;
using Newtonsoft.Json;

namespace POSIntegration
{
    public class Program
    {
        static AppServiceConnection connection = null;
        static bool keepRunning = true;
        private static List<PosManager> _posManagers =  new List<PosManager>();


        static void Main(string[] args)
        {
            Thread appServicePOSThread = new Thread(new ThreadStart(ThreadProc));
            appServicePOSThread.Start();
            Debug.WriteLine("Started");
            while (keepRunning) Thread.Sleep(1000);
        }

        static async void ThreadProc()
        {
            connection = new AppServiceConnection();
            connection.AppServiceName = "POSIntegrationService";
            connection.PackageFamilyName = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            connection.RequestReceived += Connection_RequestReceived;

            AppServiceConnectionStatus status = await connection.OpenAsync();
            switch (status)
            {
                case AppServiceConnectionStatus.Success:
                    Debug.WriteLine("Connection established - waiting for requests");
                    break;
                case AppServiceConnectionStatus.AppNotInstalled:
                    Debug.WriteLine("The app AppServicesProvider is not installed.");
                    return;
                case AppServiceConnectionStatus.AppUnavailable:
                    Debug.WriteLine("The app AppServicesProvider is not available.");
                    return;
                case AppServiceConnectionStatus.AppServiceUnavailable:
                    Debug.WriteLine($"The app AppServicesProvider is installed but it does not provide the app service {connection.AppServiceName}.");
                    return;
                case AppServiceConnectionStatus.Unknown:
                    Debug.WriteLine("An unkown error occurred while we were trying to open an AppServiceConnection.");
                    return;
            }
        }

        private static void Connection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            string key = args.Request.Message.First().Key;
            string value = args.Request.Message.First().Value.ToString();
            ValueSet valueSet = new ValueSet();
            var keyArray = key.Split(new[]{'.'}, StringSplitOptions.None);
            switch (keyArray[1])
            {
                case "POS":
                    var posManager = GetPosManager(keyArray[0]);
                    if(keyArray[2].Equals("releaseObject"))
                        _posManagers.Remove(posManager);
                    var sendBack = posManager.POSCommandSelection(keyArray[2], value);

                    valueSet.Add("response", sendBack);
                    args.Request.SendResponseAsync(valueSet).Completed += delegate { };
                    valueSet.Clear();
                    break;
                case "endProcess":
                    valueSet.Add("response", "processEnded");
                    args.Request.SendResponseAsync(valueSet).Completed += delegate { };
                    _posManagers = null;
                    keepRunning = false;
                    valueSet.Clear();
                    return;
                default:
                    Debug.WriteLine("MISSING COMMAND IN PROGRAM!!!!");
                    valueSet.Add("response", "missingCommand");
                    args.Request.SendResponseAsync(valueSet).Completed += delegate { };
                    valueSet.Clear();
                    break;
            }
        }

        private static PosManager GetPosManager(string cId)
        {
            foreach (var tempPosManager in _posManagers)
            {
                if (tempPosManager.ClientId.Equals(cId))
                {
                    return tempPosManager;
                }
            }
            var posManager = new PosManager(cId);
            _posManagers.Add(posManager);
            return posManager;
        }
    }
}
