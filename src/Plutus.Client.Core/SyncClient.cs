using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;

namespace Plutus.Client.Core;

/// <summary>Where a till keeps its sync cursors and applies catalogue changes. Implemented by
/// <c>Plutus.Client.Storage.TillStore</c>; abstracted so the sync rules are testable without
/// SQLite, a device, or a deployment.</summary>
public interface ISyncStore
{
    Task<string?> GetCatalogueCursorAsync(CancellationToken ct = default);

    /// <summary>Apply one page and advance the cursor — ⚠ IN ONE TRANSACTION. If the rows landed
    /// and the cursor did not, the next sync re-applies them (harmless, they upsert). If the cursor
    /// advanced and the rows did not, those changes are lost for ever and nothing reports it.</summary>
    Task ApplyCatalogueAsync(IReadOnlyList<CatalogueItemDto> items, string? cursor, CancellationToken ct = default);

    /// <summary>Pending sales, for the heartbeat.</summary>
    Task<int> OutboxDepthAsync(CancellationToken ct = default);

    /// <summary>Age of the oldest pending sale, or null if the queue is empty.</summary>
    Task<TimeSpan?> OldestPendingAgeAsync(CancellationToken ct = default);
}

/// <summary>What one sync did — enough for a Settings screen to show something truthful.</summary>
public sealed record SyncOutcome(int Pages, int ItemsApplied, string? Cursor, bool UpToDate, string? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>What the last heartbeat said the till should do.</summary>
/// <param name="UpdateAvailable">The build the platform says this till should be on, when this till
/// is BEHIND it — otherwise null. 26a0 Matt, 2026-08-11: *"Does the heartbeat from the till check for
/// updates? All tills should do this."* 26a0 The comparison happens HERE, once, using
/// `PlutusVersion.IsOlderThan` 2014 never a string compare, or a till on 1.10.0 is told it is behind
/// 1.9.0 for ever. 26a0 ADVISORY ONLY: nothing downstream may refuse to sell because of it.</param>
public sealed record HeartbeatOutcome(bool Delivered, bool SyncNow, bool Locked, string? LockReason, bool CatalogueStale, string? UpdateAvailable = null, int UnreadSupportReplies = 0);

/// <summary>
/// WP5 — the till's sync loop: beat, and pull the catalogue when it has moved.
///
/// ⚠ NOTHING HERE MAY EVER BLOCK SELLING. A heartbeat that fails, a catalogue that will not
/// download, a server that has gone away — all of it is invisible to the operator ringing up a
/// customer. Sync is how a till stays *current*; the outbox is how it stays *correct*, and only the
/// second one is allowed to have opinions about whether a sale can proceed.
///
/// ⚠ In Client.Core, not in MAUI: the web till, the MAUI till and any future macOS/Linux till must
/// sync the same way or they will disagree about what is in the catalogue — which shows up as two
/// tills quoting different prices for the same barcode on the same day.
/// </summary>
public sealed class SyncClient
{
    /// <summary>Pages per sync run. A bound, not a target: it stops a first-ever sync of a 20k
    /// catalogue from running unbounded inside one call, while still finishing in a few runs.</summary>
    public const int MaxPagesPerRun = 200;

    private readonly PlutusApiClient _api;
    private readonly ISyncStore _store;

    public SyncClient(PlutusApiClient api, ISyncStore store)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Send a beat and read back what the platform wants. Never throws.
    /// </summary>
    /// <param name="appVersion">This till's own component version, so the fleet list can spot a
    /// till nobody has updated.</param>
    public async Task<HeartbeatOutcome> BeatAsync(Guid deviceId, string? appVersion, CancellationToken ct = default)
    {
        try
        {
            var depth = await _store.OutboxDepthAsync(ct);
            var oldest = await _store.OldestPendingAgeAsync(ct);

            var result = await _api.HeartbeatAsync(new HeartbeatRequest(
                deviceId, appVersion, depth,
                oldest is TimeSpan t ? (long)t.TotalSeconds : null,
                DateTime.UtcNow), ct);

            if (result is null) return new HeartbeatOutcome(false, false, false, null, false);

            var mine = await _store.GetCatalogueCursorAsync(ct);
            return new HeartbeatOutcome(
                Delivered: true,
                SyncNow: result.SyncNow,
                Locked: result.Locked,
                LockReason: result.LockReason,
                // Any difference means "pull". Comparing rather than trusting a flag keeps the
                // common case — nothing changed — free.
                CatalogueStale: result.CatalogueCursor is not null && result.CatalogueCursor != mine,

                // ⚠ THE COMPARISON HAPPENS ONCE, HERE, and in `Client.Core` so every till answers
                // "am I behind?" the same way — a browser and a .exe disagreeing about which of two
                // releases is newer is a support call nobody can close.
                // ⚠ `appVersion` is what this till just REPORTED, so the answer is about the build
                // actually running rather than anything the caller believes.
                // ⚠ Null unless genuinely behind: equal, ahead, unset, and unparseable all mean
                // "say nothing" — see `PlutusVersion.IsOlderThan`, which also refuses to treat the
                // `0.0.0` sentinel as ancient.
                UpdateAvailable: PlutusVersion.IsOlderThan(appVersion, result.ExpectedMauiVersion)
                    ? result.ExpectedMauiVersion
                    : null,

                // ⚠⚠ THE SUPPORT BADGE (WP-TICKETS, 2026-08-21). Matt: *"When I reply to a live
                // ticket, how is the user informed? Does the heartbeat need to check for an
                // update?"* Yes — and it rides the beat because the beat is the only thing on a
                // till that runs whether or not anybody is looking at the screen.
                //
                // ⚠ CARRIED, NOT ACTED ON, exactly like `UpdateAvailable` above it: nothing here
                // interrupts a sale, and a badge is the least important passenger on this request.
                UnreadSupportReplies: result.UnreadSupportReplies);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // ⚠ SILENT. A till whose heartbeat 500s must keep selling and keep queueing; presence is
            // a convenience for the portal, never a precondition for trading.
            return new HeartbeatOutcome(false, false, false, null, false);
        }
    }

    /// <summary>
    /// Pull every catalogue page from the stored cursor forward.
    ///
    /// ⚠ Call this only when NO BASKET IS OPEN. Prices changing under a half-built sale is how a
    /// customer is quoted one total and charged another, and the receipt is the thing they keep.
    /// The caller owns that decision because only the caller knows about baskets.
    /// </summary>
    public async Task<SyncOutcome> SyncCatalogueAsync(CancellationToken ct = default)
    {
        var cursor = await _store.GetCatalogueCursorAsync(ct);
        var pages = 0;
        var applied = 0;

        try
        {
            while (pages < MaxPagesPerRun)
            {
                var page = await _api.GetCatalogueChangesAsync(cursor, ct: ct);
                if (page is null)
                    return new SyncOutcome(pages, applied, cursor, false, "The catalogue feed did not answer.");

                pages++;

                if (page.Items.Length > 0)
                {
                    // Cursor and rows advance together or not at all — see ISyncStore.
                    await _store.ApplyCatalogueAsync(page.Items, page.Cursor, ct);
                    applied += page.Items.Length;
                }

                cursor = page.Cursor;
                if (!page.HasMore) return new SyncOutcome(pages, applied, cursor, true, null);
            }

            // Hit the page bound. Not an error — the cursor is saved, so the next run resumes
            // exactly where this one stopped.
            return new SyncOutcome(pages, applied, cursor, false, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // Partial progress is kept: every page already applied advanced the cursor with it.
            return new SyncOutcome(pages, applied, cursor, false, $"{e.GetType().Name}: {e.Message}");
        }
    }
}
