using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Plutus.Models;
#if WINDOWS_UWP
using Windows.Devices.PointOfService;
using Windows.Devices.Enumeration;
#endif

namespace Plutus.Helpers
{
    class PosPrinterManager
    {
#if WINDOWS_UWP
        private PosPrinter Printer;
        private ClaimedPosPrinter ClaimedPrinter;
        private bool PrinterClaimed = false;
        private string ESC = "";
        private string GS = "";
        private string InitializePrinter = "";
        private string BoldOn = "";
        private string BoldOff = "";
        private string DoubleOn = "";  // 2x sized text (double-high + double-wide)
        private string DoubleOff = "";


        public PosPrinterManager()
        {
        }

        public async Task EnablePrinter()
        {
            Printer = await GetFirstReceiptPrinterAsync();
            PrinterClaimed = (ClaimedPrinter = await Printer.ClaimPrinterAsync()) != null;
        }

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
             */
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
                text += $"{@return.Item.Name}\t{@return.Item.Id}\t{ESC+"|rA"}{@return.Item.Price:C}{ESC+"|lA"}\n";
            text += $"Note:\t{@return.Reason}\n";
        }

        private void SetFormatForSaleInforationPrint(SaleModel sale, ref ReceiptPrintJob job)
        {
            var text = $"Total:\t\t{sale.Total:C}\n";
            var change = 0.0m;
            foreach (var paySale in sale.PaySales)
            {
                text += $"{paySale.PayMethod.Name}\t\t{ESC+"|rA"}{paySale.Amount}{ESC+"|lA"}\n";
                change += paySale.Change;
            }

            text += $"Change\t\t{ESC+"|rA"}{change:C}{ESC+"|lA"}";
            PrintLineFeedNoCut(ref job, text);
        }
        #endregion

        #region HeadsOfSections
        private void PrintHeaderOfReceipt(ref ReceiptPrintJob job, StoreModel store, SaleModel sale)
        {
            var text = $"{ESC+"|cA"}{BoldOn}Thank you for shopping at\n" +
                          $"{DoubleOn}{store.StoreName}{DoubleOff}\n";
            text += store.FullAddress ?? 
                    $"{store.AdLine1}\n" +
                    $"{store.AdLine2}\n" +
                    $"{store.PostCode}\n" +
                    $"{store.Country}";
            text += "\nOn\n" +
                    $"{DoubleOn}{sale.DateOfSale:D}\n" +
                    $"{sale.DateOfSale:T}{DoubleOff}{BoldOff}{ESC+"|lA"}\n";
            PrintLineFeedNoCut(ref job, text);
        }

        private void PrintHeadOfTransaction(ref ReceiptPrintJob job)
        {
            var text = $"{BoldOn}Item Name\tItem ID\t{ESC+"|rA"}Price{ESC+"|lA"}{BoldOff}\n";
            PrintLineFeedNoCut(ref job, text);
        }

        private void PrintFooterOfReceipt(ref ReceiptPrintJob job)
        {
            var text = $"{BoldOn}Please come again soon!{BoldOff}";
            PrintLineFeedCutAfter(ref job, text);
        }
        #endregion

        #region BasicPrintFunctionality
        private void PrintLineFeedNoCut(ref ReceiptPrintJob job, string multiLineText)
        {
            job.Print(multiLineText);
        }

        private void PrintLineFeedCutAfter(ref ReceiptPrintJob job, string multiLineText)
        {
            string feedString = "";
            for (uint n = 0; n < ClaimedPrinter.Receipt.LinesToPaperCut; n++)
            {
                feedString += "\n";
            }

            job.Print(multiLineText + feedString);
            if (Printer.Capabilities.Receipt.CanCutPaper)
                job.CutPaper();
        }

        private async Task<Tuple<bool, string>> ExecuteJobAndReport(ReceiptPrintJob job)
        {
            bool success = await job.ExecuteAsync();

            string message = "";

            if (success)
            {
                message = "Print Successful";
            }
            else
            {
                if (ClaimedPrinter.Receipt.IsCartridgeEmpty)
                {
                    message = "Printer is out of ink. Please replace cartridge.";
                }
                else if (ClaimedPrinter.Receipt.IsCartridgeRemoved)
                {
                    message = "Printer cartridge is missing. Please replace cartridge.";
                }
                else if (ClaimedPrinter.Receipt.IsCoverOpen)
                {
                    message = "Printer cover is open. Please close it.";
                }
                else if (ClaimedPrinter.Receipt.IsHeadCleaning)
                {
                    message = "Printer is currently cleaning the cartridge. Please wait until cleaning has completed.";
                }
                else if (ClaimedPrinter.Receipt.IsPaperEmpty)
                {
                    message = "Printer is out of paper. Please insert a new roll.";
                }
                else
                {
                    message = "Unable to print.";
                }
            }

            return Tuple.Create(success, message);
        }
        #endregion

        #region GetDeviceAndClaim
        private static async Task<T> GetFirstDeviceAsync<T>(string selector, Func<string, Task<T>> convertAsync) where T : class
        {
            var completionSource = new TaskCompletionSource<T>();
            var pendingTasks = new List<Task>();
            DeviceWatcher watcher = DeviceInformation.CreateWatcher(selector);

            watcher.Added += (DeviceWatcher sender, DeviceInformation device) =>
            {
                Func<string, Task> lamda = async (id) =>
                {
                    T t = await convertAsync(id);
                    if (t != null)
                        completionSource.TrySetResult(t);
                };
                pendingTasks.Add(lamda(device.Id));
            };

            watcher.EnumerationCompleted += async (DeviceWatcher sender, object args) =>
            {
                await Task.WhenAll(pendingTasks);
                completionSource.TrySetResult(null);
            };

            watcher.Start();

            T result = await completionSource.Task;

            watcher.Stop();
            return result;
        }

        private static async Task<PosPrinter> GetFirstReceiptPrinterAsync(
            PosConnectionTypes connectionTypes = PosConnectionTypes.All)
        {
            return await GetFirstDeviceAsync(PosPrinter.GetDeviceSelector(connectionTypes), async (id) =>
            {
                PosPrinter printer = await PosPrinter.FromIdAsync(id);
                if (printer != null && printer.Capabilities.Receipt.IsPrinterPresent)
                {
                    return printer;
                }

                printer?.Dispose();
                return null;
            });
        }
        #endregion
#endif
    }
}
