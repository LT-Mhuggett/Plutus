/*using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Plutus.Helpers.Interface;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Print;
using Plutus.Models;
using Xamarin.Forms;
using Plutus.Droid;

[assembly: Dependency(typeof(ReceiptPrintingImplementation))]
namespace Plutus.Droid
{
    class ReceiptPrintingImplementation : IReceiptPrinting
    {
        void IReceiptPrinting.PrintReceipt(SaleModel sale)
        {
            //Get the print manager instance
            PrintManager printMgr = (PrintManager)Forms.Context.GetSystemService(Context.PrintService);

            string jobName = "Plutus Receipt";

            printMgr.
        }
    }
}*/