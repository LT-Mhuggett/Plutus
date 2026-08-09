using System;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Services.Connectivity;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>
    /// The tenant's card-surcharge setting, from `GET /api/v1/payments/gateway/active`, cached in
    /// the till store's Meta for offline.
    ///
    /// ⚠ THE PORTAL IS THE SOURCE OF TRUTH and the cache is last-known-good: a till that cannot
    /// reach the server charges what it last knew, not nothing — a fee silently vanishing offline
    /// and reappearing online would make two identical baskets total differently an hour apart,
    /// and the operator would wear the argument. Never blocks: a fetch failure falls back, a till
    /// with no cache charges nothing.
    /// </summary>
    internal static class GatewaySurcharge
    {
        private const string BpKey = "gateway.surcharge.bp";
        private const string FlatKey = "gateway.surcharge.flat";

        internal static async Task<(int Bp, long FlatPence)> GetAsync(CancellationToken ct = default)
        {
            try
            {
                // ⚠ THE SHARED client — one token mint for the whole app, not one per service.
                var api = await PlutusApi.GetAsync(ct).ConfigureAwait(false);
                if (api is null) return await CachedAsync(ct).ConfigureAwait(false);

                var gateway = await api.GetActiveGatewayAsync(ct).ConfigureAwait(false);
                if (gateway is null) return await CachedAsync(ct).ConfigureAwait(false);

                await TillStoreAccess.UseAsync(async s =>
                {
                    await s.SetMetaAsync(BpKey, gateway.SurchargeBp.ToString(), ct).ConfigureAwait(false);
                    await s.SetMetaAsync(FlatKey, gateway.SurchargeFlatPence.ToString(), ct).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);

                return (gateway.SurchargeBp, gateway.SurchargeFlatPence);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("GatewaySurcharge.GetAsync", ex);
                return await CachedAsync(ct).ConfigureAwait(false);
            }
        }

        private static async Task<(int, long)> CachedAsync(CancellationToken ct)
        {
            try
            {
                return await TillStoreAccess.UseAsync(async s =>
                {
                    var bp = int.TryParse(await s.GetMetaAsync(BpKey, ct).ConfigureAwait(false), out var b) ? b : 0;
                    var flat = long.TryParse(await s.GetMetaAsync(FlatKey, ct).ConfigureAwait(false), out var f) ? f : 0;
                    return (bp, flat);
                }, ct).ConfigureAwait(false);
            }
            catch
            {
                return (0, 0);   // no store, no cache — charge nothing rather than guess
            }
        }
    }
}
