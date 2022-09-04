using CommonPOSLibrary;
using CommonPOSLibrary.Enums;
using CommonPOSLibrary.Exceptions;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Domain.Models;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;

namespace Plutus.Frontend.ClientUI.Services.PosHandeling
{
    public partial class PosPrinterManager : PrinterBaseOperations, IDisposable
    {
        private bool _deviceEnabled;
        /// <summary>
        /// Trigger the select printer device picker and return the printer Id
        /// </summary>
        /// <returns>Printer logical Id</returns>
        public partial Task<string> SelectPrinterAndGetPrinterId();

        internal partial Task SetupExecutePrintMultiLine();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        internal partial Task<bool> InitPrinter();

        /// <summary>
        /// Open the Cash drawer
        /// </summary>
        /// <returns>Successful or Not</returns>
        public partial Task OpenCashDrawer(string deviceId = null);

        
        public partial Task ExecuteOposOrPdfAsync();

        private partial uint GetPrinterPageChars();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sale"></param>
        /// <param name="store"></param>
        /// <returns></returns>
        public async Task SetUpSalePrint(Sale sale, IEnumerable<IBasketRecord> basketRecords, Store store, Business business)
        {
            if (!_deviceEnabled)
                throw new POSPrinterException(POSPrinterExceptionType.PrinterNotEnabled, "Printer is not Enabled!");
            var text = new List<KeyValuePair<string, object>>();
            PrintHeaderofReceipt(sale, store, business);
            await PrintTransactionAndRefundsAsync(basketRecords);
            if (sale.Notes.Count() > 0)
                PrintNotes(sale.Notes);

            PrintFooterOfReceipt(sale);
            CutPaper();
        }

        #region Format data for printing
        /// <summary>
        /// Format the text to print at the head of the receipt
        /// </summary>
        /// <param name="sale">The current Sale to print</param>
        /// <param name="store">The current Store transaction is occuring at</param>
        private void PrintHeaderofReceipt(Sale sale, Store store, Business business)
        {
            WriteText(Strings.ThankYouShopping, "cntr", "true");
            if (business.Logo != null && business.Logo.Length != 0)
                WriteImage(business.Logo, "cntr");
            WriteText(business.Name, "cntr", "true");
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
            if (!string.IsNullOrEmpty(business.VatIN))
            {
                BlankLine();
                WriteText($"{Strings.VatIN}: {business.VatIN}", "cntr", "true");
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
        private Task PrintTransactionAndRefundsAsync(IEnumerable<IBasketRecord> basketRecords)
        {
            var keyValue = new KeyValuePair<string, object>("getPageChars", "null");
            uint pageCharsMax = GetPrinterPageChars();

            double pricePercent = 18.77;
            if (basketRecords.Where(bR => bR is BasketItem && !(bR is BasketReturnItem)).Count() > 0)
            {
                int basketItemMaxChar = 0;
                foreach (var basketItem in basketRecords.Where(bR => bR is BasketItem && !(bR is BasketReturnItem)).Cast<BasketItem>())
                    basketItemMaxChar = basketItem.Item.IdOne.Length > basketItemMaxChar ? basketItem.Item.IdOne.Length : basketItemMaxChar;

                double itemPercent = (double)(basketItemMaxChar + 1) / pageCharsMax * 100;
                double qtyPercent = (double)4 / pageCharsMax * 100;
                double namePercent = (pageCharsMax - (basketItemMaxChar + 1) - 4 - (double)pageCharsMax / 100 * 18.75) / pageCharsMax * 100;

                WriteText(Strings.Sale, bold: "true");
                WriteText($"{Strings.Id}\t{itemPercent}", bold: "true");
                WriteText($"{Strings.Name}\t{namePercent}", bold: "true");
                WriteText($"{Strings.Price}\t{pricePercent}", bold: "true");
                WriteText($"{Strings.qty}\t{qtyPercent}", bold: "true");

                foreach (var basketItem in basketRecords.Where(bR => bR is BasketItem && !(bR is BasketReturnItem)).Cast<BasketItem>())
                {
                    WriteText($"{basketItem.Item.IdOne}\t{itemPercent}");
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
                    basketReturnItemMaxChar = basketReturnItem.Item.IdOne.Length > basketReturnItemMaxChar ? basketReturnItem.Item.IdOne.Length : basketReturnItemMaxChar;

                double returnItemPercent = ((double)basketReturnItemMaxChar + 1) / pageCharsMax * 100;
                double qtyPercent = (double)4 / pageCharsMax * 100;
                double namePercent = (pageCharsMax - (basketReturnItemMaxChar + 1) - 4 - (double)pageCharsMax / 100 * 18.75) / pageCharsMax * 100;

                WriteText(Strings.Returns, bold: "true");
                WriteText($"{Strings.Id}\t{returnItemPercent}", bold: "true");
                WriteText($"{Strings.Name}\t{namePercent}", bold: "true");
                WriteText($"{Strings.Price}\t{pricePercent}", bold: "true");
                WriteText($"{Strings.qty}\t{qtyPercent}", bold: "true");


                foreach (var basketReturnItem in basketRecords.Where(bR => bR is BasketReturnItem).Cast<BasketReturnItem>())
                {
                    WriteText($"{basketReturnItem.Item.IdOne}\t{returnItemPercent}");
                    WriteText($"{basketReturnItem.Name}\t{namePercent}");
                    WriteText($"{string.Format("{0:0.00}", Math.Abs(basketReturnItem.Price))}\tR{pricePercent}");
                    WriteText($"{basketReturnItem.Quantity}\tR{qtyPercent}");
                }
                ScoreReceipt();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sale"></param>
        private void PrintNotes(IEnumerable<Note> notes)
        {
            WriteText(Strings.Notes, bold: "true");
            foreach (var note in notes)
                WriteText(note.Text);

            ScoreReceipt();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="sale"></param>
        /// <returns></returns>
        private void PrintFooterOfReceipt(Sale sale)
        {
            WriteText("\t50");
            WriteText($"{Strings.SubTotal}\t25");
            WriteText($"{string.Format("{0:0.00}", sale.TotalExTax)}", "rght");
            WriteText("\t50");
            WriteText($"{Strings.Tax}\t25");
            WriteText($"{string.Format("{0:0.00}", sale.Total - sale.TotalExTax)}", "rght");
            WriteText("\t50");
            WriteText($"{Strings.Total}\t25");
            WriteText($"{string.Format("{0:0.00}", sale.Total)}", "rght");
            ScoreReceipt();

            var change = 0.0m;
            foreach (var payM in sale.PaySales)
            {
                WriteText($"{payM.PayMethod.Name}\t25");
                WriteText($"{string.Format("{0:0.00}", payM.Amount)}\tR20");
                WriteText($"\t55");
                change += payM.Change;
            }

            if (change > 0)
            {
                ScoreReceipt();
                WriteText("\t50");
                WriteText($"{Strings.Change}\t25");
                WriteText($"{string.Format("{0:0.00}", change)}\tR25");
            }

            BlankLine();
            WriteBarcode(sale.Id.ToString(), Settings.BarcodeSymbologySetting, 100, "cntr");
        }
        #endregion


        private Task PdfGeneration(Sale sale, Store store, System.IO.Stream image, decimal cashBack)
        {
            throw new NotImplementedException();
        }
    }
}
