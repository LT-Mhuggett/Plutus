using System.Collections.Generic;
using System.Text;

namespace CommonPOSLibrary
{
    public class PrinterBaseOperations
    {
        public List<KeyValuePair<string, object>> Lines { get; } = new List<KeyValuePair<string, object>>();

        /// <summary>
        /// Line score the receipt page
        /// </summary>
        public void ScoreReceipt()
        {
            Lines.Add(new KeyValuePair<string, object>("score...", ""));
        }

        /// <summary>
        /// Cuts the receipt paper
        /// </summary>
        public void CutPaper()
        {
            Lines.Add(new KeyValuePair<string, object>("cut...", ""));
        }


        /// <summary>
        /// Write text to receipt, can contain text decoration
        /// </summary>
        /// <param name="text">Text to write to receipt</param>
        /// <param name="align">alignment; default = left, "cntr" = center, "rght" = right</param>
        /// <param name="bold">default = no bold, "true" = bold</param>
        /// <param name="underline">default = no underline, "true" = underline</param>
        public void WriteText(string text, string align = default, string bold = default, string underline = default)
        {
            Lines.Add(new KeyValuePair<string, object>($"str.{align}.{bold}.{underline}", text));
        }

        /// <summary>
        /// Write barcode to receipt
        /// </summary>
        /// <param name="content">Content to encode to barcode</param>
        /// <param name="barcodeType">Barcode encoder</param>
        /// <param name="height">height of barcode</param>
        /// <param name="align">alignment; default = left, "cntr" = center, "rght" = right</param>
        /// <param name="contentPlacement">Content placement relative to barcode; default = below, "abve" = above, "belw" = below, "none" = No content placement around barcode</param>
        public void WriteBarcode(string content, string barcodeType, int height, string align = default, string contentPlacement = default)
        {
            Lines.Add(new KeyValuePair<string, object>($"brc.{barcodeType}.{align}.{height}.{contentPlacement}", content));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="content"></param>
        /// <param name="align"></param>
        /// <param name="width"></param>
        public void WriteImage(byte[] content, string align = default, int width = 200)
        {
            Lines.Add(new KeyValuePair<string, object>($"img.{align}.{width}", Encoding.UTF8.GetString(content, 0, content.Length)));
        }

        /// <summary>
        /// Adds a blank line to the receipt
        /// </summary>
        public void BlankLine()
        {
            WriteText("");
        }
    }
}
