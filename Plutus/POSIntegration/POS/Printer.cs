using Microsoft.PointOfService;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace POSIntegration.POS
{
    class POSPrinter : IDisposable
    {
        private static readonly string ESC = Encoding.ASCII.GetString(new byte[] { 27 });
        private readonly string BoldOn = $"{ESC}|bC";
        private readonly string AlignCenter = $"{ESC}|cA";
        private readonly string AlignRight = $"{ESC}|rA";
        private readonly string Underline = $"{ESC}|uC";
        private readonly string Reset = $"{ESC}|N";

        private PosExplorer _explorer;

        public string LogicalName { get; private set; }

        private PosPrinter Printer { get; set; }

        private void InitPrinter()
        {
            if (Printer.State == ControlState.Closed)
                Printer.Open();
            if (!Printer.Claimed)
                Printer.Claim(0);
            if (!Printer.DeviceEnabled)
                Printer.DeviceEnabled = true;
            var charList = Printer.CharacterSetList;
            Printer.CharacterSet = charList[charList.Length - 1];
        }

        public POSPrinter(ref PosExplorer posExplorer, string logicalName)
        {
            _explorer = posExplorer;
            LogicalName = logicalName;
            {
                var device = _explorer.GetDevice(DeviceType.PosPrinter, LogicalName);
                if (device == null)
                    throw new NullReferenceException($"Can't find the device by logicalName: {LogicalName}");
                Printer = _explorer.CreateInstance(device) as PosPrinter;
                if (Printer == null)
                    throw new NullReferenceException("Create instance of PosPrinter Failed.");
            }
            InitPrinter();
        }

        public static Dictionary<string, object> GetCapabilites(PosPrinter printer)
        {
            return new Dictionary<string, object>
            {
                {"Concurrent Receipt and Jorunal printing", printer.CapConcurrentJrnRec },
                {"Concurrent Receipt and Slip printing", printer.CapConcurrentRecSlp },
                {"Can print Black and secondary colour", printer.CapRec2Color },
                {"Can print BarCode", printer.CapRecBarCode },
                {"Can print Image", printer.CapRecBitmap },
                {"Can print Bold", printer.CapRecBold },
                {"Can print Double Character Height", printer.CapRecDHigh },
                {"Can print Double Character Wide", printer.CapRecDWide },
                {"Can print Double Character Wide and Wide", printer.CapRecDWideDHigh },
                {"Can print Italics", printer.CapRecItalic },
                {"Can print in Left 90 Degree rotation", printer.CapRecLeft90 },
                {"Can print in right 90 Degree rotation", printer.CapRecRight90 },
                {"Can print in 180 Degree rotation", printer.CapRecRotate180 },
                {"Can print Underlined", printer.CapRecUnderline },
                {"Has Low-Paper Sensor", printer.CapRecNearEndSensor },
                {"Has Out-Of-Paper Sensor", printer.CapRecEmptySensor },
                {"Can Cut Paper", printer.CapRecPaperCut }
            };
        }

        public int PageChars() => Printer.RecLineChars;

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
        public void PrintMultiLine(List<KeyValuePair<string, object>> keyValuePairs)
        {
            foreach (var kvp in keyValuePairs)
            {
                var keyArray = kvp.Key.Split(new[] { '.' }, StringSplitOptions.None);
                switch (keyArray[0])
                {
                    case "str":
                        {
                            var val = kvp.Value as string;
                            PrintLineNoCut($"{GetStrOffset(keyArray[1])}{GetStrBold(keyArray[2])}{GetStrUnderline(keyArray[3])}", val);
                            break;
                        }
                    case "brc":
                        {
                            try
                            {
                                var val = kvp.Value as string;
                                PrintBarcode(val, (BarCodeSymbology)Enum.Parse(typeof(BarCodeSymbology), keyArray[1]), GetBarcodeOffset(keyArray[2]), int.Parse(keyArray[3]), GetBarCodeTextPosition(keyArray[4]));
                            }
                            catch (PosControlException pCEx)
                            {
                                PrintLineNoCut("", kvp.Value as string);
                            }
                            break;
                        }
                    case "img":
                        {
                            try
                            {
                                var imageData = Encoding.UTF8.GetBytes(kvp.Value as string);
                                /*if (keyArray[2].Length == 0)
                                    PrintImage(image, GetImageOffset(keyArray[1]));
                                else
                                    PrintImage(image, GetImageOffset(keyArray[1]), int.Parse(keyArray[2]));*/
                            }
                            catch (PosControlException pCEx)
                            {
                                PrintLineNoCut("", "Image Printing Error");
                            }
                            break;
                        }
                    case "score":
                        {
                            PrintLineNoCut(new string('-', Printer.RecLineChars) + "\n");
                            break;
                        }
                    case "cut":
                        {
                            PrintLineCut("\n\n\n");
                            break;
                        }
                }
            }
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
            PrintLineNoCut(line);
            Printer.CutPaper(90);
        }

        /// <summary>
        /// Print text
        /// Then cut paper
        /// </summary>
        /// <param name="text"></param>
        private void PrintTextCut(string text)
        {
            PrintTextNoCut(text);
            Printer.CutPaper(90);
        }

        /// <summary>
        /// Print Single-Line work out overflow and autofeed issues
        /// </summary>
        /// <param name="formatting">The Line to be printed</param>
        private void PrintLineNoCut(string formatting, string lineContent = "")
        {
            if (Printer.RecLineChars != 0)
            {
                string toPrint = "";
                var split = lineContent.Split(new[] { "\t" }, StringSplitOptions.None);
                if (split.Length > 1)
                {
                    var rightJust = false;
                    if (split[1][0] == 'R')
                    {
                        rightJust = true;
                        split[1] = split[1].Replace("R", "");
                    }
                    int charsToAdd = (int)Math.Round(((Convert.ToDecimal(split[1]) / 100) * Printer.RecLineChars) - split[0].Length, MidpointRounding.AwayFromZero);
                    if (charsToAdd < 0)
                    {
                        toPrint = TruncateAt(split[0], (int)Math.Round(((Convert.ToDecimal(split[1]) / 100) * Printer.RecLineChars), MidpointRounding.AwayFromZero) - 1);
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
                    toPrint = formatting + toPrint;
                    Printer.PrintNormal(PrinterStation.Receipt, toPrint);
                    return;
                }
                toPrint = formatting + lineContent;
                if (toPrint.Length < Printer.RecLineChars)
                    Printer.PrintNormal(PrinterStation.Receipt, toPrint + Environment.NewLine);
                else if (toPrint.Length > Printer.RecLineChars)
                    Printer.PrintNormal(PrinterStation.Receipt, TruncateAt(toPrint, Printer.RecLineChars));
                else
                    Printer.PrintNormal(PrinterStation.Receipt, toPrint);
            }
            else
                Printer.PrintNormal(PrinterStation.Receipt, formatting + lineContent);
        }

        /// <summary>
        /// Print text 
        /// </summary>
        /// <param name="text">Text to be printed</param>
        private void PrintTextNoCut(string text)
        {
            if (text.Length <= Printer.RecLineChars)
                Printer.PrintNormal(PrinterStation.Receipt, text);
            else
                Printer.PrintNormal(PrinterStation.Receipt, TruncateAt(text, Printer.RecLineChars));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="alignment"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="barCodeTextPosition"></param>
        private void PrintBarcode(string text, BarCodeSymbology barCodeSymbology, int alignment, int height, BarCodeTextPosition barCodeTextPosition)
        {
            Printer.PrintBarCode(PrinterStation.Receipt, text, barCodeSymbology, height, (int)(Printer.RecLineWidth / 1.2), alignment, barCodeTextPosition);
        }

        private void PrintImage(Bitmap bmp, int alignment, int width = PosPrinter.PrinterBitmapAsIs)
        {
            Printer.PrintMemoryBitmap(PrinterStation.Receipt, bmp, width, alignment);
        }
        #endregion

        private string GetStrBold(string option)
        {
            switch (option)
            {
                case "true":
                    if (Printer.CapRecBold)
                        return BoldOn;

                    return "";

                default:
                    return "";
            }
        }

        private string GetStrUnderline(string option)
        {
            switch (option)
            {
                case "true":
                    return Underline;
                default:
                    return "";
            }
        }

        #region Offsets
        /// <summary>
        /// Choose text alignment
        /// </summary>
        /// <param name="option"></param>
        /// <returns></returns>
        private string GetStrOffset(string option)
        {
            switch (option)
            {
                case "cntr":
                    return AlignCenter;
                //return new string(' ', CenterOffset(text, Printer.RecLineChars));
                case "rght":
                    return AlignRight;
                //return new string(' ', RightAlignOffset(text, Printer.RecLineChars));
                default:
                    return "";
            }
        }

        private int GetBarcodeOffset(string option)
        {
            switch (option)
            {
                case "cntr":
                    return PosPrinter.PrinterBarCodeCenter;
                case "rght":
                    return PosPrinter.PrinterBarCodeRight;
                default:
                    return PosPrinter.PrinterBarCodeLeft;
            }
        }

        private int GetImageOffset(string option)
        {
            switch (option)
            {
                case "cntr":
                    return PosPrinter.PrinterBitmapCenter;
                case "rght":
                    return PosPrinter.PrinterBitmapRight;
                default:
                    return PosPrinter.PrinterBitmapLeft;
            }
        }

        #endregion

        private BarCodeTextPosition GetBarCodeTextPosition(string option)
        {
            switch (option)
            {
                case "abve":
                    return BarCodeTextPosition.Above;
                case "belw":
                    return BarCodeTextPosition.Below;
                case "none":
                    return BarCodeTextPosition.None;
                default:
                    return BarCodeTextPosition.Below;
            }
        }

        /// <summary>
        /// Calculate offset for centering text
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        private static int CenterOffset(string text, int maxLength) => (maxLength - text.Length) / 2;

        /// <summary>
        /// Calculate offset for right alingment
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        private static int RightAlignOffset(string text, int maxLength) => maxLength - text.Length;

        /// <summary>
        /// Truncate <c>line</c> to remove overflow
        /// </summary>
        /// <param name="line"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        private string TruncateAt(string text, int maxLength)
        {
            var retVal = text;
            if (text.Length > maxLength)
                retVal = text.Substring(0, maxLength);
            return retVal;
        }
        #endregion

        public void Dispose()
        {
            Printer.Release();
            Printer.Close();
        }
    }
}
