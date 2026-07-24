namespace Plutus.Frontend.ClientUI.Services.PosHandeling
{
    public partial class PosPrinterManager : IDisposable
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async partial Task<string> SelectPrinterAndGetPrinterId()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            throw new NotImplementedException();
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        internal async partial Task SetupExecutePrintMultiLine()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            throw new NotImplementedException();
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        internal async partial Task<bool> InitPrinter()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
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
