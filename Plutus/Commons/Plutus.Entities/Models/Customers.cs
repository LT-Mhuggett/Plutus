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
        public bool Active { get; set; }
        public DateTime CreatedAtUtc { get; set; }
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

    /// <summary>Membership/loyalty: benefits express as an auto-applied discount (a fraction,
    /// e.g. 0.10 = 10% members' discount) through the promotion machinery. Renewal-dated.</summary>
    public class Membership
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid CustomerId { get; set; }
        public string Tier { get; set; }           // e.g. "Club", "Gold"
        public decimal AutoDiscountRate { get; set; } // 0.10 = 10% off, applied at till/webstore
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
