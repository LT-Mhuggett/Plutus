using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Services.PosHandeling
{
    public partial class PosPrinterManager : IDisposable
    {
        public async partial Task<string> SelectPrinterAndGetPrinterId()
        {
            throw new NotImplementedException();
        }

        internal async partial Task SetupExecutePrintMultiLine()
        {
            throw new NotImplementedException();
        }

        internal async partial Task<bool> InitPrinter()
        {
            throw new NotImplementedException();
        }

        public partial Task OpenCashDrawer(string deviceId)
        {
            throw new NotImplementedException();
        }

        public partial Task ExecuteOposOrPdfAsync()
        {
            throw new NotImplementedException();
        }

        private partial uint GetPrinterPageChars()
        {
            throw new NotImplementedException();
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
                    //Dispose of fields and properties
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
