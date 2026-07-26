using System;

namespace Plutus.Entities.Models
{
    // Phase 6 WooCommerce connector — server-only, tenant-owned config + review-queue tables.
    // The connector LOGIC lives in the isolated src/Plutus.Webstore module; only these data rows
    // live here in the shared model (same pattern as every other feature's entities). Secrets
    // (consumer secret, webhook secret) are NEVER stored on the row — they live in server config
    // keyed by the connection Id (the TEST_TOKEN_SECRET / BILLING_WEBHOOK_SECRET pattern).

    /// <summary>One connected webstore (WP11.7 shell + WP6.1 connection). A webstore is a sales
    /// CHANNEL (SaleChannel.WebStore), not a physical location — its sales ride a virtual till/
    /// device so reports slice channel × store cleanly. Name is unique per tenant.</summary>
    public class WebStoreDetails
    {
        public Guid Id { get; set; }                 // PK (UUIDv7)
        public Guid TenantId { get; set; }
        public string Name { get; set; }             // tenant-unique (case-insensitive)
        public string? Url { get; set; }             // site base URL (…/wp-json/wc/v3)
        public string Provider { get; set; } = "woocommerce";
        /// <summary>Fulfilment store whose stock backs this webstore (null = not yet chosen).</summary>
        public int? StoreId { get; set; }
        public bool Enabled { get; set; }
        /// <summary>Virtual till/device that carry this webstore's WebStore-channel sales.</summary>
        public Guid TillId { get; set; }
        public Guid DeviceId { get; set; }
        /// <summary>WP6.3 oversell buffer — list max(0, level − buffer) to the web.</summary>
        public int OversellBuffer { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>WP6.2 review queue: a Woo SKU that didn't match a catalogue item on ingest. A human
    /// binds it to an item, ignores it, or creates a new item (WP6.5). The order that hit it is
    /// held until the SKU resolves. One row per (tenant, webstore, SKU); re-seeing bumps SeenCount.</summary>
    public class WebstoreSkuMap
    {
        public Guid Id { get; set; }                 // PK (UUIDv7)
        public Guid TenantId { get; set; }
        public Guid WebStoreId { get; set; }
        public string Sku { get; set; }
        /// <summary>Pending | Bound | Ignored.</summary>
        public string Status { get; set; } = "Pending";
        /// <summary>The catalogue item (Items.IdOne) this SKU was bound to, when Status = Bound.</summary>
        public string? BoundItemIdOne { get; set; }
        public long? FirstSeenWooOrderId { get; set; }
        public int SeenCount { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
