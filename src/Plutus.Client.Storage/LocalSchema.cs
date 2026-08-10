using System;

namespace Plutus.Client.Storage;

// ─────────────────────────────────────────────────────────────────────────────
// MAUI retrofit WP2 — local store schema v2.
//
// THE DECISION THIS ENCODES: the till's database is an OPERATIONAL CACHE, NOT AN ARCHIVE.
// History lives centrally. The till keeps the catalogue, prices, permissions, sync cursors, the
// outbox, parked baskets, and a rolling window of recent sales for reprint and X/Z. Seven years
// of trading does not ride along on every counter — and once sales sync centrally, a till holding
// its own copy of history is a second source of truth that will eventually disagree.
//
// Money is INTEGER PENCE throughout and ids are GUIDs — the legacy schema's decimals and string
// ids are exactly what the platform moved away from, and the wire contract will not accept them.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Key/value for the till's own identity and cursors: serverUrl, deviceId, tenantId,
/// businessId, tillId, storeId, deviceSeq, catalogueVersion, schemaVersion.</summary>
public class MetaEntry
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}

/// <summary>A catalogue item as the till knows it.</summary>
public class CatalogueItem
{
    /// <summary>The platform's item id — DERIVED, not assigned: DeterministicGuid.ForItem(businessId,
    /// idOne). Every till computes the same id for the same item with no mapping table.</summary>
    public Guid Id { get; set; }

    /// <summary>⚠ The canonical legacy id and default barcode. EVERY v1 stock, price and sale-line
    /// call keys on this string, and it CANNOT be recovered from <see cref="Id"/> (a one-way hash),
    /// so it must be stored, not derived back.</summary>
    public string IdOne { get; set; } = "";

    public string Name { get; set; } = "";
    /// <summary>0 Product · 1 Department.</summary>
    public int Kind { get; set; }
    public long PricePence { get; set; }
    public int VatRateBp { get; set; }
    public Guid? CategoryId { get; set; }
    public string? BandData { get; set; }

    /// <summary>
    /// FE5: this item sells without decrementing anything — services, carrier bags, a delivery
    /// charge.
    ///
    /// ⚠ The till must not show a stock level for it, must not warn about selling below zero, and
    /// must NOT post a stock movement. `CatalogueItemDto` has carried this since FE5 and the
    /// mapper silently dropped it, so WP10's untracked behaviour had nothing to read and every
    /// carrier bag looked like an item going permanently more negative.
    /// </summary>
    public bool StockUntracked { get; set; }

