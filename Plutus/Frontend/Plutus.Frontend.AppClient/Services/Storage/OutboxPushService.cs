using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Frontend.AppClient.Services.Connectivity;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>What one drain did, in terms a screen can put in front of a person.</summary>
    /// <param name="Ran">False = it could not start (not connected, no server address, already
    /// running). ⚠ NOT the same as "there was nothing to send".</param>
    /// <param name="Quarantined">⚠ Sales the server REFUSED and will not accept on a retry. This is
    /// the number somebody has to act on; everything else eventually sends itself.</param>
    public sealed record OutboxPushOutcome(
        bool Ran, int Pushed, int Failed, int Quarantined, int StillPending, string Message);

    /// <summary>
    /// The thing that actually CALLS the outbox pusher (cutover step 13).
    ///
    /// ⚠ THE SAME OMISSION AS THE CATALOGUE FEED, on the other half of the money path.
    /// `OutboxPusher` has existed and been tested for some time, and `DrainAsync` was referenced
    /// NOWHERE in the app: every sale the till committed went into the outbox and stayed there, for
    /// ever. The till looked healthy, the sale looked recorded, and no report, no VAT return and no
    /// other till ever saw a penny of it. Tested components are not a working feature until
    /// something calls them.
    ///
    /// ⚠ IT NEVER BLOCKS SELLING. Draining is how a till stays CORRECT with the platform; it has no
    /// opinion about whether the next customer can be served. Failures are swallowed and reported.
    /// </summary>
    public static class OutboxPushService
    {
        // ⚠ Re-entrant callers are DROPPED, not queued. The cadence tick and a manual "sync now"
        // landing together would otherwise run two drains over one queue, and the second would
        // re-post sales the first has in flight — the server dedupes on DeviceSeq, so it is not a
        // correctness hole, but it burns the rate limit and doubles the log.
        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>Drain what is queued. Never throws.</summary>
        public static async Task<OutboxPushOutcome> PushAsync(CancellationToken ct = default)
        {
            if (!await Gate.WaitAsync(0, ct).ConfigureAwait(false))
                return new OutboxPushOutcome(false, 0, 0, 0, 0, "A sync is already running.");

            try
            {
                var credentials = await SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);
                if (credentials?.DeviceId is not Guid)
                    return new OutboxPushOutcome(false, 0, 0, 0, 0, "This till isn't connected to Plutus yet.");

                // ⚠ THE SHARED client and ITS OWN provider — the pusher invalidates the provider to
                // re-mint once on a 401, and invalidating a DIFFERENT instance clears a cache
                // nobody is reading, leaving the drain retrying with the same dead token.
                var (api, tokens) = await PlutusApi.GetWithTokensAsync(ct).ConfigureAwait(false);
                if (api is null)
                    return new OutboxPushOutcome(false, 0, 0, 0, 0, "The server address doesn't look right.");

                var outcomes = await TillStoreAccess.UseAsync(
                    store => new OutboxPusher(store, api, tokens).DrainAsync(ct: ct), ct).ConfigureAwait(false);

                var depth = await TillStoreAccess.UseAsync(s => s.OutboxDepthAsync(ct), ct).ConfigureAwait(false);

                return Describe(outcomes, depth);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("OutboxPushService.PushAsync", ex);
                return new OutboxPushOutcome(false, 0, 0, 0, 0,
                    "Couldn't send this till's sales. See the Plutus tab's log.");
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// ⚠ QUARANTINE IS CALLED OUT SEPARATELY, and deliberately not softened. A `202` means the
        /// server took the sale and REFUSED to post it — `OutboxPusher` marks it terminal and never
        /// retries — so it is the one outcome that will not fix itself and the only one worth
        /// interrupting somebody about. Reporting "3 sales sent" while two of them were rejected is
        /// how a quarantine sits unnoticed until a VAT return is short.
        /// </summary>
        private static OutboxPushOutcome Describe(IReadOnlyList<PushOutcome> outcomes, int stillPending)
        {
            var pushed = outcomes.Count(o => o.Status == OutboxStatus.Pushed);
            var failed = outcomes.Count(o => o.Status == OutboxStatus.Failed);
            var quarantined = outcomes.Count(o => o.Status == OutboxStatus.Quarantined);

            var message = quarantined > 0
                ? $"{quarantined} sale(s) were REJECTED by Plutus and need looking at. " +
                  $"{pushed} sent, {stillPending} still queued."
                : outcomes.Count == 0
                    ? stillPending == 0 ? "Everything on this till has been sent." : $"{stillPending} sale(s) queued."
                    : $"{pushed} sale(s) sent; {stillPending} still queued.";

            return new OutboxPushOutcome(true, pushed, failed, quarantined, stillPending, message);
        }
    }
}
