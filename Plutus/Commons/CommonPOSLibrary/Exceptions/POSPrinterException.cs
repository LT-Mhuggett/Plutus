using CommonPOSLibrary.Enums;
using System;

namespace CommonPOSLibrary.Exceptions
{
    public class POSPrinterException : Exception
    {
        public POSPrinterExceptionType POSPrinterExceptionType { get; }

        public POSPrinterException(POSPrinterExceptionType pOSPrinterExceptionType) : base()
        {
            POSPrinterExceptionType = pOSPrinterExceptionType;
        }

        public POSPrinterException(POSPrinterExceptionType pOSPrinterExceptionType, string message) : base(message)
        {
            POSPrinterExceptionType = pOSPrinterExceptionType;
        }
    }
}
