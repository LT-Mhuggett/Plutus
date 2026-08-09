using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.Connectivity;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>
    /// This store's details, from `GET /api/v1/stores/{id}/info`, cached in Meta (cutover step 20).
    ///
    /// ⚠ THE PORTAL IS THE SOURCE OF TRUTH AND THE TILL NEVER WRITES IT (WP6.1, binding default 9).
    /// The screen this replaces had five commands that edited the store's name, address, logo,
    /// phone and VAT number into the LEGACY local database — so the shop's own VAT number could
    /// differ on every till in the estate, and the one on a receipt was whichever machine printed
    /// it. That is the sort of disagreement nobody finds until an inspection.
    ///
    /// ⚠ LAST-GOOD, NOT STALE-BY-DEFAULT. A till that has fetched this once keeps showing that
    /// answer offline, because a receipt header with no address is worse than one a day old. But a
    /// till that has NEVER fetched it shows "unavailable" rather than anything from the legacy
    /// tables — there is no such thing as a locally-authored store record any more, and showing one
    /// would re-legitimise the thing being deleted.
    /// </summary>
    internal static class StoreInfoCache
    {
        private const string MetaKey = "store.info";

        /// <summary>The cached details, or null when this till has never successfully fetched
        /// them. ⚠ Never throws — a details screen must not be able to stop a till.</summary>
        internal static async Task<StoreInfoResult> CachedAsync(CancellationToken ct = default)
        {
            try
            {
                var json = await TillStoreAccess.UseAsync(s => s.GetMetaAsync(MetaKey, ct), ct).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<StoreInfoResult>(json, PlutusApiClient.Json);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("StoreInfoCache.CachedAsync", ex);
                return null;
            }
        }

        /// <summary>
        /// Fetch and cache. Returns the fresh copy, or the last-good one when the server cannot be
        /// reached — so a caller gets the best answer available without having to ask twice.
        /// </summary>
        internal static async Task<StoreInfoResult> RefreshAsync(CancellationToken ct = default)
        {
            try
            {
                if (await TillPlacement.StoreIdAsync(ct).ConfigureAwait(false) is not int storeId)
                    return await CachedAsync(ct).ConfigureAwait(false);

                var credentials = await SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);
                if (credentials?.DeviceId is not Guid) return await CachedAsync(ct).ConfigureAwait(false);

                var http = PlutusHttp.TryFor(new ViewModels.Settings().ServerUrlSetting);
                if (http is null) return await CachedAsync(ct).ConfigureAwait(false);

                var bootstrap = new PlutusApiClient(http);
                var api = new PlutusApiClient(http, new DeviceTokenProvider(bootstrap, credentials));

                var info = await api.GetStoreInfoAsync(storeId, ct).ConfigureAwait(false);

                // ⚠ A null answer does NOT clear the cache. Offline, a 401 mid-token-refresh, or a
                // server blip would otherwise wipe a good store header and leave the next receipt
                // with no shop on it.
                if (info is null) return await CachedAsync(ct).ConfigureAwait(false);

                await TillStoreAccess.UseAsync(
                    s => s.SetMetaAsync(MetaKey, JsonSerializer.Serialize(info, PlutusApiClient.Json), ct), ct)
                    .ConfigureAwait(false);

                return info;
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("StoreInfoCache.RefreshAsync", ex);
                return await CachedAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The store's address as a receipt or a details screen wants it — the populated lines
        /// only, in order.
        /// </summary>
        internal static string AddressOf(StoreInfoResult info)
        {
            if (info is null) return null;

            var lines = new[] { info.AdLine1, info.AdLine2, info.City, info.PostCode, info.Country };
            var kept = new System.Collections.Generic.List<string>();
            foreach (var line in lines)
                if (!string.IsNullOrWhiteSpace(line)) kept.Add(line.Trim());

            return kept.Count == 0 ? null : string.Join("\n", kept);
        }
    }
}
