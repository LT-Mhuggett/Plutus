using System;
using System.Collections.Generic;
using System.Text;
using Plutus.Models;

namespace Plutus.Helpers.Interface
{
    interface IReceiptPrinting
    {
        void PrintReceipt(SaleModel sale);
    }
}
