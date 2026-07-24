using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.PointOfService;
using Windows.Devices.Enumeration;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using CommonPOSLibrary.Exceptions;
using Plutus.Frontend.AppClient.Platforms.Windows.Helpers;

namespace Plutus.Frontend.AppClient.Platforms.Windows.Services.POS
{
    class POSManager
    {
        #region Properties
        private POSPrinter pOSPrinter;
        #endregion

        /// <summary>
        /// The POS manager command selector and executor
        /// </summary>
        /// <param name="command">Command requested</param>
        /// <param name="value">Value argument to pass to executed method</param>
        /// <returns></returns>
        /// <exception cref="POSManagerCommandNotSupportedException">Thrown when a none supported <paramref name="command"/> is requested</exception>
        /// <exception cref="POSManagerValueInvalidException">Thrown when the <paramref name="value"/> type is invalid</exception>
        /// <exception cref="POSObjectException">Thrown when any POS object encounters and error</exception>
        public async Task<object> POSCommandSelection(string command, object value)
        {
            switch (command)
            {
                case "selectPrinter":
                    return (await DeviceHelpers.GetByDevicePickerAsync(PosPrinter.GetDeviceSelector()))?.Id ?? "";
                case "initPrinter":
                    {
                        if (!(value is string deviceId))
                        {
                            throw new POSManagerValueInvalidException($"Value: {value}, is not of type string.", typeof(string));
                        }
                        pOSPrinter = new POSPrinter(deviceId);
                        await pOSPrinter.CreatePOSObject();
                        await pOSPrinter.InitPOSObject();
                        return true;
                    }
                case "printMultiLines":
                    {
                        if (!(value is List<KeyValuePair<string, object>> toPrint))
                        {
                            throw new POSManagerValueInvalidException($"Value is not of type List<KeyValuePair<string, object>>.", typeof(List<KeyValuePair<string, object>>));
                        }
                        await pOSPrinter.PrintMultiLine(toPrint);
                        return true;
                    }
                case "closePrinter":
                    {
                        pOSPrinter.Dispose();
                        return true;
                    }
                case "openCashDrawer":
                    {
                        var pOSCashDrawer = new POSCashDrawer();
                        await pOSCashDrawer.CreatePOSObject();
                        await pOSCashDrawer.InitPOSObject();
                        await pOSCashDrawer.OpenCashDrawerAsync();
                        pOSCashDrawer.Dispose();
                        return true;
                    }
                case "getPageChars":
                    {
                        return pOSPrinter.PageChars();
                    }
                //case "getBarcodeSymbols":
                default:
                    throw new POSManagerCommandNotSupportedException($"Command: {command}, is not supported.");
            }
        }
    }
}
