using POSIntegration.POS;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace POSIntegration
{
    public class Program
    {
        static AppServiceConnection connection = null;
        static bool keepRunning = true;
        private static List<PosManager> _posManagers = new List<PosManager>();


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

        private static async void Connection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
            var messageDeferral = args.GetDeferral();
            string key = args.Request.Message.First().Key;
            string value = args.Request.Message.First().Value.ToString();
            ValueSet valueSet = new ValueSet();
            var keyArray = key.Split(new[] { '.' }, StringSplitOptions.None);
            switch (keyArray[1])
            {
                case "POS":
                    var posManager = GetOrCreatePosManager(keyArray[0]);
                    if (keyArray[2].Equals("releaseObject"))
                        _posManagers.Remove(posManager);
                    var sendBack = posManager.POSCommandSelection(keyArray[2], value);

                    valueSet.Add("response", sendBack);
                    break;

                case "closeCommunication":
                    RemovePosManager(keyArray[0]);
                    valueSet.Add("response", "true");
                    break;

                case "endProcess":
                    RemovePosManager(keyArray[0]);
                    if (_posManagers.Count == 0)
                    {
                        keepRunning = false;
                        valueSet.Add("response", "true");
                    }
                    else
                    {
                        var @string = "";
                        _posManagers.ForEach(mgr => @string += $"{mgr.ClientId}, ");
                        valueSet.Add("response", @string);
                    }
                    break;

                default:
                    Debug.WriteLine("MISSING COMMAND IN PROGRAM!!!!");
                    valueSet.Add("response", "missingCommand");
                    break;
            }

            try
            {
                await args.Request.SendResponseAsync(valueSet);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                messageDeferral.Complete();
            }
        }

        private static PosManager FindPosManager(string cId)
        {
            foreach (var tempPosManager in _posManagers)
            {
                if (tempPosManager.ClientId.Equals(cId))
                {
                    return tempPosManager;
                }
            }
            return null;
        }

        private static PosManager GetOrCreatePosManager(string cId)
        {
            var posManager = FindPosManager(cId);
            if (posManager == null)
            {
                posManager = new PosManager(cId);
                _posManagers.Add(posManager);
            }
            return posManager;
        }

        private static void RemovePosManager(string cId)
        {
            var posManager = FindPosManager(cId);
            if (posManager != null)
            {
                _posManagers.Remove(posManager);
            }
        }
    }
}
