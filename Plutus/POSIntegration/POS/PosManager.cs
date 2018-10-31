using Microsoft.PointOfService;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Windows.Foundation.Collections;
using WindowsInstaller;

namespace POSIntegration.POS
{
    class PosManager
    {
        internal readonly string ClientId;
        private PosExplorer _explorer;
        private POSPrinter _POSPrinter;
        private POSCashDrawer _cashDrawer;

        internal PosExplorer posExplorer
        {
            get => _explorer;
            set => _explorer = value;
        }

        public PosManager(string cId)
        {
            ClientId = cId;
            try
            {
                posExplorer = new PosExplorer();
            }
            catch (NullReferenceException e)
            {
                var tempFilePath = Path.GetTempFileName();
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("POSIntegration.PosForDotNet-1.14.1.msi"))
                {
                    using (var tempFile = new FileStream(tempFilePath, FileMode.Open, FileAccess.ReadWrite))
                    {
                        resource.CopyTo(tempFile);
                    }
                }
                Type type = Type.GetTypeFromProgID("WindowsInstaller.Installer");
                Installer installer = (Installer)Activator.CreateInstance(type);
                installer.InstallProduct(tempFilePath, "ACTION=INSTALL ALLUSERS=2 MSIINSTALLPERUSER=");
                File.Delete(tempFilePath);
            }
        }

        public object POSCommandSelection(string option, object value)
        {
            ValueSet valueSet = new ValueSet();
            switch (option)
            {
                case "getPrinters":
                    return GetPrinterList();
                case "initPrinter":
                    CreatePrinterInstance(value as string);
                    return true;
                case "printMultiLines":
                    var data = JsonConvert.DeserializeObject<List<KeyValuePair<string, object>>>(value as string);
                    _POSPrinter.PrintMultiLine(data);
                    return true;
                case "closePrinter":
                    ClosePrinterInstance();
                    return true;
                case "openCashDrawer":
                    if(CreateCashDrawerInstance())
                        _cashDrawer.OpenCashDrawer();
                    return true;
                case "testPosForDotNetIsPresent":
                    if (posExplorer != null)
                        return true;
                    else
                        return false;
                case "getPageChars":
                    return _POSPrinter.PageChars();
                default:
                    Debug.WriteLine("MISSING COMMAND IN POS MANAGER!!!!");
                    return "missingCommand";
            }
        }

        public string GetPrinterList()
        {
            var printersData = new Dictionary<string, Dictionary<string, object>>();
            var printers = posExplorer.GetDevices(DeviceType.PosPrinter);
            if (printers.Count > 0)
            {
                foreach (DeviceInfo printerInfo in printers)
                {
                    var tempPrinter = posExplorer.CreateInstance(printerInfo) as PosPrinter;
                    var wasOpened = false;
                    if (tempPrinter.State == ControlState.Closed)
                    {
                        tempPrinter.Open();
                        wasOpened = true;
                    }
                    var capabilites = POSPrinter.GetCapabilites(tempPrinter);
                    if (wasOpened)
                        tempPrinter.Close();
                    var info = new Dictionary<string, object>()
                {
                    {"Manufacture Name", printerInfo.ManufacturerName },
                    {"Description", printerInfo.Description},
                };
                    var combinedDic = info.Concat(capabilites).GroupBy(d => d.Key).ToDictionary(d => d.Key, d => d.First().Value);
                    printersData.Add(printerInfo.LogicalNames.FirstOrDefault() ?? "", combinedDic);
                }
            }
            return JsonConvert.SerializeObject(printersData);
        }

        public void CreatePrinterInstance(string logicalPrinterName)
        {
            _POSPrinter = new POSPrinter(ref _explorer, logicalPrinterName);
        }

        public bool CreateCashDrawerInstance()
        {
            try
            {
                _cashDrawer = new POSCashDrawer(ref _explorer);
                return true;
            }
            catch(Exception e)
            {
                Debug.WriteLine("CashDrawer Not Present! " + e);
                return false;
            }
        }

        public void ClosePrinterInstance()
        {
            _POSPrinter.Dispose();
        }
    }
}
