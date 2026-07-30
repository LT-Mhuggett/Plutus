using System;

namespace Plutus.Entities.Models
{
    // Phase 8 (architecture §9.3): customers, store credit as a LIABILITY LEDGER (D15 —
    // never a mutable balance), and membership/loyalty feeding auto-discounts. Server-only,
    // tenant-owned. Customer + membership + credit-account are synced to tills like catalogue
    // data; credit entries are the append-only truth (balance = Σ entries).

    public class Customer
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        /// <summary>FE2: the human-usable membership number — tenant-unique, immutable, assigned at
        /// creation and printed on loyalty cards as a Code 39 barcode. Nullable only so pre-FE2 rows
        /// exist before the backfill fills them; new customers always get one.</summary>
        public string? MemberNo { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>FE2: per-tenant membership-number sequence. One row per tenant; <see cref="Next"/>
    /// doubles as the optimistic-concurrency token, so two simultaneous customer creates cannot
    /// hand out the same number (the loser retries).</summary>
    public class MemberNoCounter
    {
        public Guid TenantId { get; set; }   // PK
        public long Next { get; set; }
    }

    /// <summary>One store-credit account per customer. Balance is NEVER stored here — it is
    /// the sum of the append-only <see cref="CreditEntry"/> rows (D15).</summary>
    public class CreditAccount
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid CustomerId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public enum CreditEntryType : byte
    {
        Issue = 0,    // +pence (refund-to-credit, permission-gated grant)
        Redeem = 1,   // −pence (used as a tender)
        Expire = 2,   // −pence (lapsed credit written off)
    }

    /// <summary>Append-only credit movement. Signed AmountPence: Issue positive, Redeem/Expire
    /// negative. Never updated or deleted — corrections are new entries (D5/D15).</summary>
    public class CreditEntry
    {
        public Guid Id { get; set; }               // client/server-minted UUIDv7 (idempotency anchor)
        public Guid TenantId { get; set; }
        public Guid CreditAccountId { get; set; }
        public CreditEntryType Type { get; set; }
        public long AmountPence { get; set; }      // signed
        public string? Reason { get; set; }
        /// <summary>The sale a redemption/issue is tied to (redeem tender, refund-to-credit).</summary>
        public Guid? SaleId { get; set; }
        public Guid? ActorUserId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>FE1: the tenant's catalogue of loyalty levels — pre-defined so a membership is
    /// ASSIGNED a tier rather than re-typing a name + rate every time. Reads resolve the name and
    /// rate through this row (live-follow: re-rating "Gold" updates every Gold member at once);
    /// recorded sales are immutable, so history never retro-changes. Never hard-deleted —
    /// <see cref="Active"/> false hides it from pickers while its memberships keep working.</summary>
    public class LoyaltyTier
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; }               // tenant-unique (case-insensitive), e.g. "Gold"
        public decimal AutoDiscountRate { get; set; }  // 0.10 = 10% off
        /// <summary>Membership length in months — SetMembership derives RenewalDay from it.</summary>
        public int DurationMonths { get; set; }
        public bool Active { get; set; }
        public int SortOrder { get; set; }             // display order in pickers
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>Membership/loyalty: benefits express as an auto-applied discount (a fraction,
    /// e.g. 0.10 = 10% members' discount) through the promotion machinery. Renewal-dated.</summary>
    public class Membership
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid CustomerId { get; set; }
        public string Tier { get; set; }           // e.g. "Club", "Gold"
        public decimal AutoDiscountRate { get; set; } // 0.10 = 10% off, applied at till/webstore
        /// <summary>FE1: the catalogue tier this membership was assigned. Null = a legacy
        /// free-text membership, where <see cref="Tier"/>/<see cref="AutoDiscountRate"/> ARE the
        /// truth. When set, reads prefer the tier's live name/rate and these columns act as the
        /// as-assigned snapshot (and the fallback if a tier row ever goes missing).</summary>
        public Guid? TierId { get; set; }
        public DateOnly StartDay { get; set; }
        public DateOnly RenewalDay { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>WP5.3 cross-channel identity: links a loyalty <see cref="Customer"/> to their
    /// account on an external channel (currently WooCommerce). First slice is LINK-ONLY, by email —
    /// a webstore order whose buyer email matches an existing customer records a ref here; a
    /// non-match records nothing (no auto-create — D3). One row per (tenant, provider, externalId).</summary>
    public class CustomerExternalRef
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid CustomerId { get; set; }
        public string Provider { get; set; }        // "woo"
        public string ExternalId { get; set; }      // the provider's customer/order key (as text)
        public string? Email { get; set; }          // the email the match was made on (audit trail)
        public DateTime CreatedAtUtc { get; set; }
        public DateTime LastSeenAtUtc { get; set; }
    }
}
