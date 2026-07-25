using System;

namespace Plutus.Entities.Models
{
    // WP5.4 pricing (architecture §7.4): a per-item policy decides who owns the price;
    // company price-list entries and store overrides are EFFECTIVE-DATED (schedule the
    // Sunday-night repricing); resolution = store override (if policy permits) → else the
    // company price list → else the legacy Items price (evolve-in-place baseline).
    // Every change audited. Server-only, tenant-owned. Item granularity for now — the
    // per-category-with-exceptions form arrives with the category manager.

    public enum PricePolicy : byte
    {
        Central = 0,             // HQ sets; stores read-only
        CentralWithOverride = 1, // HQ default; a store override survives HQ changes; HQ can force-reset
        Local = 2,               // the store owns the price; HQ sees but doesn't set
    }

    public class ItemPricePolicy
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string ItemIdOne { get; set; }
        public PricePolicy Policy { get; set; }
    }

    /// <summary>Company (HQ) price list — the latest entry whose EffectiveFromUtc has passed
    /// wins. Entries are never edited or deleted: repricing appends.</summary>
    public class PriceListEntry
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string ItemIdOne { get; set; }
        public long PricePence { get; set; }       // VAT-inclusive
        public long ExPricePence { get; set; }     // ex-VAT
        public DateTime EffectiveFromUtc { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>A store's price for an item (an override under CENTRAL_WITH_OVERRIDE, THE
    /// price under LOCAL). Survives HQ repricing; a force-reset revokes it.</summary>
    public class PriceOverride
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public int StoreId { get; set; }
        public string ItemIdOne { get; set; }
        public long PricePence { get; set; }
        public long ExPricePence { get; set; }
        public DateTime EffectiveFromUtc { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public Guid? RevokedBy { get; set; }
    }
}
