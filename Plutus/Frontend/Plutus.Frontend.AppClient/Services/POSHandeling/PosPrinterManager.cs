using CommonPOSLibrary;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using CommonPOSLibrary.Enums;
using CommonPOSLibrary.Exceptions;
using Database.Models;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Services.POSHandeling
{
    internal class PosPrinterManager : PrinterBaseOperations, IDisposable
    {
        /// <summary>
        /// Is the Printer Device enabled
        /// </summary>
        private bool _deviceEnabled { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public PosPrinterManager()
        {
            _deviceEnabled = false;
        }

        /// <summary>
        /// Trigger the select printer device picker and return the printer Id
        /// </summary>
        /// <returns>Printer logical Id</returns>
        public async Task<string> SelectPrinterAndGetPrinterId()
        {
            var keyValue = new KeyValuePair<string, object>("selectPrinter", "null");
            try
            {
                return (string)await AppServices.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValue);

            }
            catch (Exception ex)
            {
                //Failed cast
                Debug.WriteLine(ex.Message);
                return default;
            }
        }

        internal async Task SetupExecutePrintMultiLine()
        {
            if (!_deviceEnabled)
                throw new POSPrinterException(POSPrinterExceptionType.PrinterNotEnabled, "Printer is not Enabled!");
            var keyValue = new KeyValuePair<string, object>("printMultiLines", Lines);
            await AppServices.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValue);
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
                try
                {
                    var keyValue = new KeyValuePair<string, object>("initPrinter", App.GetViewModel().PrinterLogicalNameSetting);
                    return _deviceEnabled = (bool)await AppServices.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValue);
                }
                catch (POSObjectException pOSObjectException)
                {
                    Console.WriteLine(pOSObjectException.Message);
                    throw pOSObjectException;
                }
            }
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sale"></param>
        /// <param name="store"></param>
        /// <returns></returns>
        public async Task SetUpSalePrint(SaleModel sale, IEnumerable<IBasketRecord> basketRecords, StoreModel store)
        {
            if (!_deviceEnabled)
                throw new POSPrinterException(POSPrinterExceptionType.PrinterNotEnabled, "Printer is not Enabled!");
            var text = new List<KeyValuePair<string, object>>();
            PrintHeaderofReceipt(sale, store);
            await PrintTransactionAndRefundsAsync(basketRecords);
            if (sale.Notes.Count() > 0)
                PrintNotes(sale.Notes.Select(saleNote => saleNote.Note));

            PrintFooterOfReceipt(sale);
            CutPaper();
        }

        /// <summary>
        /// Open the Cash drawer
        /// </summary>
        /// <returns>Successful or Not</returns>
        public async Task<bool> OpenCashDrawer()
        {
            var keyValue = new KeyValuePair<string, object>("openCashDrawer", "null");
            return (bool)await AppServices.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValue);
        }

        /// <summary>
        /// Release the printers lock
        /// </summary>
        /// <returns>Successful of Not</returns>
        public async Task<bool> CloseConnection()
        {
            if (!_deviceEnabled)
                return true;
            var keyValue = new KeyValuePair<string, object>("closePrinter", "null");
            if ((bool)await AppServices.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValue))
            {
                _deviceEnabled = false;
                return true;
            }

            return false;
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
            if (store.Logo != null && store.Logo.Length != 0)
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
            var keyValue = new KeyValuePair<string, object>("getPageChars", "null");
            uint pageCharsMax = (uint)await AppServices.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValue);

            double pricePercent = 18.77;
            if (basketRecords.Where(bR => bR is BasketItem && !(bR is BasketReturnItem)).Count() > 0)
            {
                int basketItemMaxChar = 0;
                foreach (var basketItem in basketRecords.Where(bR => bR is BasketItem && !(bR is BasketReturnItem)).Cast<BasketItem>())
                    basketItemMaxChar = basketItem.Item.Id.Length > basketItemMaxChar ? basketItem.Item.Id.Length : basketItemMaxChar;

                double itemPercent = (double)(basketItemMaxChar + 1) / pageCharsMax * 100;
                double qtyPercent = (double)4 / pageCharsMax * 100;
                double namePercent = (pageCharsMax - (basketItemMaxChar + 1) - 4 - (double)pageCharsMax / 100 * 18.75) / pageCharsMax * 100;

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
                double namePercent = (pageCharsMax - (basketReturnItemMaxChar + 1) - 4 - (double)pageCharsMax / 100 * 18.75) / pageCharsMax * 100;

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
        private void PrintNotes(IEnumerable<NoteModel> notes)
        {
            WriteText("Notes".Translate(), bold: "true");
            foreach (var note in notes)
                WriteText(note.Note);

            ScoreReceipt();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="sale"></param>
        /// <returns></returns>
        private void PrintFooterOfReceipt(SaleModel sale)
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

        internal async Task ExecuteOposOrPdfAsync()
        {
            if (Lines.Count == 0)
                throw new Exception("No lines have set to print!");

            if (_deviceEnabled)
            {
                var keyValue = new KeyValuePair<string, object>("printMultiLines", Lines);
                await AppServices.Get<IPOSCommunication>().SendAndGetResponseAsync(keyValue);
            }
            //else
                //await PdfGeneration(sale, store, image, cashBack);
        }


        private Task PdfGeneration(SaleModel sale, StoreModel store, System.IO.Stream image, decimal cashBack)
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
                    if (_deviceEnabled)
                        throw new Exception("Printer has not been disposed of");
                }
                _disposedValue = true;
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
}
