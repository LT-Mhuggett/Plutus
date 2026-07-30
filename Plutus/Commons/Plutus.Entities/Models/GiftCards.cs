using System;

namespace Plutus.Entities.Models
{
    // FE7 gift cards. Modelled on store credit (D15): the balance is ALWAYS Σ entries, never a
    // stored field, so a card's history and its balance can never disagree.
    //
    // ⚠ A gift card is a LIABILITY, not revenue. Activation takes money and promises goods later;
    // the VAT-able supply happens at REDEMPTION, when real goods leave at their own tax bands.
    // That is why the activation sale line carries a zero-rate band (see GiftCardActivation) and
    // why redemption is a TENDER rather than a discount.

    /// <summary>A physical or printed card. Exists (worthless) from the moment it is generated;
    /// becomes spendable when it is sold at a till, which writes the Issue entry.</summary>
    public class GiftCard
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        /// <summary>Tenant-unique, Crockford32 + check character, stored canonical (upper, folded).
        /// Printed as text AND as a Code 39 barcode with a "G" prefix.</summary>
        public string Code { get; set; }

        /// <summary>Optional link to a loyalty customer — the buyer or the recipient. Lets "I've lost
        /// my card" be answered from the customer record.</summary>
        public Guid? CustomerId { get; set; }

        /// <summary>The sale that activated it (null while unsold).</summary>
        public Guid? SoldSaleId { get; set; }

        /// <summary>Null = generated but never sold, so it holds no money and cannot be redeemed.</summary>
        public DateTime? IssuedAtUtc { get; set; }

        /// <summary>Null = never expires (the default — expiry is a per-batch choice).</summary>
        public DateTime? ExpiresAtUtc { get; set; }

        /// <summary>Manual void (lost/stolen/mis-issued). Audited; the ledger is left intact so the
        /// liability report can still explain where the money went.</summary>
        public DateTime? VoidedAtUtc { get; set; }

        /// <summary>Free-text batch label from the generate dialog ("Christmas 2026"), so a print run
        /// can be found again.</summary>
        public string? Batch { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public enum GiftCardEntryType : byte
    {
        /// <summary>Activation — money in, card loaded.</summary>
        Issue = 0,
        /// <summary>Spent as a tender (negative).</summary>
        Redeem = 1,
        /// <summary>Manual correction, either sign (audited).</summary>
        Adjust = 2,
        /// <summary>Written off at expiry (negative).</summary>
        Expire = 3,
    }

    /// <summary>Append-only. Balance = Σ AmountPence. Never updated, never deleted.</summary>
    public class GiftCardEntry
    {
        /// <summary>Client/server-minted UUIDv7 — also the idempotency anchor, so a replayed till
        /// redemption is a no-op rather than a double spend.</summary>
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid GiftCardId { get; set; }
        public GiftCardEntryType Type { get; set; }
        /// <summary>Signed: Issue positive, Redeem/Expire negative.</summary>
        public long AmountPence { get; set; }
        public string? Reason { get; set; }
        /// <summary>The sale this entry belongs to (activation or redemption).</summary>
        public Guid? SaleId { get; set; }
        public Guid? ActorUserId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
