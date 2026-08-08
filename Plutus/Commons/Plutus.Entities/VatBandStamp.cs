using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Entities
{
    /// <summary>
    /// WP2c-exempt: fill in a sale line's VAT BAND server-side when the channel didn't state one.
    ///
    /// WHY THIS EXISTS RATHER THAN "EVERY CLIENT MUST REMEMBER". A band that only arrives when a till
    /// bothers to send it is a band that goes missing — and the failure is silent and unrecoverable:
    /// zero-rated and exempt supplies both record 0%, so a line with no band cannot be classified
    /// later, and the shop's partial-exemption figure is wrong with nothing to show it (HMRC Notice
    /// 706). There are already three ways a sale can reach this database (web till, MAUI, the
    /// webstore connector) and more platforms are expected, so "consistent because each client was
    /// written correctly" is not a guarantee. This makes it structural: the SERVER resolves the band
    /// from the catalogue, which is the same mapping the portal owns.
    ///
    /// ⚠ A BAND THE CLIENT STATED IS NEVER OVERWRITTEN. The till knows things the catalogue does not:
    /// a single-purpose gift-card line is standard-rated by the voucher treatment even though its
    /// catalogue row sits on a zero band. Client statement wins; this only fills gaps.
    ///
    /// ⚠ A line that stays null is CORRECT, not a failure. It means the tenant has two bands at one
    /// rate (zero-rated and exempt) and nobody has said which this tax row is. Guessing would put a
    /// number on a VAT return that no human chose; the report shows it as unclassified and the portal
    /// flags the tax row for a decision.
    /// </summary>
    public static class VatBandStamp
    {
        /// <summary>
        /// Stamp every line that has no <see cref="SaleLine.VatBand"/>, resolving each one from its
        /// barcode → the item's legacy tax row → the portal's band mapping (or the rate, where that
        /// is unambiguous).
        ///
        /// Batched: two small reads plus one item lookup for the whole sale, not per line.
        /// Lines with no barcode (shipping, fees, the reconciliation sentinel) are left alone —
        /// there is no catalogue row to ask.
        /// </summary>
        public static async Task StampAsync(
            MySqlDbContext db, IReadOnlyCollection<SaleLine> lines, CancellationToken ct = default)
        {
            if (lines == null || lines.Count == 0) return;

            var needing = lines
                .Where(l => string.IsNullOrWhiteSpace(l.VatBand) && !string.IsNullOrWhiteSpace(l.ItemIdOne))
                .ToList();
            if (needing.Count == 0) return;

            var bands = await BandsAsync(db, ct);
            if (bands.Count == 0) return;    // nothing published yet — leave null, do not invent

            var barcodes = needing.Select(l => l.ItemIdOne!).Distinct().ToList();
            var taxByBarcode = await db.Items.AsNoTracking().IgnoreQueryFilters()
                .Where(i => barcodes.Contains(i.IdOne))
                .Select(i => new { i.IdOne, i.TaxId })
                .ToListAsync(ct);
            if (taxByBarcode.Count == 0) return;

            var taxIds = taxByBarcode.Select(t => t.TaxId).Distinct().ToList();
            var maps = await db.VatBandTaxMaps.AsNoTracking()
                .Where(m => taxIds.Contains(m.LegacyTaxId))
                .ToDictionaryAsync(m => m.LegacyTaxId, m => m.Band, ct);
            var rates = await db.Taxes.AsNoTracking().IgnoreQueryFilters()
                .Where(t => taxIds.Contains(t.IdOne))
                .Select(t => new { t.IdOne, t.Rate })
                .ToDictionaryAsync(t => t.IdOne, t => t.Rate, ct);

            // barcode → resolved band, computed once per distinct tax row.
            var bandByTax = new Dictionary<int, string?>();
            foreach (var taxId in taxIds)
            {
                var bp = rates.TryGetValue(taxId, out var rate) ? (int)Math.Round((rate - 1d) * 10000d) : 0;
                bandByTax[taxId] = VatBandResolution.Resolve(bands, maps.GetValueOrDefault(taxId), bp);
            }
            var bandByBarcode = taxByBarcode
                .GroupBy(t => t.IdOne)
                .ToDictionary(g => g.Key, g => bandByTax.GetValueOrDefault(g.First().TaxId));

            foreach (var line in needing)
                if (bandByBarcode.TryGetValue(line.ItemIdOne!, out var band) && band != null)
                    line.VatBand = band;
        }

        /// <summary>
        /// The tenant's bands as published RIGHT NOW — the same shape and the same "latest point at
        /// or before now" rule the published contract uses, so the server can never resolve a band
        /// the tills were not told about.
        /// </summary>
        private static async Task<List<VatBand>> BandsAsync(MySqlDbContext db, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var points = await db.VatRatePoints.AsNoTracking()
                .Select(p => new { p.Band, p.DisplayName, p.Class, p.RateBp, p.EffectiveFromUtc })
                .ToListAsync(ct);

            return points
                .GroupBy(p => p.Band, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var ordered = g.OrderBy(p => p.EffectiveFromUtc).ToList();
                    var current = ordered.LastOrDefault(p => p.EffectiveFromUtc <= now) ?? ordered[0];
                    return new VatBand(g.Key, ordered[^1].DisplayName ?? g.Key,
                        (VatClass)ordered[^1].Class, current.RateBp, current.EffectiveFromUtc);
                })
                .ToList();
        }
    }
}
