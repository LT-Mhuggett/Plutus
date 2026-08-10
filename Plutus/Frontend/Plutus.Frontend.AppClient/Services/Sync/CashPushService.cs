using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.Connectivity;
using Plutus.Frontend.AppClient.Services.Storage;

namespace Plutus.Frontend.AppClient.Services.Sync
{
    /// <summary>
    /// Send the drawer's queued cash events to the platform (WP9, cutover step 23).
    ///
    /// ⚠ THE STATUS IS THE POLICY, and getting it wrong is how a till either loses a float or hides
    /// a problem:
    ///   • **201 / 200** — recorded, or an idempotent replay of an id already stored. Done either
    ///     way; a 200 is a SUCCESS, not a duplicate to worry about. That is the whole reason the
    ///     till mints the event id.
    ///   • **409** — the business day is already Z-closed. **Terminal.** Retrying cannot help, and
    ///     the server's words are kept because somebody has to explain, tomorrow, why a paid-out
    ///     never banked.
    ///   • **400** — the platform refused the shape. Terminal for the same reason.
    ///   • **anything else** — transport. Stays queued; the next tick tries again.
    ///
    /// ⚠ NOTHING HERE MAY BLOCK SELLING, and nothing may surface a dialog. This runs on the 60s
    /// clock beside the outbox drain.
    /// </summary>
    internal static class CashPushService
    {
        /// <summary>One drain. Returns a sentence for the Plutus tab, never throws.</summary>
        internal static async Task<string> PushAsync(CancellationToken ct = default)
        {
            try
            {
                var pending = await TillStoreAccess.UseAsync(s => s.PendingCashEventsAsync(50, ct), ct)
                    .ConfigureAwait(false);
                if (pending.Count == 0) return "";

                var api = await PlutusApi.GetAsync(ct).ConfigureAwait(false);
                if (api is null) return $" {pending.Count} cash event(s) waiting.";

                var credentials = await SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);
                if (credentials?.DeviceId is not Guid deviceId) return $" {pending.Count} cash event(s) waiting.";

                int sent = 0, refused = 0, held = 0;

                foreach (var row in pending)
                {
                    ct.ThrowIfCancellationRequested();

                    var request = new CashEventRequest
                    {
                        EventId = row.EventId,
                        DeviceId = deviceId,
                        Type = row.Type,
                        // ⚠ Parsed back from the stored string. The local column is text because
                        // SQLite has no date type and a round trip through a locale-shaped format is
                        // how a business day silently moves by one.
                        BusinessDay = DateOnly.TryParse(row.BusinessDay, out var day) ? day : default,
                        OccurredAtUtc = row.OccurredAtUtc,
                        AmountPence = row.AmountPence,
                        CountedPence = row.CountedPence,
                        Reason = row.Reason,
                        OperatorUserId = row.OperatorUserId,
                    };

                    var (status, _) = await api.PostCashEventAsync(request, ct).ConfigureAwait(false);

                    if (status == HttpStatusCode.Created || status == HttpStatusCode.OK)
                    {
                        await TillStoreAccess.UseAsync(
                            s => s.SettleCashEventAsync(row.EventId, OutboxStatus.Pushed, ct: ct), ct)
                            .ConfigureAwait(false);
                        sent++;
                    }
                    else if (status == HttpStatusCode.Conflict || status == HttpStatusCode.BadRequest)
                    {
                        // ⚠ TERMINAL, and recorded with the reason. A till that retries a 409 for
                        // ever looks healthy while quietly never banking.
                        await TillStoreAccess.UseAsync(
                            s => s.SettleCashEventAsync(row.EventId, OutboxStatus.Failed,
                                $"{{\"status\":{(int)status}}}", ct), ct)
                            .ConfigureAwait(false);
                        refused++;
                    }
                    else
                    {
                        // Transport. Leave it pending and stop pushing this tick — a server that is
                        // refusing one call is unlikely to accept the next, and hammering it is how
                        // a rate limit turns into a revocation.
                        held = pending.Count - sent - refused;
                        break;
                    }
                }

                var parts = "";
                if (sent > 0) parts += $" {sent} cash event(s) sent.";
                if (refused > 0) parts += $" ⚠ {refused} cash event(s) REFUSED by the platform — see the Plutus tab.";
                if (held > 0) parts += $" {held} still waiting.";
                return parts;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("CashPushService.PushAsync", ex);
                return "";
            }
        }
    }
}
