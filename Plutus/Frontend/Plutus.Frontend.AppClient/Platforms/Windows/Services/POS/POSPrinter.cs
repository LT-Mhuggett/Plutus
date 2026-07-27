using CommonPOSLibrary.Enums;
using CommonPOSLibrary.Exceptions;
using Plutus.Frontend.AppClient.Platforms.Windows.Services.POS.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.PointOfService;
using Windows.Graphics.Imaging;

namespace Plutus.Frontend.AppClient.Platforms.Windows.Services.POS
{
    public class POSPrinter : POSObjectContract<PosPrinter>
    {
        #region Fields
        private PosPrinter _printer;
        private ClaimedPosPrinter _claimedPrinter;
        private ReceiptPrintJob _printJob;
        #endregion

        #region Properties
        protected ReceiptPrintJob PrintJob
        {
            get
            {
                if (_printJob != null)
                    return _printJob;
                else
                {
                    if (_claimedPrinter != null)
                    {
                        _printJob = _claimedPrinter.Receipt.CreateJob();
                        _printJob.Print(EscPosComands.InitializePrinter);
                        return _printJob;
                    }
                    else
                        throw new POSPrinterException(POSPrinterExceptionType.PrinterNotClaimed, $"Printer has not be claimed, to create a PrintJob the printer must be claimed first.");
                }
            }
        }
        #endregion

        public POSPrinter(string deviceId) : base(deviceId)
        {
        }

        #region Printer Initialization

        public async override Task CreatePOSObject()
        {
            _printer = await PosPrinter.FromIdAsync(DeviceId);
            if (_printer == null)
                throw new POSObjectException(POSObjectExceptionType.NotFound, POSTargetObjectType.Printer, $"Printer with Id: {DeviceId}, not found.");
        }

        public async override Task InitPOSObject()
        {
            if (_printer.Status.StatusKind == PosPrinterStatusKind.Online)
            {
                _claimedPrinter = await _printer.ClaimPrinterAsync();

                if (_claimedPrinter != null)
                {
                    if (await _claimedPrinter.EnableAsync())
                    {
                        return;
                    }

                    _claimedPrinter.Dispose();
                    throw new POSObjectException(POSObjectExceptionType.NotEnableable, POSTargetObjectType.Printer, $"Printer with Id: {DeviceId}, is currently not enableable.");
                }
                throw new POSObjectException(POSObjectExceptionType.NotClaimable, POSTargetObjectType.Printer, $"Printer with Id: {DeviceId}, is currently in use by another process. Please wait.");
            }
            throw new POSObjectException(POSObjectExceptionType.OffOrOffline, POSTargetObjectType.Printer, $"Printer with Id: {DeviceId}, is off/offline.");
        }

        protected override Task<PosPrinter> GetFirstPOSObjectAsync(PosConnectionTypes posConnectionTypes = PosConnectionTypes.All)
        {
            throw new NotImplementedException();
        }
        #endregion

        public static Dictionary<string, object> GetCapabilites(PosPrinter printer)
        {
            return new Dictionary<string, object>
            {
                {"Can print Black and secondary colour", printer.Capabilities.Receipt.IsDualColorSupported },
                {"Can print BarCode", printer.Capabilities.Receipt.IsBarcodeSupported },
                {"Can print Image", printer.Capabilities.Receipt.IsBitmapSupported },
                {"Can print Bold", printer.Capabilities.Receipt.IsBoldSupported },
                {"Can print Double Character Height", printer.Capabilities.Receipt.IsDoubleHighPrintSupported },
                {"Can print Double Character Wide", printer.Capabilities.Receipt.IsDoubleWidePrintSupported },
                {"Can print Double Character Height and Wide", printer.Capabilities.Receipt.IsDoubleHighDoubleWidePrintSupported },
                {"Can print Italics", printer.Capabilities.Receipt.IsItalicSupported },
                {"Can print in Left 90 Degree rotation", printer.Capabilities.Receipt.IsLeft90RotationSupported },
                {"Can print in right 90 Degree rotation", printer.Capabilities.Receipt.IsRight90RotationSupported },
                {"Can print in 180 Degree rotation", printer.Capabilities.Receipt.Is180RotationSupported },
                {"Can print Underlined", printer.Capabilities.Receipt.IsUnderlineSupported },
                {"Has Low-Paper Sensor", printer.Capabilities.Receipt.IsPaperNearEndSensorSupported },
                {"Has Out-Of-Paper Sensor", printer.Capabilities.Receipt.IsPaperEmptySensorSupported },
                {"Can Cut Paper", printer.Capabilities.Receipt.CanCutPaper }
            };
        }

        public uint PageChars() => _claimedPrinter.Receipt.CharactersPerLine;

