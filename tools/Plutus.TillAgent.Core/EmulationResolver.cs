using System;
using System.Text.RegularExpressions;

namespace Plutus.TillAgent.Core
{
    /// <summary>
    /// FE3.1/3.3: which route to use for the selected printer queue.
    ///
    /// "Out of the box" is the whole point: a TSP100/TSP143 (futurePRNT) understands ONLY Star
    /// raster graphics — raw ESC/POS prints nothing, and even a raw raster job cannot be trusted
    /// because the futurePRNT queue was seen text-rendering datatype-RAW jobs (field evidence
    /// 2026-08-07). For that family, Auto therefore picks the GDI route: print the receipt bitmap
    /// through the vendor driver as a normal job, which is what that driver is designed for
    /// (it trims the page to the printed length and cuts). Everything else gets raw ESC/POS.
    /// Explicit settings exist for renamed queues and for forcing a specific path.
    /// </summary>
    public static class EmulationResolver
    {
        public const string Auto = "auto";
        public const string EscPos = "escpos";
        public const string StarRasterMode = "star-raster";
        public const string Gdi = "gdi";

        /// <summary>Resolve a configured mode + queue name to "escpos", "gdi" or "star-raster".</summary>
        public static string Resolve(string? configured, string? printerName)
        {
            if (string.Equals(configured, EscPos, StringComparison.OrdinalIgnoreCase)) return EscPos;
            if (string.Equals(configured, StarRasterMode, StringComparison.OrdinalIgnoreCase)) return StarRasterMode;
            if (string.Equals(configured, Gdi, StringComparison.OrdinalIgnoreCase)) return Gdi;

            var name = printerName ?? string.Empty;
            // TSP100/TSP113/TSP143 (any suffix letters) — but NOT the TSP100IV, which is a later
            // product that speaks StarPRNT/ESC-POS, not the futurePRNT raster-only protocol.
            var futurePrnt = Regex.IsMatch(name, @"TSP1(00|13|43)", RegexOptions.IgnoreCase)
                             && !name.Contains("IV", StringComparison.OrdinalIgnoreCase);
            return futurePrnt ? Gdi : EscPos;
        }
    }
}
