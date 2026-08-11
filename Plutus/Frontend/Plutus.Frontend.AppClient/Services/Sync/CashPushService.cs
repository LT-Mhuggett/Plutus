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

                int sent = 0, refused = 0, held = 0, outOfBalance = 0, zHeldForSales = 0;

                foreach (var row in pending)
                {
                    ct.ThrowIfCancellationRequested();

                    // ⚠ A Z CLOSE WAITS FOR ITS OWN DAY'S SALES. The platform's expected drawer is
                    // float + **cash takings** + ins − outs, and the takings half is whatever sales
                    // it has actually received. Sending the Z first therefore reports a shortage
                    // equal to every sale still queued: a till that traded £400 through an outage
                    // would tell the person who counted it correctly that they were £400 down.
                    //
                    // ⚠ AND THE Z IS TERMINAL SERVER-SIDE. It refuses everything against the day
                    // afterwards, so a Z that overtakes its own sales does not merely mis-report —
                    // it puts the day's real sales into quarantine behind a close that should have
                    // followed them.
                    //
                    // ⚠ `break`, NOT `continue`. Cash events are drained oldest-first and the Z is
                    // the last of its day; skipping past it to push a later day's float would send
                    // this till's queue out of order. Waiting a tick costs a minute — the sales
                    // drain runs first, immediately before this, so the usual case is that it is
                    // already empty and nothing waits at all.
                    if (row.Type == CashEventTypes.ZClose)
                    {
                        var unsent = await TillStoreAccess
                            .UseAsync(s => s.PendingSalesForDayAsync(row.BusinessDay, ct), ct)
                            .ConfigureAwait(false);

                        if (unsent > 0)
                        {
                            held = pending.Count - sent - refused;
                            zHeldForSales = unsent;
                            break;
                        }
                    }

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

                    // ⚠ THE BODY IS KEPT NOW, and discarding it was finding I. The platform answers a
                    // Z close with what the drawer SHOULD have held and the difference against what
                    // was counted — the only place either figure exists, because only the platform
                    // sees the sales half. This drain took the status code and threw the rest away,
                    // so a till could close £20 short, be accepted with a 201, and say nothing.
                    var (status, body) = await api.PostCashEventAsync(request, ct).ConfigureAwait(false);

                    if (status == HttpStatusCode.Created || status == HttpStatusCode.OK)
                    {
                        await TillStoreAccess.UseAsync(
                            s => s.SettleCashEventAsync(row.EventId, OutboxStatus.Pushed,
                                expectedPence: body?.ExpectedPence, variancePence: body?.VariancePence, ct: ct), ct)
                            .ConfigureAwait(false);
                        sent++;

                        // ⚠ COUNTED, NOT ANNOUNCED. This runs on the 60s clock beside the outbox
                        // drain and must never raise a dialog — a variance popping up mid-sale would
                        // interrupt the next customer over yesterday's drawer. The figure is
                        // recorded on the row and the Cash screen shows it; since `TillCadence.Ticked`
                        // that screen redraws itself, so an operator who stays on the tab after a Z
                        // watches the verdict arrive.
                        if (body?.VariancePence is not null and not 0) outOfBalance++;
                    }
                    else if (status == HttpStatusCode.Conflict || status == HttpStatusCode.BadRequest)
                    {
                        // ⚠ TERMINAL, and recorded with the reason. A till that retries a 409 for
                        // ever looks healthy while quietly never banking.
                        await TillStoreAccess.UseAsync(
                            s => s.SettleCashEventAsync(row.EventId, OutboxStatus.Failed,
                                $"{{\"status\":{(int)status}}}", ct: ct), ct)
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
                if (outOfBalance > 0)
                    parts += $" ⚠ {outOfBalance} counted drawer(s) DID NOT BALANCE — see the Cash tab.";
                if (zHeldForSales > 0)
                    parts += $" Z close waiting for {zHeldForSales} sale(s) to send first.";
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
