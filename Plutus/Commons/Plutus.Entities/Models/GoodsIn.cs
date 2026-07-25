using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    // WP5.3 goods-in (architecture §9.4): Suppliers → PurchaseOrders → POLines; receiving
    // posts RECEIPT movements into the WP5.1 ledger (partials supported, cost captured).
    // Server-only, tenant-owned.

    public class Supplier
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public enum PurchaseOrderStatus : byte { Open = 0, PartiallyReceived = 1, Received = 2, Cancelled = 3 }

    public class PurchaseOrder
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid SupplierId { get; set; }
        /// <summary>Destination store — receipts land at its STORE stock location.</summary>
        public int StoreId { get; set; }
        public PurchaseOrderStatus Status { get; set; }
        public string? Reference { get; set; }
        public string? Notes { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public List<POLine> Lines { get; set; } = new();
    }

    public class POLine
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PurchaseOrderId { get; set; }
        public string ItemIdOne { get; set; }
        public Guid ItemId { get; set; }
        public int QtyOrdered { get; set; }
        public int QtyReceived { get; set; }
        /// <summary>Agreed unit cost in pence; updated with the actual cost at receipt.</summary>
        public long UnitCostPence { get; set; }
    }
}