    /// <summary>FE5.4: a binned item must stop being sellable even on an offline till, which is
    /// why the changes feed carries tombstones rather than just upserts.</summary>
    public bool Removed { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>A future-dated price. Held locally so a price scheduled for 02:00 activates at 02:00
/// even on a till that has been offline for a week — the lookup compares at basket-add time
/// rather than relying on a sync happening at the right moment.</summary>
public class PriceScheduleEntry
{
    public Guid ItemId { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public long PricePence { get; set; }
}

// ⚠ `BarcodeAlias` WAS HERE AND IS DELETED (2026-08-09). The table existed, was mapped, and was
// read by FindByBarcodeAsync — and **nothing in the history of this repo ever wrote a row to it**,
// because the platform has no barcode entity for a feed to carry. Matt confirmed the same day that
// multi-barcode items are not needed.
//
// Deleted rather than left empty: a mapped table and a live read path advertise a feature that does
// not exist, so the next person asked for multiple barcodes would reasonably believe the till
// already half-supports them and go looking for the bug. `CatalogueItem.IdOne` IS the barcode.
// Real support needs a server entity, a feed field and a portal UI first.

/// <summary>An operator who may sign in at this till, with the credential hash and permission set
/// synced down so login works with the network off (§9.2, WP8).</summary>
public class LocalOperator
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public byte[]? CredentialHash { get; set; }
    public byte[]? CredentialSalt { get; set; }
    public string? PermissionsJson { get; set; }
    public string? TimeWindowsJson { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// A sale committed at this till. ⚠ This table IS the outbox (Status=Pending) AND the rolling
/// window (Status=Pushed) — one table, one transaction, no dual-write. That is deliberate: a sale
/// and its queue entry can never disagree, because they are the same row.
///
/// <see cref="PayloadJson"/> is the exact contract payload, so the pusher never re-serialises from
/// entities: what was committed at checkout is byte-for-byte what reaches the server.
/// </summary>
public class LocalSale
{
    public Guid SaleId { get; set; }
    public long DeviceSeq { get; set; }
    public string BusinessDay { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string PayloadJson { get; set; } = "";
    /// <summary>Mirrors Plutus.Client.Core.OutboxStatus.</summary>
    public int Status { get; set; }
    public DateTime? PushedAtUtc { get; set; }
    public string? ServerResponseJson { get; set; }
    public int Attempts { get; set; }
}

/// <summary>
/// How much of an EARLIER sale a later sale gave back. One row per (refund sale, origin sale).
///
/// ⚠ WHY A TABLE AND NOT A COLUMN ON <see cref="LocalSale"/>. One basket can refund lines from two
/// different original sales, and a single `OriginSaleId` column cannot say so — it would have to
/// pick one and drop the other, or go null. Either way `AlreadyRefundedPenceAsync` UNDERCOUNTS,
/// and undercounting what has already been given back is precisely how a till refunds more than
/// the customer ever paid. Matt's binding default 12: *"You should not be able to refund MORE than
/// the price paid for it."* A cap computed from an incomplete history is not a cap.
///
/// ⚠ WHY IT EXISTS AT ALL: the origin id is buried inside `PayloadJson` (in each line's
/// `LineMeta.Return`), which SQLite cannot index or sum over. Written in the SAME transaction as
/// the sale it belongs to, from that sale's own payload, so the two can never disagree.
/// </summary>
public class LocalRefund
{
    /// <summary>The REFUND sale — the one being rung now.</summary>
    public Guid SaleId { get; set; }

    /// <summary>The ORIGINAL sale whose goods are coming back.</summary>
    public Guid OriginSaleId { get; set; }

    /// <summary>What this sale gave back against that origin, as a POSITIVE number of pence.
    /// ⚠ Positive: refund line gross is negative, and a cap compared against a negative number
    /// passes everything.</summary>
    public long RefundedPence { get; set; }
}

/// <summary>A parked basket. Serialised as CONTRACT JSON with no .NET `$type` metadata — the
/// legacy blobs carried `NatApp.Plutus.*` type names that break the moment a namespace changes,
/// which is what made discounted parked baskets crash on recall.</summary>
public class SavedBasket
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string ContractJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// A price as the wire wants it: both halves, from the same point in the same timeline.
///
/// ⚠ NOT a price and a rate. `SharedKernel.VatLineMath` derives the line's declared rate FROM this
/// pair, so the two numbers must have been a pair when they were published — pairing an inc price
/// from the timeline with an ex price derived from a snapped rate produces a rate nobody set.
/// </summary>
public readonly record struct PricePair(long IncPence, long ExPence);

/// <summary>
/// What the till needs to know about an item's tax treatment, beyond its rate.
/// </summary>
/// <param name="TaxId">The LEGACY tax row. ⚠ This is what distinguishes zero-rated from exempt —
/// both price at 0% and are different in law (HMRC Notice 706): exempt supplies block recovery of
/// input tax attributable to them, zero-rated ones do not. No rate can carry that, which is why
/// the band identity travels with the sale line.</param>
/// <param name="StockUntracked">Sells without moving stock — services, carrier bags.</param>
public readonly record struct ItemTaxInfo(int TaxId, int VatRateBp, bool StockUntracked);

/// <summary>
/// Enough of a past sale to RECOGNISE it in a list — not enough to reason about it.
///
/// ⚠ NEVER REFUND FROM THIS. It carries no lines, so it cannot say what is still returnable. The
/// caller picks a sale here and then reads the real thing through
/// <see cref="TillStore.FindLocalSaleAsync"/> (or the server, which knows what OTHER tills have
/// already given back). A cap computed from a summary is not a cap.
/// </summary>
/// <param name="Status">Mirrors <c>Plutus.Client.Core.OutboxStatus</c> — a sale still queued is
/// perfectly refundable (the money left the drawer when it was handed over), so this is shown, not
/// filtered on.</param>
public readonly record struct LocalSaleSummary(
    Guid SaleId,
    DateTime OccurredAtUtc,
    string BusinessDay,
    long GrossPence,
    int LineCount,
    string FirstItemIdOne,
    int Status);
