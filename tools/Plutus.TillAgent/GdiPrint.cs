using System;
using System.Drawing;
using System.Drawing.Printing;

namespace Plutus.TillAgent
{
    /// <summary>
    /// FE3.3: silent printing THROUGH the vendor driver as a normal GDI print job — the way the
    /// futurePRNT driver is designed to be used, and the default for TSP100-family printers.
    ///
    /// Why not bytes: the TSP100 futurePRNT queue was seen text-rendering even datatype-RAW jobs
    /// (glyph-soup receipt, 2026-08-07), and Windows has NO in-box USB PointOfService support for
    /// receipt printers (verified against Microsoft's supported-peripherals list — the "Direct"
    /// route needs Star's OPOS registered, which shop PCs running the old NatApp have but a fresh
    /// PC does not). GDI is the one path the driver itself owns end-to-end: it renders our
    /// pre-rasterised receipt bitmap, trims the page to the printed length ("Paper Type:
    /// Receipt") and cuts at document end — the same mechanics the browser fallback proved on
    /// this exact printer, minus the browser and its dialog.
    /// </summary>
    public static class GdiPrint
    {
        private const float PrinterDpi = 203f;   // thermal head; our bitmaps are rendered at this
        private const float GdiUnitsPerInch = 100f; // System.Drawing.Printing works in 1/100"

        public static void Print(string printerName, Bitmap receipt)
        {
            if (string.IsNullOrWhiteSpace(printerName)) throw new InvalidOperationException("No printer selected.");

            using var job = new PrintDocument();
            job.PrinterSettings = new PrinterSettings { PrinterName = printerName };
            if (!job.PrinterSettings.IsValid)
                throw new InvalidOperationException($"Printer '{printerName}' is not available.");

            job.DocumentName = "Plutus receipt";
            // StandardPrintController: no on-screen progress UI — this must be silent.
            job.PrintController = new StandardPrintController();
            job.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

            var wUnits = receipt.Width * GdiUnitsPerInch / PrinterDpi;
            var hUnits = receipt.Height * GdiUnitsPerInch / PrinterDpi;
            var printed = false;
            job.PrintPage += (_, e) =>
            {
                // one page, top-left; the driver's "Receipt" paper type trims the page length
                // to the last printed line, so the receipt's own height decides the paper used
                e.Graphics!.DrawImage(receipt, 0f, 0f, wUnits, hUnits);
                e.HasMorePages = false;
                printed = true;
            };
            job.Print();
            if (!printed) throw new InvalidOperationException("The driver produced no page.");
        }
    }
}
