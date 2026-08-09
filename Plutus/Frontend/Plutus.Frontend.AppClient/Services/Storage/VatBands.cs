using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Client.Storage;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>
    /// This till's view of the portal's VAT bands.
    ///
    /// ⚠ THE APP'S FIRST ROUTE TO `VatBandCache`. Phase 1 built `MetaVatBandStore` so the cache
    /// could finally be constructed; nothing in the app constructed it. Three times in this
    /// retrofit a fully-tested component has sat unreachable (WP5's sync spine, `RefundRules`,
    /// `IVatBandStore`), so the wiring is the deliverable, not the class.
    ///
    /// ⚠ NOTHING HERE MAY BLOCK SELLING. Every method answers null on any failure and the callers
    /// treat null as "not decided", never as "0%": a till that has never synced is UNINFORMED, and
    /// rendering that as zero-rated would put 0% on every line of a real sale.
    /// </summary>
    public static class VatBands
    {
        /// <summary>
        /// Which published band an item's legacy tax row means, or null when nothing says.
        ///
        /// ⚠ Null is a CORRECT answer and must travel as null: it means the tenant has two bands at
        /// one rate (zero and exempt are both 0%) and nobody has said which this row is. The server
        /// then falls back to snapping the rate and the portal reports the row as unclassified —
        /// whereas a guess here becomes an invented figure on a VAT return.
        /// </summary>
        public static Task<string?> BandKeyForItemAsync(Guid itemId, CancellationToken ct = default) =>
            ResolveAsync(itemId, (cache, taxId) => cache.BandKeyForTaxIdAsync(taxId, ct), ct);

        /// <summary>The band's human name, for a Tax column. Null renders blank, never "0%".</summary>
        public static async Task<string?> DisplayNameForItemAsync(Guid itemId, CancellationToken ct = default)
        {
            var key = await BandKeyForItemAsync(itemId, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(key)) return null;

            var bands = await TillStoreAccess.TryUseAsync(
                s => new MetaVatBandStore(s).LoadAsync(ct), ct).ConfigureAwait(false);

            return bands?.Bands.FirstOrDefault(
                b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase))?.DisplayName;
        }

        private static async Task<string?> ResolveAsync(
            Guid itemId, Func<VatBandCache, int, Task<string?>> resolve, CancellationToken ct)
        {
            try
            {
                var tax = await TillStoreAccess.TryUseAsync(s => s.TaxInfoAsync(itemId, ct), ct)
                    .ConfigureAwait(false);
                if (tax is not { } info || info.TaxId <= 0) return null;

                var api = await TillPlacement.TryCreateApiAsync().ConfigureAwait(false);

                // ⚠ The cache reads locally; the client is only its refresh path. A till with no
                // usable server address must still resolve bands it has already been told.
                var cache = await TillStoreAccess.TryUseAsync(
                    s => Task.FromResult(api is null ? null : new VatBandCache(api, new MetaVatBandStore(s))), ct)
                    .ConfigureAwait(false);
                if (cache is null) return null;

                return await resolve(cache, info.TaxId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("VatBands.ResolveAsync", ex);
                return null;
            }
        }
    }
}
