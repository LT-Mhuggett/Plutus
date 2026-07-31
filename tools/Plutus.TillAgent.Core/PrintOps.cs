using System.Collections.Generic;

namespace Plutus.TillAgent.Core
{
    /// <summary>
    /// FE3 the print wire contract: what the till sends the agent.
    ///
    /// ⚠ Design: the till sends a RENDERED DOCUMENT (these ops), not the raw sale. Receipt layout —
    /// the per-store template, header/footer lines, VAT number, whether to print the barcode —
    /// already lives in the till and is cached there per store. Shipping the sale instead would mean
    /// re-implementing that layout inside the agent and keeping two copies in step forever; the first
    /// template change would silently print the old receipt on hardware and the new one on PDF.
    ///
    /// The vocabulary is deliberately the one the codebase already speaks: it mirrors
    /// <c>CommonPOSLibrary.PrinterBaseOperations</c> (text with align/bold/underline, barcode, cut),
    /// which the Xamarin/MAUI tills build their receipts with.
    /// </summary>
    public enum PrintOpKind
    {
        /// <summary>A line of text (empty = blank line).</summary>
        Text = 0,
        /// <summary>A horizontal rule of dashes, printer-width.</summary>
        Rule = 1,
        /// <summary>A Code 39 barcode.</summary>
        Barcode = 2,
        /// <summary>Feed and cut the paper.</summary>
        Cut = 3,
        /// <summary>Kick the cash drawer.</summary>
        Drawer = 4,
    }

    public enum PrintAlign { Left = 0, Centre = 1, Right = 2 }

    /// <summary>One instruction. A record so the JSON shape stays obvious on the wire.</summary>
    public sealed class PrintOp
    {
        public PrintOpKind Kind { get; set; }
        public string? Text { get; set; }
        public PrintAlign Align { get; set; }
        public bool Bold { get; set; }
        public bool Underline { get; set; }
        /// <summary>Double-height + double-width (the receipt total, the shop name).</summary>
        public bool Large { get; set; }
        /// <summary>Barcode height in dots (default 80 ≈ 10mm).</summary>
        public int Height { get; set; } = 80;

        public static PrintOp Line(string? text = null, PrintAlign align = PrintAlign.Left,
                                   bool bold = false, bool large = false, bool underline = false) =>
            new() { Kind = PrintOpKind.Text, Text = text ?? string.Empty, Align = align, Bold = bold, Large = large, Underline = underline };

        public static PrintOp Rule() => new() { Kind = PrintOpKind.Rule };
        public static PrintOp Barcode(string content, int height = 80) =>
            new() { Kind = PrintOpKind.Barcode, Text = content, Align = PrintAlign.Centre, Height = height };
        public static PrintOp Cut() => new() { Kind = PrintOpKind.Cut };
        public static PrintOp Drawer() => new() { Kind = PrintOpKind.Drawer };
    }

    /// <summary>A whole print job.</summary>
    public sealed class PrintDocument
    {
        public List<PrintOp> Ops { get; set; } = new();
        /// <summary>Characters per line for the paper in use (80mm ≈ 42 at Font A, 58mm ≈ 32).
        /// Drives the rule width and right-alignment padding when the printer can't align.</summary>
        public int Columns { get; set; } = 42;
        /// <summary>Kick the drawer as part of this job (a cash sale) — saves a second round trip.</summary>
        public bool OpenDrawer { get; set; }
    }
}
