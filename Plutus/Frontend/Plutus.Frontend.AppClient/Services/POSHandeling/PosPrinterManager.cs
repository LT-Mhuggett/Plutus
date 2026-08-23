using CommonPOSLibrary;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using CommonPOSLibrary.Enums;
using CommonPOSLibrary.Exceptions;
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
        /// <summary>
        /// Print the receipt for a COMMITTED sale (cutover step 14).
        ///
        /// ⚠ Takes <see cref="Services.Printing.ReceiptSale"/>, not the legacy `SaleModel`. The
        /// money and the sale id come from the payload the platform actually accepted, so the paper
        /// in the customer's hand cannot disagree with the record — and the barcode carries the
        /// platform `saleId`, which is the only thing a reprint or a receipt-led refund can ever
        /// look the sale up by.
        /// </summary>
        public async Task SetUpSalePrint(
            Services.Printing.ReceiptSale sale, IEnumerable<IBasketRecord> basketRecords, Models.StoreDetails store)
        {
            if (!_deviceEnabled)
                throw new POSPrinterException(POSPrinterExceptionType.PrinterNotEnabled, "Printer is not Enabled!");

            PrintHeaderofReceipt(sale, store);
            await PrintTransactionAndRefundsAsync(basketRecords);
            if (sale.Notes.Count > 0)
                PrintNotes(sale.Notes);

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
        private void PrintHeaderofReceipt(Services.Printing.ReceiptSale sale, Models.StoreDetails store)
        {
            // ⚠ A MISSING STORE MUST NOT LOSE THE SALE. `EnsureStoreAsync` has five paths that
            // deliberately leave `Store` null rather than block sign-in (no API, no store id, no
            // response, any exception) — and this method dereferenced it six times. The resulting
            // NullReferenceException escaped `FinaliseTransation`, an `async void` whose only
            // catches are for printer exceptions, AFTER the sale had been committed. So the sale
            // was recorded and queued, but the basket was never cleared and no confirmation shown —
            // and the operator, seeing no confirmation, rings it again. A missing shop address is
            // a blank line on a receipt; a duplicate sale is real money.
            store ??= new Models.StoreDetails();

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
            WriteText($"{sale.WhenLocal:D}", "cntr");
            WriteText($"{sale.WhenLocal:T}", "cntr");
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
            if (basketRecords.OfType<BasketItem>().Any(b => !b.IsReturn))
            {
                int basketItemMaxChar = 0;
                foreach (var basketItem in basketRecords.OfType<BasketItem>().Where(b => !b.IsReturn))
                    basketItemMaxChar = basketItem.Item.Id.Length > basketItemMaxChar ? basketItem.Item.Id.Length : basketItemMaxChar;

                double itemPercent = (double)(basketItemMaxChar + 1) / pageCharsMax * 100;
                double qtyPercent = (double)4 / pageCharsMax * 100;
                double namePercent = (pageCharsMax - (basketItemMaxChar + 1) - 4 - (double)pageCharsMax / 100 * 18.75) / pageCharsMax * 100;

                WriteText("Sale".Translate(), bold: "true");
                WriteText($"{"Id".Translate()}\t{itemPercent}", bold: "true");
                WriteText($"{"Name".Translate()}\t{namePercent}", bold: "true");
                WriteText($"{"Price".Translate()}\t{pricePercent}", bold: "true");
                WriteText($"{"qty".Translate()}\t{qtyPercent}", bold: "true");

                foreach (var basketItem in basketRecords.OfType<BasketItem>().Where(b => !b.IsReturn))
                {
                    WriteText($"{basketItem.Item.Id}\t{itemPercent}");
                    WriteText($"{basketItem.Name}\t{namePercent}");
                    WriteText($"{string.Format("{0:0.00}", basketItem.Price)}\tR{pricePercent}");
                    WriteText($"{basketItem.Quantity}\tR{qtyPercent}");
                }
                ScoreReceipt();
            }

            if (basketRecords.OfType<BasketItem>().Any(b => b.IsReturn))
            {
                int basketReturnItemMaxChar = 0;
                foreach (var basketReturnItem in basketRecords.OfType<BasketItem>().Where(b => b.IsReturn))
                    basketReturnItemMaxChar = basketReturnItem.Item.Id.Length > basketReturnItemMaxChar ? basketReturnItem.Item.Id.Length : basketReturnItemMaxChar;

                double returnItemPercent = ((double)basketReturnItemMaxChar + 1) / pageCharsMax * 100;
                double qtyPercent = (double)4 / pageCharsMax * 100;
                double namePercent = (pageCharsMax - (basketReturnItemMaxChar + 1) - 4 - (double)pageCharsMax / 100 * 18.75) / pageCharsMax * 100;

                WriteText("Returns".Translate(), bold: "true");
                WriteText($"{"Id".Translate()}\t{returnItemPercent}", bold: "true");
                WriteText($"{"Name".Translate()}\t{namePercent}", bold: "true");
                WriteText($"{"Price".Translate()}\t{pricePercent}", bold: "true");
                WriteText($"{"qty".Translate()}\t{qtyPercent}", bold: "true");


                foreach (var basketReturnItem in basketRecords.OfType<BasketItem>().Where(b => b.IsReturn))
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
        private void PrintNotes(IEnumerable<string> notes)
        {
            WriteText("Notes".Translate(), bold: "true");
            foreach (var note in notes)
                WriteText(note);

            ScoreReceipt();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="sale"></param>
        /// <returns></returns>
        /// <summary>
        /// ⚠ EVERY FIGURE HERE IS THE COMMITTED ONE. The totals are the payload's, not a re-sum of
        /// the basket — the customer's paper and the platform's record must not be two opinions —
        /// and pence are divided by 100 only at the moment of printing.
        /// </summary>
        private void PrintFooterOfReceipt(Services.Printing.ReceiptSale sale)
        {
            WriteText("\t50");
            WriteText($"{"SubTotal".Translate()}\t25");
            WriteText($"{sale.ExPence / 100m:0.00}", "rght");
            WriteText("\t50");
            WriteText($"{"Tax".Translate()}\t25");
            WriteText($"{sale.VatPence / 100m:0.00}", "rght");
            WriteText("\t50");
            WriteText($"{"Total".Translate()}\t25");
            WriteText($"{sale.GrossPence / 100m:0.00}", "rght");
            ScoreReceipt();

            foreach (var tender in sale.Tenders)
            {
                WriteText($"{tender.Name}\t25");
                WriteText($"{tender.AmountPence / 100m:0.00}\tR20");
                WriteText($"\t55");
            }

            if (sale.ChangePence > 0)
            {
                ScoreReceipt();
                WriteText("\t50");
                WriteText($"{"Change".Translate()}\t25");
                WriteText($"{sale.ChangePence / 100m:0.00}\tR25");
            }

            BlankLine();

            // ⚠ THE PLATFORM SALE ID. This printed `SaleModel.Id` — a legacy string that, since
            // step 11 deleted the legacy save which assigned it, NOTHING SETS, so every receipt has
            // been carrying an empty barcode. The barcode is how a customer's receipt finds its
            // sale again for a reprint or a receipt-led refund (step 15's read path keys on exactly
            // this), and an id the platform has never heard of can never be looked up.
            WriteBarcode(sale.SaleId.ToString("N"), App.GetViewModel().BarcodeSymbologySetting, 100, "cntr");
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


        private Task PdfGeneration(Models.CheckoutSale sale, Models.StoreDetails store, System.IO.Stream image, decimal cashBack)
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
