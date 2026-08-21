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
/// <param name="ExpectedMauiVersion">The MAUI till build the platform expects, or null for "say
/// nothing". ⚠ Matt, 2026-08-11: *"Does the heartbeat from the till check for updates? All tills
/// should do this."* It did not — this is that answer, and it rides the beat because the beat is
/// already the platform's only way to tell a till anything (tills sit behind NAT; nothing can reach
/// in). ⚠ **ADVISORY, NEVER A GATE**: a till below this still sells, still takes money, still
/// drains. There is no self-update for MAUI (Matt's decision, same day) — it is an unpackaged .exe
/// that cannot fetch its own replacement, so refusing to work would strand a shop with no way out.</param>
/// <param name="ExpectedWebVersion">The same for the web till. ⚠ Expected to stay null: the browser
/// already detects a new deploy exactly, by comparing its running bundle hash against the one the
/// server serves. Carried so both surfaces are configured in one place, not because it is needed.</param>
/// <param name="UnreadSupportReplies">
/// ⚠⚠ HOW A SHOP LEARNS THAT SUPPORT ANSWERED (WP-TICKETS, 2026-08-21). Matt: *"When I reply to a
/// live ticket, how is the user informed? Does the heartbeat need to check for an update?"* It did,
/// and it does: **the beat is the platform's only way to tell a till anything** — tills sit behind
/// NAT and nothing can reach in — and it is the only channel that reaches a till with nobody
/// watching a browser tab.
///
/// ⚠ A COUNT, NOT A LIST. The beat runs every 60 seconds on every till in the estate, so it carries
/// the cheapest thing that can drive a badge; the ❓ opens the desk, which fetches the threads.
///
/// ⚠ ZERO, NEVER NULL, so a till that cannot read the desk shows no badge rather than a broken one.
/// </param>
public sealed record HeartbeatResult(
    string? CatalogueCursor,
    bool SyncNow,
    bool Locked,
    string? LockReason,
    DateTime ServerUtcNow,
    string? ExpectedMauiVersion = null,
    string? ExpectedWebVersion = null,
    int UnreadSupportReplies = 0,

    /// <summary>WP-TZ: the shop's timezone (IANA), or null. ⚠ The till renders its clock in
    /// it and WARNS when this PC disagrees — a PC on the wrong zone files sales under the
    /// wrong trading day, silently. ⚠ It does NOT change `BusinessDay`.</summary>
    string? StoreTimeZoneId = null);

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
/// <summary>One dated price on the wire. ⚠ The PAIR travels together — a line's <c>vatRateBp</c> is
/// derived from inc/ex, so a mismatched pair is a wrong VAT figure on a printed receipt.</summary>
public sealed record PricePointDto(
    long PricePence,
    long ExPricePence,
    DateTime EffectiveFromUtc,
    DateTime CreatedAtUtc);

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
    DateTime UpdatedAtUtc,
    /// <summary>0 Central · 1 CentralWithOverride · 2 Local — who owns this item's price.</summary>
    byte PricePolicy = 0,
    /// <summary>
    /// The company price list for this item, **including FUTURE points**.
    ///
    /// ⚠ Shipping the timeline rather than today's number is what lets a Sunday-night reprice
    /// scheduled on Thursday activate at the boundary on a till that has been offline all week.
    /// Sending only the current price would leave that till charging last week's prices with
    /// nothing to notice it.
    /// </summary>
    PricePointDto[]? CentralPrices = null,
    /// <summary>This TILL's store's non-revoked overrides, effective-dated. ⚠ Already filtered to
    /// the till's own store and to live overrides — another store's price is not this till's
    /// business, and a revoked override is not a price at all.</summary>
    PricePointDto[]? StorePrices = null,

    /// <summary>
    /// The manufacturer or publisher. ⚠ **A SEARCHED FIELD**, and its absence was a real parity
    /// gap: `SharedKernel.ItemSearch` matches on name, barcode AND brand, but the till's catalogue
    /// row had no brand column, so `TillStore.SearchAsync` passed null and the scan box matched on
    /// TWO fields where the server and the web till matched three. Searching "Marvel" found nothing
    /// on a MAUI till and everything on the web one — the same query, the same shop, two answers.
    /// </summary>
    string? Brand = null,

    /// <summary>Free text about the item. Shown when editing; not searched (see `ItemSearch` —
    /// adding a fourth searched field without adding it to the server changes what a till finds
    /// and nothing would say so).</summary>
    string? Desc = null,

    /// <summary>
    /// What the shop PAID, in pence.
    ///
    /// ⚠ PENCE, though the column is `decimal`. Money is integer pence everywhere in this project
    /// (architecture §4.1) and the conversion happens once, here at the boundary, rather than
    /// travelling as a decimal that every consumer rounds its own way.
    /// ⚠ It is margin data. It belongs on a till only because the item editor writes it back, and
    /// the PUT binds the whole entity — an editor that could not see cost would zero it.
    /// </summary>
    long CostPence = 0,

    /// <summary>
    /// The item's ADDITIONAL barcodes — multi-barcode (`Build/archive/Multi-barcode plan.md`, MB2).
    ///
    /// ⚠⚠ ALIASES, NOT IDENTITIES. <see cref="IdOne"/> is still the item's identity and the only id
    /// anything downstream may carry; these are extra codes that RESOLVE to it. A till that let one
    /// of these reach a sale line would cause two silent faults — a phantom `StockLevel` from
    /// `StockProjectionConsumer`, and a null VAT band from `VatBandStamp`.
    ///
    /// ⚠⚠ THE WHOLE SET, EVERY TIME, AND THE TILL REPLACES ITS ROWS WITH IT. That is what makes
    /// REMOVAL work without tombstones: an item that loses a barcode simply arrives with a smaller
    /// array. Applying the same payload twice is therefore harmless, which is the property the
    /// catalogue-sync design calls for ("full-row payloads, not diffs").
    ///
    /// ⚠ NULL when the item has none — not an empty array — so an item with no aliases serialises
    /// byte-identically to what it did before this field existed.
    ///
    /// ⚠ An alias write TOUCHES `Item.ModifiedAt` server-side. Without that the feed's cursor never
    /// moves and no till ever hears about it: the cursor pages by the ITEM, not by this table.
    /// </summary>
    string[]? Barcodes = null);

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
