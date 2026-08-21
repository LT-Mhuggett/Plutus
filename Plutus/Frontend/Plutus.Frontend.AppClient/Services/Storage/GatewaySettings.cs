using System;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.Connectivity;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>
    /// The tenant's card settings, from `GET /api/v1/payments/gateway/active`, cached in the till
    /// store's Meta for offline.
    ///
    /// ⚠ WAS `GatewaySurcharge`, RENAMED 2026-08-21 (WP14). It always fetched the whole
    /// <see cref="ActiveGatewayDto"/> and kept two of its five fields — so the till paid for the
    /// round trip and then threw away `Provider`, `Label` and `Integrated`, which are exactly what
    /// the checkout needed to say which machine the cashier should reach for. The name described
    /// what it kept rather than what it asked for, which is how the other three stayed invisible.
    ///
    /// ⚠ THE PORTAL IS THE SOURCE OF TRUTH and the cache is last-known-good: a till that cannot
    /// reach the server charges what it last knew, not nothing — a fee silently vanishing offline
    /// and reappearing online would make two identical baskets total differently an hour apart,
    /// and the operator would wear the argument. Never blocks: a fetch failure falls back, a till
    /// with no cache charges nothing.
    ///
    /// ⚠⚠ THE CACHE HOLDS THE SERVER'S ANSWER, NOT THE DECISION MADE FROM IT. `Provider`, `Label`
    /// and `Integrated` go into Meta and <see cref="PaymentGateway.Resolve"/> runs on the way OUT,
    /// every time. Storing the resolved <see cref="CardPaymentDisplay"/> instead would freeze one
    /// day's reading of the rule into the till's database, and the day the rule changes — say
    /// "a chosen provider with no integration is still standalone" — every till already in a shop
    /// would keep answering the old way with nothing to show why.
    /// </summary>
    internal static class GatewaySettings
    {
        private const string BpKey = "gateway.surcharge.bp";
        private const string FlatKey = "gateway.surcharge.flat";

        // ⚠ New keys, and deliberately absent from an existing till's Meta until its first online
        // checkout. A till upgrading to this build has the two surcharge keys and not these three,
        // which reads as `Provider = null` — and `PaymentGateway.Resolve(null)` is STANDALONE, the
        // flow that works when nothing else does. The upgrade degrades to the right answer.
        private const string ProviderKey = "gateway.card.provider";
        private const string LabelKey = "gateway.card.label";
        private const string IntegratedKey = "gateway.card.integrated";

        /// <summary>What the checkout needs to know about cards, in one round trip.</summary>
        /// <param name="Bp">Surcharge in basis points.</param>
        /// <param name="FlatPence">Flat surcharge component.</param>
        /// <param name="Card">⚠ Never null — a failed lookup resolves to standalone.</param>
        internal sealed record Settings(int Bp, long FlatPence, CardPaymentDisplay Card);

        internal static async Task<Settings> GetAsync(CancellationToken ct = default)
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
                    await s.SetMetaAsync(ProviderKey, gateway.Provider ?? string.Empty, ct).ConfigureAwait(false);
                    await s.SetMetaAsync(LabelKey, gateway.Label ?? string.Empty, ct).ConfigureAwait(false);
                    // ⚠ Lowercase "true"/"false" so `bool.TryParse` reads it back on any culture.
                    await s.SetMetaAsync(IntegratedKey, gateway.Integrated ? "true" : "false", ct).ConfigureAwait(false);
                    return true;
                }, ct).ConfigureAwait(false);

                return new Settings(
                    gateway.SurchargeBp, gateway.SurchargeFlatPence, PaymentGateway.Resolve(gateway));
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("GatewaySettings.GetAsync", ex);
                return await CachedAsync(ct).ConfigureAwait(false);
            }
        }

        private static async Task<Settings> CachedAsync(CancellationToken ct)
        {
            try
            {
                return await TillStoreAccess.UseAsync(async s =>
                {
                    var bp = int.TryParse(await s.GetMetaAsync(BpKey, ct).ConfigureAwait(false), out var b) ? b : 0;
                    var flat = long.TryParse(await s.GetMetaAsync(FlatKey, ct).ConfigureAwait(false), out var f) ? f : 0;

                    var provider = await s.GetMetaAsync(ProviderKey, ct).ConfigureAwait(false);
                    var label = await s.GetMetaAsync(LabelKey, ct).ConfigureAwait(false);
                    var integrated = bool.TryParse(
                        await s.GetMetaAsync(IntegratedKey, ct).ConfigureAwait(false), out var i) && i;

                    // ⚠ Rebuild the DTO and run the SHARED rule over it — see the class remarks. An
                    // empty provider makes this `Resolve(…)` answer standalone, which is what a till
                    // that has never been online should say.
                    return new Settings(bp, flat, PaymentGateway.Resolve(
                        new ActiveGatewayDto(provider ?? string.Empty, label ?? string.Empty, integrated, bp, flat)));
                }, ct).ConfigureAwait(false);
            }
            catch
            {
                // ⚠ No store, no cache — charge nothing rather than guess, and say standalone, which
                // is the flow whose only dependency is a human.
                return new Settings(0, 0, PaymentGateway.Resolve(null));
            }
        }
    }
}
