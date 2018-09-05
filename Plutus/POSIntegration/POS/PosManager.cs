using Microsoft.PointOfService;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace POSIntegration.POS
{
    class PosManager
    {
        internal readonly string ClientId;
        private PosExplorer _explorer;
        private POSPrinter _POSPrinter;

        internal PosExplorer posExplorer
        {
            get{ return _explorer??(_explorer=new PosExplorer()); }
        }

        public PosManager(string cId)
        {
            ClientId = cId;
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
                default:
                    Debug.WriteLine("MISSING COMMAND IN POS MANAGER!!!!");
                    return "missingCommand";
            }
        }

        public string GetPrinterList()
        {
            var printersData = new Dictionary<string, Dictionary<string, object>>();
            if (printersData.Count > 0)
            {
                var printers = posExplorer.GetDevices(DeviceType.PosPrinter);
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
                Debug.WriteLine("No Printers found!");
            return JsonConvert.SerializeObject(printersData);
        }

        public void CreatePrinterInstance(string logicalPrinterName)
        {
            _POSPrinter = new POSPrinter(ref _explorer, logicalPrinterName);
        }

        public void ClosePrinterInstance()
        {
            _POSPrinter.Dispose();
        }
    }
}
