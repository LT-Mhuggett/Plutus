using CommonPOSLibrary;
using Database.Models;
using NatApp.Plutus.Helpers.Extensions;
using NatApp.Plutus.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace NatApp.Plutus.Services.POSHandeling
{
    internal class PosPrinterManager : PrinterBaseOperations, IDisposable
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

        public async Task<string[]> GetBarcodeSymbols()
        {
            var keyValues = new List<KeyValuePair<string, object>>()
            {
                new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.getBarcodeSymbols", "null")
            };
            try
            {
                var stringResult = (string)await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);
                return JsonConvert.DeserializeObject<string[]>(stringResult);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return default;
            }
        }
        internal async Task SetupExecutePrintMultiLine()
        {
            if (!DeviceEnabled)
                throw new PrinterException("Printer is not Initalized", DeviceEnabled);
            var keyValues = new List<KeyValuePair<string, object>>()
            {
                new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.printMultiLines", JsonConvert.SerializeObject(Lines))
            };
            await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);
            await CloseConnection();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        internal async Task<bool> InitPrinter()
        {
            if (!string.IsNullOrEmpty(App.GetViewModel().PrinterLogicalNameSetting))
            {
                await GetPrinterList();
                if (Printers.Count == 0)
                {
                    return false;
                }
                bool printerFound = false;
                foreach (var printer in Printers)
                {
                    if (printer.Key.Equals(App.GetViewModel().PrinterLogicalNameSetting))
                    {
                        printerFound = true;

                        var keyValues = new List<KeyValuePair<string, object>>()
                        {
                            new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.initPrinter", printer.Key)
                        };
                        DeviceEnabled = (bool)await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);
                        break;
                    }
                }
                if (!printerFound)
                {
                    App.GetViewModel().PrinterLogicalNameSetting = null;
                    return false;
                }
                return DeviceEnabled;
            }
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sale"></param>
        /// <param name="store"></param>
        /// <returns></returns>
        private async Task SetUpExecutePrint(SaleModel sale, IEnumerable<IBasketRecord> basketRecords, StoreModel store)
        {
            var keyValues = new List<KeyValuePair<string, object>>();
            if (!DeviceEnabled)
                throw new PrinterException("Printer is not Initalized", DeviceEnabled);
            var text = new List<KeyValuePair<string, object>>();
            PrintHeaderofReceipt(sale, store);
            await PrintTransactionAndRefundsAsync(basketRecords);
            if (basketRecords.Where(bR => bR is BasketNote).Count() > 0)
                PrintNotes(basketRecords);
            PrintFooterOfReceipt(sale, store);
            CutPaper();
            var textToSend = JsonConvert.SerializeObject(Lines);
            keyValues.Add(new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.printMultiLines", textToSend));
            await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);
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
        private void PrintHeaderofReceipt(SaleModel sale, StoreModel store)
        {
            WriteText("ThankYouShopping".Translate(), "cntr", "true");
            if (store.Logo.Length != 0)
                WriteImage(store.Logo, "cntr");
            WriteText(store.StoreName, "cntr", "true");
            BlankLine();
            if (!string.IsNullOrEmpty(store.ContactNumber))
                WriteText(store.ContactNumber, "cntr");

            if (string.IsNullOrEmpty(store.FullAddress))
            {
                WriteText(store.AdLine1, "cntr");
                WriteText(store.AdLine2, "cntr");
                WriteText(store.PostCode, "cntr");
                WriteText(store.Country, "cntr");
            }
            else
                WriteText(store.FullAddress, "cntr");

            BlankLine();
            if (!string.IsNullOrEmpty(store.VatIN))
            {
                BlankLine();
                WriteText($"{"VatIN".Translate()}: {store.VatIN}", "cntr", "true");
            }
            BlankLine();
            WriteText($"{sale.DateOfSale:D}", "cntr");
            WriteText($"{sale.DateOfSale:T}", "cntr");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="sale"></param>
        /// <returns></returns>
        private async Task PrintTransactionAndRefundsAsync(IEnumerable<IBasketRecord> basketRecords)
        {
            var keyValues = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>($"{App.GetViewModel().SessionId.ToString()}.POS.getPageChars", "null")
            };
            int pageCharsMax = (int)await DependencyService.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValues);

            double pricePercent = 18.77;
            if (basketRecords.Where(bR => bR is BasketItem && !(bR is BasketReturnItem)).Count() > 0)
            {
                int basketItemMaxChar = 0;
                foreach (var basketItem in basketRecords.Where(bR => bR is BasketItem && !(bR is BasketReturnItem)).Cast<BasketItem>())
                    basketItemMaxChar = basketItem.Item.Id.Length > basketItemMaxChar ? basketItem.Item.Id.Length : basketItemMaxChar;

                double itemPercent = (double)(basketItemMaxChar + 1) / pageCharsMax * 100;
                double qtyPercent = (double)4 / pageCharsMax * 100;
                double namePercent = (pageCharsMax - (basketItemMaxChar + 1) - 4 - ((double)pageCharsMax / 100) * 18.75) / pageCharsMax * 100;

                WriteText("Sale".Translate(), bold: "true");
                WriteText($"{"Id".Translate()}\t{itemPercent}", bold: "true");
                WriteText($"{"Name".Translate()}\t{namePercent}", bold: "true");
                WriteText($"{"Price".Translate()}\t{pricePercent}", bold: "true");
                WriteText($"{"qty".Translate()}\t{qtyPercent}", bold: "true");

                foreach (var basketItem in basketRecords.Where(bR => bR is BasketItem && !(bR is BasketReturnItem)).Cast<BasketItem>())
                {
                    WriteText($"{basketItem.Item.Id}\t{itemPercent}");
                    WriteText($"{basketItem.Name}\t{namePercent}");
                    WriteText($"{string.Format("{0:0.00}", basketItem.Price)}\tR{pricePercent}");
                    WriteText($"{basketItem.Quantity}\tR{qtyPercent}");
                }
                ScoreReceipt();
            }

            if (basketRecords.Where(bR => bR is BasketReturnItem).Count() > 0)
            {
                int basketReturnItemMaxChar = 0;
                foreach (var basketReturnItem in basketRecords.Where(bR => bR is BasketReturnItem).Cast<BasketReturnItem>())
                    basketReturnItemMaxChar = basketReturnItem.Item.Id.Length > basketReturnItemMaxChar ? basketReturnItem.Item.Id.Length : basketReturnItemMaxChar;

                double returnItemPercent = ((double)basketReturnItemMaxChar + 1) / pageCharsMax * 100;
                double qtyPercent = (double)4 / pageCharsMax * 100;
                double namePercent = (pageCharsMax - (basketReturnItemMaxChar + 1) - 4 - ((double)pageCharsMax / 100) * 18.75) / pageCharsMax * 100;

                WriteText("Returns".Translate(), bold: "true");
                WriteText($"{"Id".Translate()}\t{returnItemPercent}", bold: "true");
                WriteText($"{"Name".Translate()}\t{namePercent}", bold: "true");
                WriteText($"{"Price".Translate()}\t{pricePercent}", bold: "true");
                WriteText($"{"qty".Translate()}\t{qtyPercent}", bold: "true");


                foreach (var basketReturnItem in basketRecords.Where(bR => bR is BasketReturnItem).Cast<BasketReturnItem>())
                {
                    WriteText($"{basketReturnItem.Item.Id}\t{returnItemPercent}");
                    WriteText($"{basketReturnItem.Name}\t{namePercent}");
                    WriteText($"{string.Format("{0:0.00}", Math.Abs(basketReturnItem.Price))}\tR{pricePercent}");
                    WriteText($"{basketReturnItem.Quantity}\tR{qtyPercent}");
                }
                ScoreReceipt();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sale"></param>
        private void PrintNotes(IEnumerable<IBasketRecord> basketRecords)
        {
            WriteText("Notes".Translate(), bold: "true");
            foreach (var note in basketRecords.Where(bR => bR is BasketNote).Cast<BasketNote>())
                WriteText(note.Note.Note);
            ScoreReceipt();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="sale"></param>
        /// <returns></returns>
        private void PrintFooterOfReceipt(SaleModel sale, StoreModel store)
        {
            WriteText("\t50");
            WriteText($"{"SubTotal".Translate()}\t25");
            WriteText($"{string.Format("{0:0.00}", sale.TotalExTax)}", "rght");
            WriteText("\t50");
            WriteText($"{"Tax".Translate()}\t25");
            WriteText($"{string.Format("{0:0.00}", sale.Total - sale.TotalExTax)}", "rght");
            WriteText("\t50");
            WriteText($"{"Total".Translate()}\t25");
            WriteText($"{string.Format("{0:0.00}", sale.Total)}", "rght");
            ScoreReceipt();

            var change = 0.0m;
            foreach (var payM in sale.PaySales)
            {
                WriteText($"{payM.TempPayMethod.Name}\t25");
                WriteText($"{string.Format("{0:0.00}", payM.Amount)}\tR20");
                WriteText($"\t55");
                change += payM.Change;
            }

            if (change > 0)
            {
                ScoreReceipt();
                WriteText("\t50");
                WriteText($"{"Change".Translate()}\t25");
                WriteText($"{string.Format("{0:0.00}", change)}\tR25");
            }

            BlankLine();
            WriteBarcode(sale.Id, App.GetViewModel().BarcodeSymbologySetting, 100, "cntr");
        }
        #endregion

        internal async Task ExecuteOposOrPdfAsync(SaleModel sale, IEnumerable<IBasketRecord> basketRecords, StoreModel store, System.IO.Stream image, decimal cashBack)
        {
            if (await InitPrinter())
            {
                if (sale.PaySales.Any(pay => pay.TempPayMethod.IsChangeable.Equals(true)))
                    await OpenCashDrawer();
                await SetUpExecutePrint(sale, basketRecords, store);
                await CloseConnection();
                DeviceEnabled = false;
            }
            else
                await PdfGeneration(sale, store, image, cashBack);
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
