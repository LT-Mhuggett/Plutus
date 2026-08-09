using System;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Frontend.AppClient.Services.Connectivity;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>
    /// Where this till is: which till it is, which store it sits in, and the legacy business that
    /// seeds item ids. **One place, so the app cannot hold two answers.**
    ///
    /// ⚠ META IS THE SOURCE OF TRUTH. `EnrolmentFlow` writes `TillId`, `StoreId`, `BusinessId`,
    /// `TenantId` and `ServerUrl` into the v2 store, because placement is a *fact about the
    /// platform*, not a credential. `SecureDeviceCredentialStore` keeps what it should: the device
    /// id and its secret.
    ///
    /// ⚠ WHY THE FALLBACKS EXIST, in the order they are tried:
    ///   1. **Meta** — correct for anything enrolled through `EnrolmentFlow`.
    ///   2. **Preferences** — tills enrolled between 2026-08-08 18:27 and this change stored the
    ///      till id there and never touched Meta. Read once and back-filled, so the answer
    ///      converges rather than staying split.
    ///   3. **`GET /api/v1/tills/devices/{id}/status`** — tills enrolled BEFORE 18:27 on 2026-08-08
    ///      have neither. This is the recovery added in `88ded02`: a device that has a valid
    ///      identity must never be told it is not enrolled.
    /// Each fallback writes what it learns back to Meta, so it runs at most once per till.
    ///
    /// ⚠ This replaces THREE copies of that recovery block (sign-in, roster sync, store lookup).
    /// They were written separately within a day of each other, which is exactly how two of them
    /// end up disagreeing later.
    /// </summary>
    public static class TillPlacement
    {
        /// <summary>
        /// Build an API client for this till, or null when there is no usable identity/address.
        ///
        /// ⚠ Never mutate <c>BaseAddress</c> — <see cref="PlutusHttp"/> caches one client per
        /// address precisely because assigning it after the first request throws, and that reached
        /// an operator once as "This instance has already started one or more requests".
        /// </summary>
        public static async Task<PlutusApiClient?> TryCreateApiAsync(CancellationToken ct = default)
        {
            // ⚠ THE SHARED client — one token mint for the whole app, not one per service. See
            // `PlutusApi`: `/api/v1/tokens/device` allows 5 a minute PER IP.
            return await Connectivity.PlutusApi.GetAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Which till this is. Null only when the device genuinely has no identity, or when every
        /// fallback failed — ⚠ which is "ask again later", never "not enrolled".
        /// </summary>
        /// <param name="api">Optional; supplied when the caller already has one. Without it the
        /// server fallback is skipped rather than a second client being built.</param>
        public static async Task<Guid?> TillIdAsync(PlutusApiClient? api = null, CancellationToken ct = default)
        {
            var fromMeta = await TillStoreAccess.TryUseAsync(s => s.GetGuidMetaAsync(MetaKeys.TillId, ct), ct)
                .ConfigureAwait(false);
            if (fromMeta is Guid known) return known;

            var credentials = await SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);
            if (credentials?.DeviceId is not Guid deviceId) return null;

            if (credentials.TillId is Guid fromPreferences)
            {
                await RememberTillIdAsync(fromPreferences, ct).ConfigureAwait(false);
                return fromPreferences;
            }

            api ??= await TryCreateApiAsync().ConfigureAwait(false);
            if (api is null) return null;

            var (_, status) = await api.GetDeviceStatusAsync(deviceId, ct).ConfigureAwait(false);
            if (status?.TillId is not Guid recovered) return null;

            await RememberTillIdAsync(recovered, ct).ConfigureAwait(false);
            // Mirrored into Preferences too, so a build that still reads it there agrees. ⚠ Meta
            // remains authoritative; this mirror goes away when the last reader does.
            credentials.SaveTillId(recovered);
            return recovered;
        }

        /// <summary>This till's store. Null until a placement refresh has succeeded once.</summary>
        public static Task<int?> StoreIdAsync(CancellationToken ct = default) =>
            TillStoreAccess.TryUseAsync(s => s.GetIntMetaAsync(MetaKeys.StoreId, ct), ct);

        /// <summary>
        /// The LEGACY business id. ⚠ NOT the tenant id — <c>DeterministicGuid.ForItem</c> seeds
        /// from this, so using the tenant id instead makes every item id on this till diverge from
        /// the web till's for the same barcode, silently and permanently.
        /// </summary>
        public static Task<Guid?> BusinessIdAsync(CancellationToken ct = default) =>
            TillStoreAccess.TryUseAsync(s => s.GetGuidMetaAsync(MetaKeys.BusinessId, ct), ct);

        /// <summary>
        /// Re-learn store and business from the platform. Safe to call on every start and
        /// idempotent — ⚠ and it must be called on every start, because a till can be MOVED
        /// between stores in the portal and its prices, receipts and themes have to follow it.
        /// Never throws: a placement refresh that fails leaves the last known values in place.
        /// </summary>
        public static async Task RefreshAsync(PlutusApiClient? api = null, CancellationToken ct = default)
        {
            try
            {
                api ??= await TryCreateApiAsync().ConfigureAwait(false);
                if (api is null) return;

                // Make sure Meta has the till id first, or RefreshPlacementAsync has nothing to
                // work from on a till that predates Meta.
                if (await TillIdAsync(api, ct).ConfigureAwait(false) is not Guid) return;

                var credentials = await SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);
                if (credentials is null) return;

                await TillStoreAccess.UseAsync(
                    store => new EnrolmentFlow(store, api, credentials).RefreshPlacementAsync(ct), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("TillPlacement.RefreshAsync", ex);
            }
        }

        /// <summary>Refresh without waiting — for app start, where nothing should block the UI.</summary>
        public static void RefreshInBackground()
        {
            _ = Task.Run(async () =>
            {
                try { await RefreshAsync().ConfigureAwait(false); }
                catch (Exception ex) { Analytics.CrashLog.Write("TillPlacement.RefreshInBackground", ex); }
            });
        }

        private static Task RememberTillIdAsync(Guid tillId, CancellationToken ct) =>
            TillStoreAccess.TryUseAsync(async s =>
            {
                await s.SetMetaAsync(MetaKeys.TillId, tillId.ToString("D"), ct).ConfigureAwait(false);
                return true;
            }, ct);
    }
}
