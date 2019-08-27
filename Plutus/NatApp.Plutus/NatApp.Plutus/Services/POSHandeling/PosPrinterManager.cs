using Database.Models;
using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace NatApp.Plutus.Services.POSHandeling
{
    internal class PosPrinterManager : IDisposable
    {
        /// <summary>
        /// Is the Printer Device enabled
        /// </summary>
        private bool DeviceEnabled { get; set; }

        /// <summary>
        /// The list of available POS Printers
        /// </summary>
        internal Dictionary<string, Dictionary<string, object>> Printers { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public PosPrinterManager()
        {
            DeviceEnabled = false;
        }

        /// <summary>
        /// Retrieve all POS printers
        /// </summary>
        /// <returns>Printers in Dictionary format</returns>
        public async Task<Dictionary<string, Dictionary<string, object>>> GetPrinterList()
        {
            var keyValues = new List<KeyValuePair<string, object>>()
            {
                new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.getPrinters", "null")
            };
            try
            {
                var stringResult = (string)await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);
                return Printers = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(stringResult);
            }
            catch (Exception ex)
            {
                //Failed cast
                Debug.WriteLine(ex.Message);
                return default;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="logicalName"></param>
        /// <returns></returns>
        private async Task<bool> InitPrinter(string logicalName)
        {
            var keyValues = new List<KeyValuePair<string, object>>()
            {
                new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.initPrinter", logicalName)
            };
            return DeviceEnabled = (bool)await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sale"></param>
        /// <param name="store"></param>
        /// <returns></returns>
        private async Task<bool> SetUpExecutePrint(SaleModel sale, IEnumerable<IBasketRecord> basketRecords, StoreModel store)
        {
            var keyValues = new List<KeyValuePair<string, object>>();
            if (!DeviceEnabled)
                throw new PrinterException("Printer is not Initalized", DeviceEnabled);
            var text = new List<KeyValuePair<string, object>>();
            PrintHeaderofReceipt(ref text, sale, store);
            text = await PrintTransactionAndRefundsAsync(text, basketRecords);
            if (basketRecords.Where(bR=>bR is BasketNote).Count() > 0)
                PrintNotes(ref text, basketRecords);
            PrintFooterOfReceipt(ref text, sale);
            text.Add(new KeyValuePair<string, object>("cut...", ""));
            var textToSend = JsonConvert.SerializeObject(text);
            keyValues.Add(new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.printMultiLines", textToSend));
            return (bool)await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);
        }

        /// <summary>
        /// Open the Cash drawer
        /// </summary>
        /// <returns>Successful or Not</returns>
        private async Task<bool> OpenCashDrawer()
        {
            var keyValues = new List<KeyValuePair<string, object>>()
            {
                new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.openCashDrawer", "null")
            };
            return (bool)await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);
        }

        /// <summary>
        /// Release the printers lock
        /// </summary>
        /// <returns>Successful of Not</returns>
        private async Task<bool> CloseConnection()
        {
            if (!DeviceEnabled)
                throw new PrinterException("Printer is not Initalized", DeviceEnabled);
            var keyValues = new List<KeyValuePair<string, object>>()
            {
                new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.closePrinter", "null")
            };
            return (bool)await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);
        }

        #region Format data for printing
        /// <summary>
        /// Format the text to print at the head of the receipt
        /// </summary>
        /// <param name="sale">The current Sale to print</param>
        /// <param name="store">The current Store transaction is occuring at</param>
        /// <returns></returns>
        private List<KeyValuePair<string, object>> PrintHeaderofReceipt(ref List<KeyValuePair<string, object>> text,
            SaleModel sale, StoreModel store)
        {
            text.Add(new KeyValuePair<string, object>("str.cntr.true.", "ThankYouShopping".Translate()));
            text.Add(new KeyValuePair<string, object>("str.cntr.true.", store.StoreName));
            if (string.IsNullOrEmpty(store.FullAddress))
            {
                text.Add(new KeyValuePair<string, object>("str.cntr.true.", store.AdLine1));
                text.Add(new KeyValuePair<string, object>("str.cntr.true.", store.AdLine2));
                text.Add(new KeyValuePair<string, object>("str.cntr.true.", store.PostCode));
                text.Add(new KeyValuePair<string, object>("str.cntr.true.", store.Country));
            }
            else
                text.Add(new KeyValuePair<string, object>("str.cntr.true.", store.FullAddress));
            text.Add(new KeyValuePair<string, object>("str.cntr.true.", $"{sale.DateOfSale:D}"));
            text.Add(new KeyValuePair<string, object>("str.cntr.true.", $"{sale.DateOfSale:T}"));
            return text;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="sale"></param>
        /// <returns></returns>
        private async Task<List<KeyValuePair<string, object>>> PrintTransactionAndRefundsAsync(List<KeyValuePair<string, object>> text,
            IEnumerable<IBasketRecord> basketRecords)
        {
            var keyValues = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.getPageChars", "null")
            };
            int pageCharsMax = (int)await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);

            double pricePercent = 18.77;
            if (basketRecords.Where(bR=>bR is BasketItem && !(bR is BasketReturnItem)).Count() > 0)
            {
                int basketItemMaxChar = 0;
                foreach (var basketItem in basketRecords.Where(bR=>bR is BasketItem && !(bR is BasketReturnItem)).Cast<BasketItem>())
                    basketItemMaxChar = basketItem.Item.Id.Length > basketItemMaxChar ? basketItem.Item.Id.Length : basketItemMaxChar;

                double itemPercent = (double)(basketItemMaxChar + 1) / pageCharsMax * 100;
                double qtyPercent = (double)4 / pageCharsMax * 100;
                double namePercent = (pageCharsMax - (basketItemMaxChar + 1) - 4 - ((double)pageCharsMax / 100) * 18.75) / pageCharsMax * 100;

                text.Add(new KeyValuePair<string, object>("str..true.", "Sale".Translate()));
                text.Add(new KeyValuePair<string, object>("str..true.", "Id".Translate() + "\t" + itemPercent));
                text.Add(new KeyValuePair<string, object>("str..true.", "Name".Translate() + "\t" + namePercent));
                text.Add(new KeyValuePair<string, object>("str..true.", "Price".Translate() + "\t" + pricePercent));
                text.Add(new KeyValuePair<string, object>("str..true.", "qty".Translate() + "\t" + qtyPercent));

                foreach (var basketItem in basketRecords.Where(bR => bR is BasketItem && !(bR is BasketReturnItem)).Cast<BasketItem>())
                {
                    text.Add(new KeyValuePair<string, object>("str...", basketItem.Item.Id + "\t" + itemPercent));
                    text.Add(new KeyValuePair<string, object>("str...", basketItem.Name + "\t" + namePercent));
                    text.Add(new KeyValuePair<string, object>("str...", basketItem.Price + "\tR" + pricePercent));
                    text.Add(new KeyValuePair<string, object>("str...", basketItem.Quantity + "\tR" + qtyPercent));
                }
                text.Add(new KeyValuePair<string, object>("score...", ""));
            }

            if (basketRecords.Where(bR=>bR is BasketReturnItem).Count() > 0)
            {
                int basketReturnItemMaxChar = 0;
                foreach (var basketReturnItem in basketRecords.Where(bR => bR is BasketReturnItem).Cast<BasketReturnItem>())
                    basketReturnItemMaxChar = basketReturnItem.Item.Id.Length > basketReturnItemMaxChar ? basketReturnItem.Item.Id.Length : basketReturnItemMaxChar;

                double returnItemPercent = ((double)basketReturnItemMaxChar + 1) / pageCharsMax * 100;
                double qtyPercent = (double)4 / pageCharsMax * 100;
                double namePercent = (pageCharsMax - (basketReturnItemMaxChar + 1) - 4 - ((double)pageCharsMax / 100) * 18.75) / pageCharsMax * 100;

                text.Add(new KeyValuePair<string, object>("str..true.", "Returns".Translate()));
                text.Add(new KeyValuePair<string, object>("str..true.", "Id".Translate() + "\t" + returnItemPercent));
                text.Add(new KeyValuePair<string, object>("str..true.", "Name".Translate() + "\t" + namePercent));
                text.Add(new KeyValuePair<string, object>("str..true.", "Price".Translate() + "\t" + pricePercent));
                text.Add(new KeyValuePair<string, object>("str..true.", "qty".Translate() + "\t" + qtyPercent));


                foreach (var basketReturnItem in basketRecords.Where(bR => bR is BasketReturnItem).Cast<BasketReturnItem>())
                {
                    text.Add(new KeyValuePair<string, object>("str...", basketReturnItem.Item.Id + "\t" + returnItemPercent));
                    text.Add(new KeyValuePair<string, object>("str...", basketReturnItem.Name + "\t" + namePercent));
                    text.Add(new KeyValuePair<string, object>("str...", Math.Abs(basketReturnItem.Price) * -1 + "\tR" + pricePercent));
                    text.Add(new KeyValuePair<string, object>("str...", basketReturnItem.Quantity + "\tR" + qtyPercent));
                }
                text.Add(new KeyValuePair<string, object>("score...", ""));
            }
            return text;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="sale"></param>
        /// <returns></returns>
        private List<KeyValuePair<string, object>> PrintNotes(ref List<KeyValuePair<string, object>> text, IEnumerable<IBasketRecord> basketRecords)
        {
            text.Add(new KeyValuePair<string, object>("str..true.", "Notes".Translate()));
            foreach (var note in basketRecords.Where(bR=>bR is BasketNote).Cast<BasketNote>())
                text.Add(new KeyValuePair<string, object>("str...", note.Note.Note));
            text.Add(new KeyValuePair<string, object>("score...", ""));
            return text;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="sale"></param>
        /// <returns></returns>
        private List<KeyValuePair<string, object>> PrintFooterOfReceipt(ref List<KeyValuePair<string, object>> text, SaleModel sale)
        {
            text.Add(new KeyValuePair<string, object>("str...", "\t50"));
            text.Add(new KeyValuePair<string, object>("str...", $"{"Total".Translate()}\t25"));
            text.Add(new KeyValuePair<string, object>("str.rght..", $"{sale.Total}"));
            text.Add(new KeyValuePair<string, object>("score...", ""));

            var change = 0.0m;
            foreach (var payM in sale.PaySales)
            {
                text.Add(new KeyValuePair<string, object>("str...", $"{payM.TempPayMethod.Name}\t25"));
                text.Add(new KeyValuePair<string, object>("str...", $"{payM.Amount}\tR20"));
                text.Add(new KeyValuePair<string, object>("str...", "\t55"));
                change += payM.Change;
            }

            if (change > 0)
            {
                text.Add(new KeyValuePair<string, object>("score...", ""));
                text.Add(new KeyValuePair<string, object>("str...", "\t50"));
                text.Add(new KeyValuePair<string, object>("str...", $"{"Change".Translate()}\t25"));
                text.Add(new KeyValuePair<string, object>("str...", $"{change}\tR25"));
            }
            text.Add(new KeyValuePair<string, object>("str...", "\n"));
            text.Add(new KeyValuePair<string, object>("brc.cntr.100.belw", sale.Id));
            return text;
        }
        #endregion

        internal async Task ExecuteOposOrPdfAsync(SaleModel sale, IEnumerable<IBasketRecord> basketRecords, StoreModel store, System.IO.Stream image, decimal cashBack)
        {
            if (string.IsNullOrEmpty(App.GetViewModel().PrinterLogicalNameSetting))
            {
                await PdfGeneration(sale, store, image, cashBack);
                return;
            }
            await GetPrinterList();
            if (Printers.Count == 0)
            {
                App.GetViewModel().PrinterLogicalNameSetting = null;
                await PdfGeneration(sale, store, image, cashBack);
                return;
            }
            var printerFound = false;
            foreach (var printer in Printers)
            {
                if (printer.Key.Equals(App.GetViewModel().PrinterLogicalNameSetting))
                {
                    printerFound = true;
                    await InitPrinter(printer.Key);
                }
            }
            if (!printerFound)
            {
                App.GetViewModel().PrinterLogicalNameSetting = null;
                await PdfGeneration(sale, store, image, cashBack);
                return;
            }
            if (sale.PaySales.Any(pay => pay.TempPayMethod.IsChangeable.Equals(true)))
                await OpenCashDrawer();
            await SetUpExecutePrint(sale, basketRecords, store);
            await CloseConnection();
            DeviceEnabled = false;
        }

        private Task PdfGeneration(SaleModel sale, StoreModel store, System.IO.Stream image, decimal cashBack)
        {
            //Create PDF
            Debug.WriteLine("PDF Creator Here");
            throw new NotImplementedException();
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        /// <summary>
        /// Ensure all resources are disposed of
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Printers = null;
                }

                disposedValue = true;
            }
        }

        /// <summary>
        /// Trigger the dispose method
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }

    [Serializable]
    internal class PrinterException : Exception
    {
        public bool DeviceEnabled;

        public PrinterException(bool deviceEnabled)
        {
            DeviceEnabled = deviceEnabled;
        }

        public PrinterException(string message, bool deviceEnabled) : base(message)
        {
            DeviceEnabled = deviceEnabled;
        }

        public PrinterException(string message, bool deviceEnabled, Exception innerException) : base(message, innerException)
        {
            DeviceEnabled = deviceEnabled;
        }

        protected PrinterException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
