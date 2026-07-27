using System;
using System.Globalization;

namespace Plutus.Webstore
{
    /// <summary>Decimal-text → integer pence (the platform money unit). Woo returns money as
    /// culture-invariant decimal strings ("17.33", "0.00"); we never carry decimals past this line.</summary>
    internal static class WebstoreMoney
    {
        /// <summary>Parse a Woo money string to pence. Empty/null → 0. Throws <see cref="FormatException"/>
        /// on garbage (the mapper turns that into a quarantine, never a silent 0).</summary>
        public static long ParsePence(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var d = decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
            return (long)Math.Round(d * 100m, MidpointRounding.AwayFromZero);
        }

        /// <summary>A Woo GMT timestamp ("2025-06-05T17:09:49", no zone suffix but GMT by field
        /// convention) → a UTC-kinded DateTime.</summary>
        public static DateTime ParseGmt(string text)
            => DateTime.SpecifyKind(
                DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None),
                DateTimeKind.Utc);
    }
}
