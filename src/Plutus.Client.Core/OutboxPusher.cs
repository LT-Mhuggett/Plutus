using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.Client.Core;

/// <summary>
/// WP3: drains the till's outbox to <c>POST /api/v1/sales</c>.
///
/// The rules this encodes, and why each one exists:
///   • <b>DeviceSeq order.</b> The server uses the sequence for gap detection; draining out of
///     order makes a complete queue look like it lost sales.
///   • <b>A poison sale must never block the queue.</b> A 400 marks that one Failed and moves on —
///     otherwise one malformed sale from a bug in March stops every sale in April from syncing.
///   • <b>202 is terminal, not a failure.</b> Quarantine means a human will look at it; retrying
///     just re-quarantines.
///   • <b>Network failures retry forever</b> with <see cref="Backoff"/>. A sale is money; it is
///     never dropped for being old.
///   • <b>401 re-mints once.</b> A token expiring mid-drain is ordinary, not an error.
///
/// Deliberately not a background service: MAUI owns the scheduling (app start, connectivity
/// change, heartbeat signal), and keeping the loop callable makes the soak test synchronous.
/// </summary>
public sealed class OutboxPusher
{
    private readonly IOutboxStore _store;
    private readonly PlutusApiClient _api;
    private readonly IDeviceTokenProvider? _tokens;
    private readonly Func<DateTime> _utcNow;

    public OutboxPusher(IOutboxStore store, PlutusApiClient api, IDeviceTokenProvider? tokens = null, Func<DateTime>? utcNow = null)
    {
        _store = store;
        _api = api;
        _tokens = tokens;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Drain up to <paramref name="max"/> pending sales. Returns one outcome per attempt.
    /// Stops early only on a transport failure — there is no point walking the rest of the queue
    /// when the network is down, and doing so would burn the backoff on every row at once.</summary>
    public async Task<IReadOnlyList<PushOutcome>> DrainAsync(int max = 200, CancellationToken ct = default)
    {
        var outcomes = new List<PushOutcome>();
        var pending = await _store.GetPendingAsync(max, ct);

        foreach (var entry in pending)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await PushOneAsync(entry, retryOn401: true, ct);
            outcomes.Add(outcome);
            if (outcome.ShouldStop) break;
        }
        return outcomes;
    }

    private async Task<PushOutcome> PushOneAsync(OutboxEntry entry, bool retryOn401, CancellationToken ct)
    {
        HttpStatusCode status;
        Contracts.Client.IngestResponse? body;
        try
        {
            (status, body) = await _api.PostSaleAsync(entry.PayloadJson, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Transport failure — stay Pending, count the attempt, stop the drain.
            entry.Attempts++;
            await _store.UpdateAsync(entry, ct);
            return new PushOutcome(entry.SaleId, OutboxStatus.Pending, ShouldStop: true, e.Message);
        }

        switch (status)
        {
            case HttpStatusCode.Created:      // 201 recorded
            case HttpStatusCode.OK:           // 200 duplicate — the server already had it
                entry.Status = OutboxStatus.Pushed;
                entry.PushedAtUtc = _utcNow();
                entry.Attempts = 0;
                entry.ServerResponseJson = body?.Status;
                await _store.UpdateAsync(entry, ct);
                return new PushOutcome(entry.SaleId, OutboxStatus.Pushed, ShouldStop: false);

            case HttpStatusCode.Accepted:     // 202 quarantined — terminal, never retry
                entry.Status = OutboxStatus.Quarantined;
                entry.PushedAtUtc = _utcNow();
                entry.ServerResponseJson = body?.Status ?? "quarantined";
                await _store.UpdateAsync(entry, ct);
                return new PushOutcome(entry.SaleId, OutboxStatus.Quarantined, ShouldStop: false);

            case HttpStatusCode.BadRequest:   // 400 poison — skip it, keep draining
                entry.Status = OutboxStatus.Failed;
                entry.ServerResponseJson = body?.Detail ?? "rejected";
                await _store.UpdateAsync(entry, ct);
                return new PushOutcome(entry.SaleId, OutboxStatus.Failed, ShouldStop: false, body?.Detail);

            case HttpStatusCode.Unauthorized when retryOn401 && _tokens != null:
                _tokens.Invalidate();
                return await PushOneAsync(entry, retryOn401: false, ct);

            // ⚠⚠ 426 UPGRADE REQUIRED IS NOT A TRANSPORT HICCUP. It says "this till's build is too
            // old for the platform to accept" — a state that CANNOT resolve itself, however long the
            // backoff runs. It was lumped in with 5xx below, so a till that hit it would retry for
            // ever: healthy-looking, queue quietly growing, nobody told. That is the same shape as
            // the 409/400 bug fixed on the cash drain (2026-08-11), and it is worse here because the
            // sale is the money.
            //
            // ⚠ STAYS PENDING, NOT FAILED — and the distinction is the whole point. A 400 is
            // terminal because the platform will never accept that payload. A 426 is terminal for
            // THIS BUILD: update the till and the very same sale goes through. Marking it Failed
            // would throw away recoverable money.
            //
            // ⚠ SO IT STOPS, AND SAYS WHY. `ShouldStop` halts the drain (nothing else will fare
            // better), and the reason is carried so the Plutus tab can say "this till needs
            // updating" rather than "HTTP 426". Selling continues — WP5's rule, and the right one:
            // an out-of-date till can still take money and still owes its customer a receipt.
            case HttpStatusCode.UpgradeRequired:
                entry.Attempts++;
                await _store.UpdateAsync(entry, ct);
                return new PushOutcome(entry.SaleId, OutboxStatus.Pending, ShouldStop: true,
                    "This till's software is too old for Plutus to accept its sales. "
                    + "Nothing is lost — they will send once it has been updated.");

            default:
                // 401 after a re-mint, 403, 5xx … all stay Pending for the backoff.
                entry.Attempts++;
                await _store.UpdateAsync(entry, ct);
                return new PushOutcome(entry.SaleId, OutboxStatus.Pending, ShouldStop: true, $"HTTP {(int)status}");
        }
    }
}
