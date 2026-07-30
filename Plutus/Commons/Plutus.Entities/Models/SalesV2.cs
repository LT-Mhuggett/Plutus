using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.Entities.Models
{
    // Sales schema v2 (spec T1.3). New, server-only tables that live ALONGSIDE the legacy
    // Sale/Transaction tables during evolve-in-place; the Kapow migrator (T1.8) moves data in,
    // and the legacy tables are dropped later. The header table is "SalesV2" (not "Sales") to
    // avoid colliding with the legacy Sales table; it is renamed to "Sales" after the drop.
    // Money is integer pence (long); ids are UUIDv7 (stored char(36), consistent with the rest
    // of this evolve-in-place DB rather than the spec's BINARY(16)).

    public enum SaleChannel : byte { Till = 0, WebPos = 1, WebStore = 2 }
    // FE7 adds GiftCard = 4. A new enum VALUE on a byte column needs no migration; it is kept
    // distinct from Credit so the payment-split report doesn't lump gift cards in with store credit —
    // they are different liabilities with different reconciliations.
    public enum TenderType : byte { Cash = 0, Card = 1, Online = 2, Credit = 3, GiftCard = 4 }
    public enum AdjustmentType : byte { Refund = 0, Void = 1 }

    /// <summary>Thrown when a sale's totals do not reconcile (T1.3 invariants).</summary>
    public sealed class InvalidSaleException : Exception
    {
        public InvalidSaleException(string message) : base(message) { }
    }

    public class SaleV2
    {
        public Guid Id { get; set; }              // client-minted UUIDv7 saleId
        public Guid TenantId { get; set; }
        public Guid TillId { get; set; }
        public Guid DeviceId { get; set; }
        public long DeviceSeq { get; set; }
        public SaleChannel Channel { get; set; }
        public DateOnly BusinessDay { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
        public long GrossPence { get; set; }
        public long VatPence { get; set; }
        public string? LegacyRef { get; set; }
        public Guid? OperatorUserId { get; set; }
        public string? Note { get; set; }
        public bool VatReconstructed { get; set; }

        public List<SaleLine> Lines { get; set; } = new();
        public List<SaleTender> Tenders { get; set; } = new();

        /// <summary>Builds a sale graph and enforces the T1.3 money invariants, throwing
        /// <see cref="InvalidSaleException"/> if anything fails to reconcile (to the penny).</summary>
        public static SaleV2 Create(
            Guid id, Guid tenantId, Guid tillId, Guid deviceId, long deviceSeq,
            SaleChannel channel, DateOnly businessDay, DateTime occurredAtUtc, DateTime receivedAtUtc,
            long grossPence, long vatPence, IEnumerable<SaleLine> lines, IEnumerable<SaleTender> tenders,
            string legacyRef = null, Guid? operatorUserId = null, string note = null, bool vatReconstructed = false)
        {
            var sale = new SaleV2
            {
                Id = id, TenantId = tenantId, TillId = tillId, DeviceId = deviceId, DeviceSeq = deviceSeq,
                Channel = channel, BusinessDay = businessDay, OccurredAtUtc = occurredAtUtc, ReceivedAtUtc = receivedAtUtc,
                GrossPence = grossPence, VatPence = vatPence, LegacyRef = legacyRef,
                OperatorUserId = operatorUserId, Note = note, VatReconstructed = vatReconstructed,
                Lines = (lines ?? Enumerable.Empty<SaleLine>()).ToList(),
                Tenders = (tenders ?? Enumerable.Empty<SaleTender>()).ToList(),
            };
            sale.Validate();
            return sale;
        }

        /// <summary>The four T1.3 invariants. Also callable after an EF round-trip.</summary>
        public void Validate()
        {
            if (Lines.Count == 0) throw new InvalidSaleException("A sale must have at least one line.");

            foreach (var l in Lines)
            {
                var expected = checked(l.UnitPricePence * l.Qty - l.DiscountPence);
                if (l.LineGrossPence != expected)
                    throw new InvalidSaleException(
                        $"Line {l.LineNo}: LineGrossPence {l.LineGrossPence} != UnitPrice {l.UnitPricePence} × Qty {l.Qty} − discount {l.DiscountPence} ({expected}).");
            }

            var lineSum = Lines.Sum(l => l.LineGrossPence);
            if (GrossPence != lineSum)
                throw new InvalidSaleException($"GrossPence {GrossPence} != Σ line gross {lineSum}.");

            var vatSum = Lines.Sum(l => l.VatAmountPence);
            if (VatPence != vatSum)
                throw new InvalidSaleException($"VatPence {VatPence} != Σ line VAT {vatSum}.");

            var tenderNet = Tenders.Sum(t => t.AmountPence) - Tenders.Sum(t => t.ChangePence);
            if (tenderNet != GrossPence)
                throw new InvalidSaleException($"Net tender {tenderNet} (Σ amount − Σ change) != GrossPence {GrossPence}.");
        }
    }

    public class SaleLine
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid SaleId { get; set; }
        public int LineNo { get; set; }
        public Guid ItemId { get; set; }
        /// <summary>The item's legacy barcode/natural key (Items.IdOne). Carried so sale lines join
        /// back to the catalogue for item-level reports — the migrated ItemId is a minted surrogate
        /// that doesn't. Null only for the reconciliation sentinel line.</summary>
        public string? ItemIdOne { get; set; }
        public int Qty { get; set; }
        public long UnitPricePence { get; set; }
        public long LineGrossPence { get; set; }
        public long DiscountPence { get; set; }   // total line discount (breakdown in DiscountsJson)
        public int VatRateBp { get; set; }         // basis points: 2000 = 20%
        public long VatAmountPence { get; set; }
        public long? OverriddenFromPence { get; set; }
        public string? DiscountsJson { get; set; }
    }

    public class SaleTender
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid SaleId { get; set; }
        public TenderType TenderType { get; set; }
        public long AmountPence { get; set; }
        public long ChangePence { get; set; }
        public string? ProviderRef { get; set; }
    }

    public class SaleAdjustment
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public AdjustmentType Type { get; set; }
        public Guid OriginalSaleId { get; set; }
        public Guid? AdjustmentSaleId { get; set; }
        public Guid? ItemId { get; set; }
        public int? Qty { get; set; }
        public long AmountPence { get; set; }
        public string Reason { get; set; }
        public Guid? AuthoriserUserId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class SaleQuarantine
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid SaleId { get; set; }   // T1.4: idempotency anchor for re-POSTed quarantines
        public string PayloadJson { get; set; }
        public string Reason { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
    }

    public class OutboxEvent
    {
        public long Id { get; set; }               // AUTO_INCREMENT
        public Guid EventId { get; set; }          // unique
        public Guid TenantId { get; set; }
        public string EventType { get; set; }
        public string PayloadJson { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class ConsumerOffset
    {
        public string ConsumerName { get; set; }   // PK
        public long LastOutboxId { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
