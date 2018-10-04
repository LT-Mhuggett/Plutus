using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Plutus.Helpers.Interface;
using Database.Models;
using Xamarin.Forms;
using Newtonsoft.Json;
using System.Linq;

namespace Plutus.Helpers
{
    class PosPrinterManager
    {
        public bool DeviceEnabled { get; set; } = false;
        private Dictionary<string, Dictionary<string, object>> _printers { get; set; }

        public async Task<Dictionary<string, Dictionary<string, object>>> GetPrinterList()
        {
            var keyValues = new List<KeyValuePair<string, object>>();
            keyValues.Add(new KeyValuePair<string, object>($"{App.Store.Id}-32134.POS.getPrinters", "null"));
            var stringResult = (string) await DependencyService.Get<IPOSCommunication>().SendAndGetReponseAsync(keyValues);
            var result = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(stringResult);
            return result;
        }

        private async Task<bool> InitPrinter(string logicalName)
        {
            var keyValues = new List<KeyValuePair<string, object>>();
            keyValues.Add(new KeyValuePair<string, object>($"{App.Store.Id}-32134.POS.initPrinter", logicalName));
            return DeviceEnabled = (bool) await DependencyService.Get<IPOSCommunication>().SendAndGetReponseAsync(keyValues);
        }

        private async Task<bool> SetupAndExecutePrint(SaleModel sale, StoreModel store)
        {
            var keyValues = new List<KeyValuePair<string, object>>();
            if (!DeviceEnabled)
                throw new Exception("No printer is enabled!!");
            var text = PrintHeaderOfReceipt(store, sale);
            text = PrintTransactionAndRefunds(sale, ref text);
            if(sale.Notes.Count > 0)
            {
                text = PrintNotes(sale, ref text);
            }
            text = PrintFooterOfReceipt(sale, ref text);
            text.Add(new KeyValuePair<string, object>("cut...", ""));
            var textToSend = JsonConvert.SerializeObject(text);
            keyValues.Add(new KeyValuePair<string, object>($"{App.Store.Id}-32134.POS.printMultiLines", textToSend));
            return (bool) await DependencyService.Get<IPOSCommunication>().SendAndGetReponseAsync(keyValues);        
        }

        private async Task<bool> OpenCashDrawer()
        {
            var keyValues = new List<KeyValuePair<string, object>>();
            keyValues.Add(new KeyValuePair<string, object>($"{App.Store.Id}-32134.POS.openCashDrawer", "null"));
            return DeviceEnabled = (bool)await DependencyService.Get<IPOSCommunication>().SendAndGetReponseAsync(keyValues);
        }

        private async Task<bool> CloseConnection()
        {
            if(!DeviceEnabled)
                throw new Exception("No printer is enabled!!");
            var keyValues = new List<KeyValuePair<string, object>>();
            keyValues.Add(new KeyValuePair<string, object>($"{App.Store.Id}-32134.POS.closePrinter", ""));
            return (bool) await DependencyService.Get<IPOSCommunication>().SendAndGetReponseAsync(keyValues);
        }

        #region Format data for printing
        private List<KeyValuePair<string, object>> PrintHeaderOfReceipt(StoreModel store, SaleModel sale)
        {
            var head = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>( "str.cntr.true.", "Thank you for shopping at" ),
                new KeyValuePair<string, object>( "str.cntr.true.", store.StoreName ),
            };
            if(string.IsNullOrEmpty(store.FullAddress))
            {
                head.Add(new KeyValuePair<string, object>( "str.cntr.true.", store.AdLine1 ));
                head.Add(new KeyValuePair<string, object>( "str.cntr.true.", store.AdLine2 ));
                head.Add(new KeyValuePair<string, object>( "str.cntr.true.", store.PostCode));
                head.Add(new KeyValuePair<string, object>( "str.cntr.true.", store.Country ));
            }
            else
            {
                head.Add(new KeyValuePair<string, object> ( "str.cntr.true.", store.FullAddress));
            }
            head.Add(new KeyValuePair<string, object> ( "str.cntr.true.", $"{sale.DateOfSale:D}"));
            head.Add(new KeyValuePair<string, object> ( "str.cntr.true.", $"{sale.DateOfSale:T}"));
            return head;
        }

