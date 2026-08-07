using System;
using System.Text.RegularExpressions;

namespace Plutus.TillAgent.Core
{
    /// <summary>
    /// FE3.1: which language to speak to the selected printer.
    ///
    /// "Out of the box" is the whole point: a TSP100/TSP143 (futurePRNT) understands ONLY Star
    /// raster graphics — feed it ESC/POS and it prints nothing or feeds paper wildly. Auto mode
    /// recognises those models from the Windows queue name (the futurePRNT driver names itself
    /// "Star TSP100 Cutter (TSP143)" and similar) so nobody has to know what an emulation is.
    /// The explicit settings exist for renamed queues.
    /// </summary>
    public static class EmulationResolver
    {
        public const string Auto = "auto";
        public const string EscPos = "escpos";
        public const string StarRasterMode = "star-raster";

        /// <summary>Resolve a configured mode + printer name to "escpos" or "star-raster".</summary>
        public static string Resolve(string? configured, string? printerName)
        {
            if (string.Equals(configured, EscPos, StringComparison.OrdinalIgnoreCase)) return EscPos;
            if (string.Equals(configured, StarRasterMode, StringComparison.OrdinalIgnoreCase)) return StarRasterMode;

            var name = printerName ?? string.Empty;
            // TSP100/TSP113/TSP143 (any suffix letters) — but NOT the TSP100IV, which is a later
            // product that speaks StarPRNT/ESC-POS, not the futurePRNT raster-only protocol.
            var futurePrnt = Regex.IsMatch(name, @"TSP1(00|13|43)", RegexOptions.IgnoreCase)
                             && !name.Contains("IV", StringComparison.OrdinalIgnoreCase);
            return futurePrnt ? StarRasterMode : EscPos;
        }
    }
}
