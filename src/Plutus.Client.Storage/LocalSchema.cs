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

/// <summary>Extra barcodes pointing at an item. The item's own <see cref="CatalogueItem.IdOne"/>
/// is its default code — there is no server-side Barcode entity, so these are aliases only.</summary>
public class BarcodeAlias
{
    public string Code { get; set; } = "";
    public Guid ItemId { get; set; }
}

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
