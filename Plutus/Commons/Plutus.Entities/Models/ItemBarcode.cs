using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// One ADDITIONAL barcode that resolves to an item — the multi-barcode entity
    /// (`Build/archive/Multi-barcode plan.md`, MB2).
    ///
    /// ⚠⚠ AN ALIAS IS NOT AN IDENTITY. <see cref="Item.IdOne"/> remains the item's identity for
    /// ever: it is half the composite primary key, it seeds `DeterministicGuid.ForItem` (whose
    /// output is frozen by a golden vector and twinned in TypeScript), five foreign-key families
    /// point at it, and it is on every historical sale line. This table is purely additive — a scan
    /// of a row here RESOLVES to the item and the caller then carries the item's own `IdOne`. Plan
    /// decision D1.
    ///
    /// ⚠⚠ AND THE ALIAS STRING MUST NEVER TRAVEL PAST RESOLUTION (plan D2). Two silent faults
    /// punish a leak: `StockProjectionConsumer` creates a phantom `StockLevel` for any id it does
    /// not recognise, with no error, and `VatBandStamp` quietly leaves the line's VAT band null.
    /// Neither shows up anywhere a person would look.
    ///
    /// ⚠ NO FOREIGN KEY to `Items`, matching every other barcode-keyed table on this database
    /// (`PriceListEntry`, `PriceOverride`, `ItemPricePolicy`, `StockLevel` are all plain strings).
    /// The writer validates the item exists; orphans cannot arise because items are never hard
    /// deleted — the Bin is a soft delete. Plan D12.
    ///
    /// ⚠ Uniqueness is `(TenantId, Code)` and the writer ALSO refuses a code that is any item's own
    /// `IdOne`. Two items answering one scan is the ambiguity the till's own unique index exists to
    /// prevent, and it would be unresolvable at a counter. Plan D8.
    /// </summary>
    public class ItemBarcode
    {
        /// <summary>UUIDv7, minted in code (`Uuid7.New()`), never database-generated.</summary>
        public Guid Id { get; set; }

        /// <summary>⚠ A REAL column, and the type is in `MySqlDbContext.TenantOwned` — that array is
        /// what makes the tenant query filter impossible to forget.</summary>
        public Guid TenantId { get; set; }

        /// <summary>The alias, exactly as it will be scanned. ⚠ Trimmed but NEVER case-folded — see
        /// `ItemBarcodeRules.Normalise` for why (plan D6).</summary>
        public string Code { get; set; }

        /// <summary>⚠ The item's CANONICAL <see cref="Item.IdOne"/> — what a resolver hands on, and
        /// the only id anything downstream may see.</summary>
        public string ItemIdOne { get; set; }

        /// <summary>The item's <see cref="Item.IdTwo"/> (the legacy business id). Carried so a
        /// resolver can rebuild the item's key without a second lookup, and so an audit row can say
        /// which business a code belonged to.</summary>
        public Guid BusinessId { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
