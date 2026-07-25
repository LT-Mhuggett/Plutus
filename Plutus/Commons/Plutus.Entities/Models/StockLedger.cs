using System;

namespace Plutus.Entities.Models
{
    // WP5.1 stock ledger (architecture §5/§7.3): stock is an APPEND-ONLY movement ledger;
    // current quantity is a materialised sum, rebuildable from the ledger — what makes
    // multi-channel stock reconcilable when things go wrong. Server-only, tenant-owned.
    // Items are keyed by the legacy natural key (ItemIdOne) during evolve-in-place, with
    // the deterministic platform ItemId (SharedKernel.DeterministicGuid) carried alongside
    // for the Phase-5 item-UUID adoption.

    public enum StockLocationType : byte { Store = 0, Warehouse = 1 }

    public enum StockMovementType : byte
    {
        Receipt = 0,      // goods-in (WP5.3)
        TransferOut = 1,  // paired transfer (WP5.2)
        TransferIn = 2,
        Sale = 3,         // folded from SaleRecorded
        Return = 4,       // folded from SaleRecorded (negative-qty lines)
        Adjustment = 5,   // manual correction / stock take / opening balance
        WriteOff = 6,
    }

    public class StockLocation
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>The store this location belongs to (a WAREHOUSE hangs off a store too
        /// during evolve-in-place; company-level warehouses arrive with multi-store tenants).</summary>
        public int StoreId { get; set; }
        public StockLocationType Type { get; set; }
        public string Name { get; set; }
    }

    public class StockMovement
    {
        public Guid Id { get; set; }               // UUIDv7 — time-ordered
        public Guid TenantId { get; set; }
        public Guid StockLocationId { get; set; }
        public string ItemIdOne { get; set; }      // legacy natural key (EAN/UPC)
        public Guid ItemId { get; set; }           // deterministic platform id
        public StockMovementType Type { get; set; }
        /// <summary>Signed quantity change (SALE negative, RETURN/RECEIPT positive…).</summary>
        public int QtyDelta { get; set; }
        public string? Reason { get; set; }
        /// <summary>What caused it: saleId, transferId, purchase order id…</summary>
        public Guid? RefId { get; set; }
        public Guid? ActorUserId { get; set; }
        public DateTime AtUtc { get; set; }
    }

    /// <summary>Materialised current quantity per location+item — ALWAYS equals the ledger
    /// sum (property-tested; rebuildable via StockRebuilder).</summary>
    public class StockLevel
    {
        public long Id { get; set; }               // AUTO_INCREMENT
        public Guid TenantId { get; set; }
        public Guid StockLocationId { get; set; }
        public string ItemIdOne { get; set; }
        public int Quantity { get; set; }
    }
}