        #region Heads of Sections
        /// <summary>
        /// Takes multiple combinations that fire seperate events based on the <c>key</c> 
        /// of the KeyValuePair suplied.
        /// </summary>
        /// <remarks>
        /// <para>
        /// String:
        /// Key:<c>"str.algn?.bold?.udrl?"</c>
        /// </para>
        /// <para>
        /// Barcode:
        /// Key:<c>"brc.bcsymbol.algn?.hght.txtp?"</c>
        /// </para>
        /// <para>
        /// Image:
        /// Key:<c>"img.algn?.wdth?"</c>
        /// </para>
        /// Cut:
        /// <para>
        /// Key:<c>"cut"</c>
        /// </para>
        /// <para>
        /// Options:
        /// algn: "cntr" = center align, "rght" = right align, "" = left align
        /// bold: "true" = bold on, "" = bold off
        /// udrl: "true" = underline on, "" = underline off
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// key = "str.cntr"
        /// value = "test Text"
        /// </code>
        /// Prints test Text centered
        /// </example>
        /// <param name="keyValuePairs"></param>
        public async Task PrintMultiLine(List<KeyValuePair<string, object>> keyValuePairs)
        {
            foreach (var kvp in keyValuePairs)
            {
                var keyArray = kvp.Key.Split(new[] { '.' }, StringSplitOptions.None);
                switch (keyArray[0])
                {
                    case "str":
                        {
                            var val = kvp.Value as string;
                            PrintLineNoCut($"{GetStrOffset(keyArray[1])}{GetStrBold(keyArray[2])}{GetStrUnderline(keyArray[3])}", $"{GetStrOffset("")}{GetStrBold(keyArray[2], true)}{GetStrUnderline(keyArray[3], true)}", val);
                            break;
                        }
                    case "brc":
                        {
                            var val = kvp.Value as string;
                            //Barcode symbologies need to be implemented to print
                            PrintBarcode(val, BarcodeSymbologies.Code128, Convert.ToUInt32(keyArray[3]), GetBarcodeTextPosition(keyArray[4]), GetPrinterAlignment(keyArray[2]));
                            break;
                        }
                    case "img":
                        {
                            //Implement Imag print
                            break;
                        }
                    case "score":
                        {
                            PrintLineNoCut(new string('-', Convert.ToInt32(_claimedPrinter.Receipt.CharactersPerLine)) + "\n", "");
                            break;
                        }
                    case "cut":
                        {
                            PrintLineCut("\n\n\n");
                            break;
                        }
                }
            }

            var currentCharSet = _claimedPrinter.CharacterSet;
           

             await PrintJob.ExecuteAsync();
        }
        #endregion

        #region Basic IO
        #region Printing
        /// <summary>
        /// Print Single-Line work out overflow and autofeed issues
        /// Then cut paper
        /// </summary>
        /// <param name="line"></param>
        private void PrintLineCut(string line)
        {
            PrintLineNoCut(line, "");
            PrintJob.CutPaper(90);
        }

        /// <summary>
        /// Print text
        /// Then cut paper
        /// </summary>
        /// <param name="text"></param>
        private void PrintTextCut(string text)
        {
            PrintTextNoCut(text);
            PrintJob.CutPaper(90);
        }

        /// <summary>
        /// Print Single-Line work out overflow and autofeed issues
        /// </summary>
        /// <param name="formatting">The Line to be printed</param>
        private void PrintLineNoCut(string formatting, string formatReset, string lineContent = "")
        {
            if (_claimedPrinter.Receipt.CharactersPerLine != 0)
            {
                var split = lineContent.Split(new[] { "\t" }, StringSplitOptions.None);
                string toPrint;
                if (split.Length > 1)
                {
                    var rightJust = false;
                    if (split[1][0] == 'R')
                    {
                        rightJust = true;
                        split[1] = split[1].Replace("R", "");
                    }
                    var charsToAdd = (int)Math.Round((Convert.ToDecimal(split[1]) / 100 * _claimedPrinter.Receipt.CharactersPerLine) - split[0].Length, MidpointRounding.AwayFromZero);
                    if (charsToAdd < 0)
                    {
                        toPrint = TruncateAt(split[0], (uint)Math.Round(Convert.ToDecimal(split[1]) / 100 * _claimedPrinter.Receipt.CharactersPerLine, MidpointRounding.AwayFromZero) - 1);
                        if (rightJust)
                            toPrint = new string(' ', charsToAdd) + toPrint;
                        else
                            toPrint += new string(' ', 1);
                    }
                    else
                    {
                        if (rightJust)
                            toPrint = new string(' ', charsToAdd) + split[0];
                        else
                            toPrint = split[0] + new string(' ', charsToAdd);
                    }

                    toPrint = formatting + toPrint + formatReset;
                    PrintJob.Print(toPrint);
                    return;
                }

                toPrint = formatting + lineContent + formatReset;
                if (toPrint.Length < _claimedPrinter.Receipt.CharactersPerLine)
                    PrintJob.PrintLine(toPrint);
                else if (toPrint.Length > _claimedPrinter.Receipt.CharactersPerLine)
                    PrintJob.Print(TruncateAt(toPrint, _claimedPrinter.Receipt.CharactersPerLine));
                else
                    PrintJob.Print(toPrint);
            }
            else
                PrintJob.Print(formatting + lineContent + formatReset);
        }