        private List<KeyValuePair<string, object>> PrintTransactionAndRefunds(SaleModel sale, ref List<KeyValuePair<string, object>> text)
        {
            if (sale.Transactions.Count > 0)
            {
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Transaction")));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Id") + "\t33.35"));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Name") + "\t47.90"));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Price") + "\t18.75"));

                foreach(var trans in sale.Transactions)
                {
                    for(int i = 0; i < trans.Amount; i++)
                    { 
                        text.Add(new KeyValuePair<string, object>("str...", trans.ItemId + "\t33.35"));
                        text.Add(new KeyValuePair<string, object>("str...", trans.Item.Name + "\t47.90"));
                        text.Add(new KeyValuePair<string, object>("str...", $"{trans.Item.Price:c}\tR18.75"));
                    }
                }
            }

            if(sale.Refunds.Count > 0)
            {
                text.Add(new KeyValuePair<string, object>("score...", ""));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Returns")));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Id") + "\t33.35"));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Name") + "\t47.90"));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Price") + "\t18.75"));

                foreach (var refund in sale.Refunds)
                {
                    for(int i = 0; i < refund.Amount;i++)
                    { 
                        text.Add(new KeyValuePair<string, object>("str...", refund.Id + "\t33.35"));
                        text.Add(new KeyValuePair<string, object>("str...", refund.Item.Name + "\t47.90"));
                        text.Add(new KeyValuePair<string, object>("str...", $"{refund.Item.Price:c}\tR18.75"));
                    }
                }
            }
            return text;
        }

        private List<KeyValuePair<string, object>> PrintNotes(SaleModel sale, ref List<KeyValuePair<string, object>> text)
        {
            text.Add(new KeyValuePair<string, object>("str..true.", "Notes"));
            foreach(var noteSale in sale.Notes)
            {
                text.Add(new KeyValuePair<string, object>("str...", noteSale.Note.Note));
            }
            return text;
        }

        private List<KeyValuePair<string, object>> PrintFooterOfReceipt(SaleModel sale, ref List<KeyValuePair<string, object>> text)
        {
            text.Add(new KeyValuePair<string, object>("score...", ""));
            text.Add(new KeyValuePair<string, object>("str...", "\t50"));
            text.Add(new KeyValuePair<string, object>("str...", $"{App.Translate.ProvideValue("Total")}\t25"));
            text.Add(new KeyValuePair<string, object>("str.rght..", $"{sale.Total:c}\tR25"));
            text.Add(new KeyValuePair<string, object>("score...", ""));
            var change = 0.0m;
            foreach(var payM in sale.PaySales)
            {
                text.Add(new KeyValuePair<string, object>("str...", $"{payM.PayMethod.Name}\t25"));
                text.Add(new KeyValuePair<string, object>("str...", $"{payM.Amount:c}\tR20"));
                text.Add(new KeyValuePair<string, object>("str...", "\t55"));
                change += payM.Change;
            }
            if(change>0)
            {
                text.Add(new KeyValuePair<string, object>("score...", ""));
                text.Add(new KeyValuePair<string, object>("str...", "\t50"));
                text.Add(new KeyValuePair<string, object>("str...", $"{App.Translate.ProvideValue("Change")}\t25"));
                text.Add(new KeyValuePair<string, object>("str...", $"{change:c}\tR25"));
            }
            text.Add(new KeyValuePair<string, object>("brc.cntr.100.belw", sale.Id));
            return text;
        }
        #endregion

        internal async Task ExecuteOposOrPdfAsync(StoreModel store, System.IO.Stream image, SaleModel sale, decimal cashBack)
        {
            if(App.AppSettings.PrinterLogicalName == null)
            {
                var pdf = new PDFCreator();
                await pdf.GenRecipt(store, null, sale, cashBack);
                return;
            }
            _printers = await GetPrinterList();
            if(_printers.Count == 0)
            {
                await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), "No Printers found defaulting to PDF, Please fix this issue within the Admin control area.", App.Translate.ProvideValue("Cancel"));
                App.AppSettings.PrinterLogicalName = null;
                var pdf = new PDFCreator();
                await pdf.GenRecipt(store, null, sale, cashBack);
            }
            else
            {
                var printerFound = false;
                foreach(var printer in _printers)
                {
                    if(printer.Key == App.AppSettings.PrinterLogicalName)
                    {
                        printerFound = true;
                        await InitPrinter(printer.Key);
                    }
                }
                if(!printerFound)
                {
                    await App.Current.MainPage.DisplayAlert(App.Translate.ProvideValue("Hmm"), "No Printer could not be found defaulting to PDF, Please fix this issue within the Admin control area.", App.Translate.ProvideValue("Cancel"));
                    App.AppSettings.PrinterLogicalName = null;
                    var pdf = new PDFCreator();
                    await pdf.GenRecipt(store, null, sale, cashBack);
                }
                await SetupAndExecutePrint(sale, store);
                if (sale.PaySales.Exists(pay => pay.PayMethod.IsChangeable.Equals(true)))
                    await OpenCashDrawer();
                await CloseConnection();
                DeviceEnabled = false;
            }
        }
        /*
#if WINDOWS_UWP
public async void PrintManagment(SaleModel sale, StoreModel store)
{
   if (!PrinterClaimed)
       return;
   Debug.Assert(ClaimedPrinter.Receipt != null, "ClaimedPrinter.Receipt != null");
   ReceiptPrintJob printJob = ClaimedPrinter.Receipt.CreateJob();
   if (Printer.Capabilities.Receipt.IsBoldSupported &&
       Printer.Capabilities.Receipt.IsDoubleHighDoubleWidePrintSupported)
   {
       ESC = "\u001B";
       GS = "\u001D";
       InitializePrinter = "@";
       BoldOn = ESC + "E\u0001";
       BoldOff = ESC + "E\0";
       DoubleOn = GS + "!\u0011";
       DoubleOff = GS + "!\0";
       printJob.Print(InitializePrinter);
   }

   PrintHeaderOfReceipt(ref printJob, store, sale);

   printJob.PrintBarcode(sale.Id, BarcodeSymbologies.Code128, 30, 120, PosPrinterBarcodeTextPosition.Below,
       PosPrinterAlignment.Center);

   /**
    * Prepare Transaction prints
    */
        /*
       {
           var text = "";
           foreach (var tran in sale.Transactions)
           {
               if (text == "")
                   PrintHeadOfTransaction(ref printJob);

               SetFormatForTransItemInfoPrint(tran, ref text);
           }

           PrintLineFeedNoCut(ref printJob, text);
       }

       /**
        * Prepare Return prints
        *//*
       {
           var text = "";
           foreach (var @return in sale.Refunds)
           {
               if (text == "")
               {
                   text += $"{BoldOn}{DoubleOn}Returns{DoubleOff}{BoldOff}\n";
                   PrintHeadOfTransaction(ref printJob);
               }

               SetFormatForReturnsItemInfoPrint(@return, ref text);
           }

           PrintLineFeedNoCut(ref printJob, text);
       }

       SetFormatForSaleInforationPrint(sale, ref printJob);

       PrintFooterOfReceipt(ref printJob);

       await ExecuteJobAndReport(printJob);
   }

   #region BasicPrintFormats

   private void SetFormatForTransItemInfoPrint(TransactionModel transaction, ref string text)
   {
       text +=
           $"{transaction.Item.Name}\t{transaction.Item.Id}\t{transaction.Item.Price:C}\n";
   }

   private void SetFormatForReturnsItemInfoPrint(RefundModel @return, ref string text)
   {
       for (var i = 0; i < @return.Amount - 1; i++)
           text += $"{@return.Item.Name}\t{@return.Item.Id}\t{ESC + "|rA"}{@return.Item.Price:C}{ESC + "|lA"}\n";
       text += $"Note:\t{@return.Reason}\n";
   }

   private void SetFormatForSaleInforationPrint(SaleModel sale, ref ReceiptPrintJob job)
   {
       var text = $"Total:\t\t{sale.Total:C}\n";
       var change = 0.0m;
       foreach (var paySale in sale.PaySales)
       {
           text += $"{paySale.PayMethod.Name}\t\t{ESC + "|rA"}{paySale.Amount}{ESC + "|lA"}\n";
           change += paySale.Change;
       }

       text += $"Change\t\t{ESC + "|rA"}{change:C}{ESC + "|lA"}";
       PrintLineFeedNoCut(ref job, text);
   }

   #endregion

   #region HeadsOfSections



   private void PrintHeadOfTransaction(ref ReceiptPrintJob job)
   {
       var text = $"{BoldOn}Item Name\tItem ID\t{ESC + "|rA"}Price{ESC + "|lA"}{BoldOff}\n";
       PrintLineFeedNoCut(ref job, text);
   }

   private void PrintFooterOfReceipt(ref ReceiptPrintJob job)
   {
       var text = $"{BoldOn}Please come again soon!{BoldOff}";
       PrintLineFeedCutAfter(ref job, text);
   }

   #endregion

#endif
*/
    }
}
