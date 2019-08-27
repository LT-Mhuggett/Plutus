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
            text = await PrintTransactionAndRefundsAsync(sale, text);
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

        private async Task<List<KeyValuePair<string, object>>> PrintTransactionAndRefundsAsync(SaleModel sale, List<KeyValuePair<string, object>> text)
        {
            var keyValues = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>($"{App.Store.Id}-32134.POS.getPageChars", "null")
            };
            int PageCharsMax = (int)await DependencyService.Get<IPOSCommunication>().SendAndGetReponseAsync(keyValues);

            double pricePercent = 18.75;

            if (sale.Transactions.Count > 0)
            {
                int transMaxChar = 0;
                foreach (var trans in sale.Transactions)
                {
                    transMaxChar = trans.TempItem.Id.Length > transMaxChar ? trans.TempItem.Id.Length : transMaxChar;
                }

                double transPercent = ((double)transMaxChar+1) / PageCharsMax * 100;
                double qtyPercent = (double)4 / PageCharsMax * 100;
                double namePercent = (PageCharsMax - (transMaxChar + 1) - 4 - ((double)PageCharsMax / 100) * 18.75) / PageCharsMax * 100;

                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Transaction")));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Id") + "\t" + transPercent));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Name") + "\t" + namePercent));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Price") + "\t" + pricePercent));
                text.Add(new KeyValuePair<string, object>("str..true.", "qty\t" + qtyPercent));

                foreach (var trans in sale.Transactions)
                {
                    text.Add(new KeyValuePair<string, object>("str...", trans.ItemId + "\t" + transPercent));
                    text.Add(new KeyValuePair<string, object>("str...", trans.TempItem.Name + "\t" + namePercent));
                    text.Add(new KeyValuePair<string, object>("str...", trans.TempItem.Price + "\tR" + pricePercent));
                    text.Add(new KeyValuePair<string, object>("str...", trans.Amount + "\tR" + qtyPercent));
                }
                text.Add(new KeyValuePair<string, object>("score...", ""));
            }

            if (sale.Refunds.Count > 0)
            {

                int refundsMaxChar = 0;
                foreach (var refund in sale.Refunds)
                {
                    refundsMaxChar = refund.TempItem.Id.Length > refundsMaxChar ? refund.TempItem.Id.Length : refundsMaxChar;
                }

                double refundPercent = ((double)refundsMaxChar + 1) / PageCharsMax * 100;
                double qtyPercent = (double)4 / PageCharsMax * 100;
                double namePercent = (PageCharsMax - (refundsMaxChar + 1) - 4 - ((double)PageCharsMax / 100) * 18.75) / PageCharsMax * 100;

                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Returns")));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Id") + "\t" + refundPercent));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Name") + "\t" + namePercent));
                text.Add(new KeyValuePair<string, object>("str..true.", App.Translate.ProvideValue("Price") + "\t" + pricePercent));
                text.Add(new KeyValuePair<string, object>("str..true.", "qty\t" + qtyPercent));

                foreach (var refund in sale.Refunds)
                {
                    text.Add(new KeyValuePair<string, object>("str...", refund.TempItem.Id + "\t" + refundPercent));
                    text.Add(new KeyValuePair<string, object>("str...", refund.TempItem.Name + "\t" + namePercent));
                    text.Add(new KeyValuePair<string, object>("str...", refund.TempItem.Price + "\tR" + pricePercent));
                    text.Add(new KeyValuePair<string, object>("str...", refund.Amount + "\tR" + qtyPercent));
                }
                text.Add(new KeyValuePair<string, object>("score...", ""));
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
            text.Add(new KeyValuePair<string, object>("score...", ""));
            return text;
        }

        private List<KeyValuePair<string, object>> PrintFooterOfReceipt(SaleModel sale, ref List<KeyValuePair<string, object>> text)
        {
            text.Add(new KeyValuePair<string, object>("str...", "\t50"));
            text.Add(new KeyValuePair<string, object>("str...", $"{App.Translate.ProvideValue("Total")}\t25"));
            text.Add(new KeyValuePair<string, object>("str.rght..", $"{sale.Total}\tR25"));
            text.Add(new KeyValuePair<string, object>("score...", ""));
            var change = 0.0m;
            foreach(var payM in sale.PaySales)
            {
                text.Add(new KeyValuePair<string, object>("str...", $"{payM.PayMethod.Name}\t25"));
                text.Add(new KeyValuePair<string, object>("str...", $"{payM.Amount}\tR20"));
                text.Add(new KeyValuePair<string, object>("str...", "\t55"));
                change += payM.Change;
            }
            if(change > 0)
            {
                text.Add(new KeyValuePair<string, object>("score...", ""));
                text.Add(new KeyValuePair<string, object>("str...", "\t50"));
                text.Add(new KeyValuePair<string, object>("str...", $"{App.Translate.ProvideValue("Change")}\t25"));
                text.Add(new KeyValuePair<string, object>("str...", $"{change}\tR25"));
            }
            text.Add(new KeyValuePair<string, object>("str...", "\n"));
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
                if (sale.PaySales.Exists(pay => pay.PayMethod.IsChangeable.Equals(true)))
                    await OpenCashDrawer();
                await SetupAndExecutePrint(sale, store);
                await CloseConnection();
                DeviceEnabled = false;
            }
        }
    }
}
