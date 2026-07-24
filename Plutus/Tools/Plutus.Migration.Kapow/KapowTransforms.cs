using System;
using System.Collections.Generic;
using System.Globalization;
using Plutus.SharedKernel;

namespace Plutus.Migration.Kapow
{
    /// <summary>F3: money is decimal TEXT in the Kapow DB ('3.49', '1.5', '0.0'). Parse to
    /// integer pence, culture-invariant, rounded to the nearest penny.</summary>
    public static class KapowMoney
    {
        public static bool TryParsePence(string? decimalText, out long pence)
        {
            pence = 0;
            if (string.IsNullOrWhiteSpace(decimalText)) return false;
            if (!decimal.TryParse(decimalText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var pounds))
                return false;
            pence = (long)Math.Round(pounds * 100m, MidpointRounding.AwayFromZero);
            return true;
        }

        public static long ParsePence(string? decimalText) =>
            TryParsePence(decimalText, out var p) ? p
                : throw new FormatException($"Unparseable money value '{decimalText}'.");
    }

    /// <summary>F2: VAT is a mutable multiplier (Vats.Rate ×1.2/×1.05/×1.0) applied to the
    /// ex-VAT price. Convert to basis points and reconstruct the per-line VAT amount from the
    /// VAT-inclusive line gross (flagged VatReconstructed downstream).</summary>
    public static class KapowVat
    {
        /// <summary>×1.2 → 2000 bp (20%); ×1.05 → 500; ×1.0 → 0.</summary>
        public static int ToBasisPoints(double multiplier)
            => (int)Math.Round((multiplier - 1.0) * 10000.0, MidpointRounding.AwayFromZero);

        /// <summary>VAT contained in a VAT-inclusive amount: inc − round(inc / multiplier).</summary>
        public static long VatFromInclusive(long incPence, double multiplier)
        {
            if (multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(multiplier));
            if (Math.Abs(multiplier - 1.0) < 1e-9) return 0;
            var exPence = (long)Math.Round(incPence / multiplier, MidpointRounding.AwayFromZero);
            return incPence - exPence;
        }
    }

    /// <summary>Kapow timestamps are local, zoneless text ('2026-07-23 14:05:34.6186178').
    /// Convert to UTC and derive the device-local BusinessDay.</summary>
    public static class KapowTime
    {
        public static readonly TimeZoneInfo UkZone = ResolveUk();

        private static TimeZoneInfo ResolveUk()
        {
            foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* try next */ }
            return TimeZoneInfo.Utc; // last resort; migration logs the fallback
        }

        public static bool TryParseLocal(string? text, out DateTime local)
            => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out local);

        public static DateTime ToUtc(string? text, TimeZoneInfo? zone = null)
        {
            if (!TryParseLocal(text, out var local))
                throw new FormatException($"Unparseable timestamp '{text}'.");
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone ?? UkZone);
        }

        public static DateOnly BusinessDay(string? text)
            => TryParseLocal(text, out var local) ? DateOnly.FromDateTime(local)
                : throw new FormatException($"Unparseable timestamp '{text}'.");
    }

    /// <summary>F1/F4: the Kapow keys (timestamp sale-IDs, barcode item-IDs, pseudo-items) are
    /// remapped to minted UUIDv7 surrogate keys, deterministically within one migration run.
    /// The old key is preserved by the caller (e.g. Sales.LegacyRef, Barcodes table).</summary>
    public sealed class IdRemap<TOld> where TOld : notnull
    {
        private readonly Dictionary<TOld, Guid> _map = new();

        public Guid GetOrMint(TOld oldId)
        {
            if (!_map.TryGetValue(oldId, out var id))
            {
                id = Uuid7.New();
                _map[oldId] = id;
            }
            return id;
        }

        public bool TryGet(TOld oldId, out Guid id) => _map.TryGetValue(oldId, out id);
        public int Count => _map.Count;
    }
}