        /// <summary>
        /// Print text 
        /// </summary>
        /// <param name="text">Text to be printed</param>
        private void PrintTextNoCut(string text)
        {
            if (text.Length <= _claimedPrinter.Receipt.CharactersPerLine)
                PrintJob.Print(text);
            else
                PrintJob.Print(TruncateAt(text, _claimedPrinter.Receipt.CharactersPerLine));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="alignment"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="barCodeTextPosition"></param>
        private void PrintBarcode(string text, uint barcodeSymbology, uint height, PosPrinterBarcodeTextPosition barcodeTextPosition, PosPrinterAlignment alignment)
        {
            PrintJob.PrintBarcode(text, barcodeSymbology, height, (uint)(_claimedPrinter.Receipt.CharactersPerLine * .8), barcodeTextPosition, alignment);
        }

        private void PrintImage(BitmapFrame bmp, PosPrinterAlignment alignment)
        {
            PrintJob.PrintBitmap(bmp, alignment);
        }
        #endregion

        /// <summary>
        /// Enable bold if printer is capable 
        /// </summary>
        /// <param name="option"></param>
        /// <returns></returns>
        private string GetStrBold(string option, bool turnOff = false)
        {
            switch (option)
            {
                case "true":
                    if (_printer.Capabilities.Receipt.IsBoldSupported)
                    {
                        if (turnOff)
                            return EscPosComands.BoldOff;
                        else
                            return EscPosComands.BoldOn;
                    }
                    else
                        return "";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Enable underline if printer is capable 
        /// </summary>
        /// <param name="option"></param>
        /// <returns></returns>
        private string GetStrUnderline(string option, bool turnOff = false)
        {
            switch (option)
            {
                case "true":
                    if (_printer.Capabilities.Receipt.IsUnderlineSupported)
                    {
                        if (turnOff)
                            return EscPosComands.Underline_Off;
                        else
                            return EscPosComands.Underline_On_Thin;
                    }
                    return "";

                default:
                    return "";

            }
        }

        #region Offsets

        /// <summary>
        /// Enable correct text alignment
        /// </summary>
        /// <param name="option"></param>
        /// <returns></returns>
        private string GetStrOffset(string option)
        {
            switch (option)
            {
                case "cntr":
                    return EscPosComands.Align_Center;
                case "rght":
                    return EscPosComands.Align_Right;
                default:
                    return EscPosComands.Align_Left;
            }
        }

        /// <summary>
        /// Get alignment for Barcode and Image
        /// </summary>
        /// <param name="option"></param>
        /// <returns></returns>
        private PosPrinterAlignment GetPrinterAlignment(string option)
        {
            switch (option)
            {
                case "cntr":
                    return PosPrinterAlignment.Center;
                case "rght":
                    return PosPrinterAlignment.Right;
                default:
                    return PosPrinterAlignment.Left;
            }
        }

        /// <summary>
        /// Get character offset for center text aligment
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        private static int CenterOffset(string text, int maxLength) => (maxLength - text.Length) / 2;

        /// <summary>
        /// Get character offset for right text alignment 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        private static int RightAlighOffset(string text, int maxLength) => maxLength - text.Length;
        #endregion

        /// <summary>
        /// Get Barcode text position enum
        /// </summary>
        /// <param name="option"></param>
        /// <returns></returns>
        private PosPrinterBarcodeTextPosition GetBarcodeTextPosition(string option)
        {
            switch (option)
            {
                case "abve":
                    return PosPrinterBarcodeTextPosition.Above;
                case "belw":
                    return PosPrinterBarcodeTextPosition.Below;
                case "none":
                    return PosPrinterBarcodeTextPosition.None;
                default:
                    return PosPrinterBarcodeTextPosition.Below;
            }
        }
        /// <summary>
        /// Truncate <c>line</c> to remove overflow
        /// </summary>
        /// <param name="line"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        /// <exception cref="OverflowException"></exception>
        private string TruncateAt(string text, uint maxLength)
        {
            int maxLengthSafe = checked((int)maxLength);
            var retVal = text;
            if (text.Length > maxLengthSafe)
                retVal = text.Substring(0, maxLengthSafe);
            return retVal;
        }

        public override void Dispose()
        {
            _printer.Dispose();
        }
        #endregion
    }
}
