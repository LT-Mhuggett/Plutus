using Windows.Devices.PointOfService;
using Plutus.Frontend.ClientUI.Platforms.Windows.Helpers;
using CommonPOSLibrary.Exceptions;
using CommonPOSLibrary.Enums;
using Plutus.Frontend.ClientUI.Core;
using System.Diagnostics;
using Plutus.Frontend.ClientUI.Domain.Models;
using Plutus.Entities.Models;

namespace Plutus.Frontend.ClientUI.Services.PosHandeling
{
    public partial class PosPrinterManager : IDisposable
    {
        private PosPrinter _posPrinter;
        public async partial Task<string> SelectPrinterAndGetPrinterId()
        {
            return ((await DeviceHelpers.GetByDevicePickerAsync(Windows.Devices.PointOfService.PosPrinter.GetDeviceSelector()))?.Id) ?? "";
        }

        internal async partial Task SetupExecutePrintMultiLine()
        {
            if (!_deviceEnabled)
                throw new POSPrinterException(POSPrinterExceptionType.PrinterNotEnabled, "Printer is not Enabled.");
            await _posPrinter.PrintMultiLine(Lines);
        }

        internal async partial Task<bool> InitPrinter()
        {
            if (!string.IsNullOrEmpty(Settings.PrinterLogicalNameSetting))
            {
                try
                {
                    _posPrinter = new PosPrinter(Settings.PrinterLogicalNameSetting);
                    await _posPrinter.CreatePOSObject();
                    await _posPrinter.InitPOSObject();
                    return true;
                }
                catch(POSObjectException posObjectException)
                {
                    Debug.WriteLine(posObjectException.Message);
                    return false;
                }
            }
            return false;
        }

        public async partial Task OpenCashDrawer(string deviceId)
        {
            using PosCashDrawer posCashDrawer = new(deviceId);
            await posCashDrawer.CreatePOSObject();
            await posCashDrawer.InitPOSObject();
            await posCashDrawer.OpenCashDrawerAsync();
        }

        public async partial Task ExecuteOposOrPdfAsync()
        {
            if (Lines.Count == 0)
                throw new Exception("No lines have set to print!");

            if (_deviceEnabled)
                await _posPrinter.PrintMultiLine(Lines);

            //else
            //await PdfGeneration(sale, store, image, cashBack);
        }

        private partial uint GetPrinterPageChars()
        {
            if (!_deviceEnabled)
                throw new POSPrinterException(POSPrinterExceptionType.PrinterNotEnabled, "Printer is not Enabled.");
            return _posPrinter.PageChars();
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        /// <summary>
        /// Ensure all resources are disposed of
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _posPrinter.Dispose();
                    _deviceEnabled = false;
                }
                _disposedValue = true;
            }
        }

        /// <summary>
        /// Trigger the dispose method
        /// </summary>
        public void Dispose() => Dispose(true);
        #endregion
    }
}
