using System;

namespace Plutus.Contracts.Client;

// ─────────────────────────────────────────────────────────────────────────────
// WP5 — the two things a till needs to stay current: a heartbeat so the fleet knows it is alive,
// and a cursor-based catalogue feed so it can trade offline against fresh data.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// POST /api/v1/heartbeat — sent every 60s with the device token.
///
/// ⚠ Heartbeat failures are SILENT by design. A till whose heartbeat 500s must keep selling and
/// keep queueing; presence is a convenience for the portal, never a precondition for trading.
/// </summary>
/// <param name="OutboxDepth">Pending sales. The number a manager actually wants: takings sitting on
/// a counter rather than in the ledger.</param>
/// <param name="OldestUnsyncedAgeSeconds">Age of the oldest pending sale. Depth alone hides the
/// difference between 20 sales from the last hour and 3 stuck since Tuesday.</param>
/// <param name="DeviceClockUtc">The till's own clock, so drift is visible centrally rather than
/// only when a day's takings quarantine against the wrong instant.</param>
public sealed record HeartbeatRequest(
    Guid DeviceId,
    string? AppVersion,
    int OutboxDepth,
    long? OldestUnsyncedAgeSeconds,
    DateTime DeviceClockUtc);

/// <summary>
/// The reply carries PULL SIGNALS rather than the server pushing anything — a till on a shop LAN
/// behind NAT cannot be reached, so everything the platform wants it to do has to be something it
/// asks about on a cadence it controls.
/// </summary>
/// <param name="CatalogueCursor">The newest catalogue change the server holds. A till whose own
/// cursor differs pulls <c>/catalogue/changes</c>. Null = this tenant has no catalogue yet.</param>
/// <param name="SyncNow">An operator pressed "sync now" in the portal. One-shot: the server clears
/// it once delivered, so it cannot loop.</param>
/// <param name="Locked">Lock the UI to a "contact your administrator" screen. ⚠ Local sales data is
/// untouched and the outbox keeps draining — this is a till that must stop SELLING, not a till that
/// must lose what it has already taken.</param>
/// <param name="ServerUtcNow">For clock-drift detection, same as the ping.</param>
public sealed record HeartbeatResult(
    string? CatalogueCursor,
    bool SyncNow,
    bool Locked,
    string? LockReason,
    DateTime ServerUtcNow);

/// <summary>
/// One catalogue row as a till holds it.
///
/// ⚠ Carries the PRICE PAIR, not a VAT rate. A till derives <c>vatRateBp</c> from the pair at sale
/// time exactly as the web till does — sending a pre-computed rate here would make the two tills
/// bucket the same item's VAT differently, which is the behavioural drift Part C exists to stop.
/// <see cref="TaxId"/> is the legacy tax row, which resolves to a published VAT band.
/// </summary>
/// <param name="Id">Derived, not assigned: <c>DeterministicGuid.ForItem(businessId, idOne)</c>.</param>
/// <param name="IdOne">⚠ The barcode, and the REAL invariant — every v1 stock, price and sale-line
/// call keys on this string, and it cannot be recovered from <see cref="Id"/>.</param>
/// <param name="Removed">A TOMBSTONE — the item was binned. ⚠ Without these an offline till keeps
/// selling something the shop has withdrawn, indefinitely, because "not in the feed" and "deleted"
/// look identical to a client that only ever sees upserts.</param>
public sealed record CatalogueItemDto(
    Guid Id,
    string IdOne,
    string Name,
    long PricePence,
    long ExPricePence,
    int TaxId,
    Guid? CategoryId,
    bool StockUntracked,
    bool Removed,
    DateTime UpdatedAtUtc);

/// <summary>
/// GET /api/v1/catalogue/changes?since={cursor}&amp;limit={n}
///
/// ⚠ <see cref="Cursor"/> IS OPAQUE. Store it and hand it back; do not parse it. It is keyset
/// pagination over (ModifiedAt, IdOne) rather than a page number, because a catalogue being edited
/// while a till pages through it would otherwise skip or repeat rows — and a skipped row is an item
/// whose new price never arrives.
/// </summary>
public sealed record CatalogueChangesResult(
    string? Cursor,
    bool HasMore,
    CatalogueItemDto[] Items);
