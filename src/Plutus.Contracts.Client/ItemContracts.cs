using System;
using System.Text.Json.Serialization;

namespace Plutus.Contracts.Client;

/// <summary>
/// An item as the LEGACY `/api/Item` endpoints bind it (WP10 / cutover step 25).
///
/// ⚠ THE PUT BINDS THE WHOLE ENTITY, and that is the trap this type exists to make unmissable.
/// `PUT /api/Item/{id1}` takes a complete `Item`, so any field left out is written back as its
/// default — an ordinary price change would silently clear `StockUntracked` (turning a service or a
/// carrier bag into stock-tracked goods) or blank `BinnedAtUtc` (RESTORING a withdrawn item to
/// sale). The web till carries the same warning in `api.ts itemBody`, having been bitten first.
///
/// So the only safe edit is READ-MODIFY-WRITE: fetch the item, change what the operator changed,
/// send it all back. `PlutusApiClient.UpdateItemFieldsAsync` does exactly that and is the method to
/// use; this type is deliberately not convenient to construct from scratch.
///
/// ⚠ Legacy endpoints, and deliberately so — they are what the WEB TILL calls in production today
/// (binding default 10: when in doubt, match the web till). A parallel v2 item API would be a
/// second door onto the same table.
///
/// ⚠ MONEY IS `decimal` HERE, uniquely. Everywhere else in this project money is integer pence; the
/// legacy `Item` columns are decimal and the wire binds them directly, so converting on the way in
/// or out would introduce a rounding step between the till and the catalogue that nothing else has.
/// Convert at the SCREEN, never here.
/// </summary>
public sealed class ItemDto
{
    /// <summary>The barcode. ⚠ Also the composite key's first half — `IdOne` must be present on a
    /// PUT or the entity will not bind.</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("idOne")] public string? IdOne { get; set; }

    /// <summary>The LEGACY business id. ⚠ Not the tenant id — item ids derive from this, and the
    /// wrong one diverges every id from the web till's, permanently and without a symptom.</summary>
    [JsonPropertyName("idTwo")] public Guid IdTwo { get; set; }
    [JsonPropertyName("businessId")] public Guid BusinessId { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>⚠ The web till sends "-" rather than empty when a brand is unknown; matched here so
    /// two tills do not write different placeholders into the same column.</summary>
    [JsonPropertyName("brand")] public string? Brand { get; set; }

    [JsonPropertyName("desc")] public string? Desc { get; set; }
    [JsonPropertyName("cost")] public decimal Cost { get; set; }

    /// <summary>⚠ THE PAIR IS GUARDED SERVER-SIDE: `|Price − ExPrice × rate| ≤ 2p`, or the write is
    /// a 400. Free-typed ex-prices corrupted 47 live items (a £7.99 item with a £799.00 ex-price)
    /// and with them every downstream VAT figure. Derive the ex price from the inc price and the
    /// band; never let an operator type both.</summary>
    [JsonPropertyName("exPrice")] public decimal ExPrice { get; set; }
    [JsonPropertyName("price")] public decimal Price { get; set; }

    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("amount")] public int Amount { get; set; }
    [JsonPropertyName("taxId")] public int TaxId { get; set; }
    [JsonPropertyName("catId")] public Guid CatId { get; set; }

    /// <summary>⚠ ECHOED BACK ON EVERY EDIT. Omit it and a service or carrier bag silently becomes
    /// stock-tracked.</summary>
    [JsonPropertyName("stockUntracked")] public bool StockUntracked { get; set; }

    /// <summary>⚠ ECHOED BACK ON EVERY EDIT. Omit it and editing a BINNED item RESTORES it to
    /// sale — on every till, offline, via the catalogue feed's tombstone being lifted.</summary>
    [JsonPropertyName("binnedAtUtc")] public DateTime? BinnedAtUtc { get; set; }
}

/// <summary>
/// A tax band as `/api/Tax/Index` returns it.
///
/// ⚠ <see cref="Rate"/> IS A MULTIPLIER, NOT A PERCENTAGE — 1.2 means 20% VAT, 1.0 means zero-rated
/// or exempt. Reading it as a percentage makes a £10 item cost £2, and the server's band guard
/// (`|price − exPrice × rate| ≤ 2p`) would reject the write with a message about ex-prices that
/// gives no hint the units were wrong. The web till says the same thing in `api.ts`.
///
/// ⚠ ZERO-RATED AND EXEMPT BOTH HAVE RATE 1.0 AND ARE NOT THE SAME THING. They are different bands
/// with different names and they land in different boxes on a VAT return; only the band id
/// distinguishes them. Never collapse them by comparing rates.
/// </summary>
public sealed class TaxBandDto
{
    [JsonPropertyName("idOne")] public int IdOne { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("rate")] public decimal Rate { get; set; }
}

/// <summary>
/// On-hand quantity for one item, as `POST /api/v1/stock/levels/bulk` reports it.
///
/// ⚠ THE TWO KINDS OF "NO NUMBER" ARE DIFFERENT AND BOTH ARRIVE AS NULL. `Quantity` is null when
/// the item is <see cref="Untracked"/> — its level is meaningless by design — and ALSO when there
/// is simply no stock record, because nothing has ever been received. The flag is what tells them
/// apart, and a client that ignores it will print "0" for a carrier bag.
///
/// ⚠ Neither case is a zero. Inventing one is the bug this shape exists to prevent: an operator
/// reading "0" against an item the shop has never counted will reorder it.
/// </summary>
public sealed class StockLevelDto
{
    [JsonPropertyName("itemIdOne")] public string? ItemIdOne { get; set; }
    [JsonPropertyName("untracked")] public bool Untracked { get; set; }
    [JsonPropertyName("quantity")] public int? Quantity { get; set; }

    /// <summary>What the column shows: a number, "∞" for untracked, "—" for never counted.</summary>
    public string Display => Untracked ? "∞" : Quantity?.ToString() ?? "—";
}

/// <summary>A catalogue category as `/api/Category/Index` returns it.</summary>
public sealed class CategoryDto
{
    [JsonPropertyName("idOne")] public Guid IdOne { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}
