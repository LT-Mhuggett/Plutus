using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Migration.Kapow
{
    public sealed class KapowLineInput
    {
        public string OldItemId { get; set; } = "";
        public int Qty { get; set; }
        public string UnitPriceText { get; set; } = "0";
        public string? DiscountText { get; set; }
        /// <summary>Σ of Transaction_Discounts.DiscountRate for this line — a FRACTION (0.10 = 10%
        /// off), applied to the pre-discount line gross. Takes precedence over DiscountText.</summary>
        public double DiscountRate { get; set; }
        public double VatMultiplier { get; set; } = 1.0; // Vats.Rate (×1.2 etc.)
        public long? OverriddenFromPence { get; set; }   // from CheckoutItemChange, if any
    }

    public sealed class KapowTenderInput
    {
        public byte TenderType { get; set; }
        public string AmountText { get; set; } = "0";
        public string? ChangeText { get; set; }
    }

    public sealed class KapowSaleInput
    {
        public string LegacyId { get; set; } = "";        // Sales.Id (timestamp string)
        public string CreatedLocal { get; set; } = "";    // completion time (local, zoneless)
        public string? DateOfSaleLocal { get; set; }      // basket-start; source of BusinessDay
        public string TotalText { get; set; } = "0";      // Sales.Total (decimal text)
        public Guid TenantId { get; set; }
        public Guid TillId { get; set; }
        public Guid DeviceId { get; set; }
        public long DeviceSeq { get; set; }
        public Guid? OperatorUserId { get; set; }
        public string? Note { get; set; }
        public List<KapowLineInput> Lines { get; set; } = new();
        public List<KapowTenderInput> Tenders { get; set; } = new();
    }

    public sealed class MapResult
    {
        public SaleV2? Sale { get; init; }
        public string? QuarantineReason { get; init; }
        public bool IsQuarantined => Sale == null;
        /// <summary>On a total mismatch: Σ line gross − declared Sales.Total (pence, signed).
        /// Null for other quarantine causes. Used only for migration diagnostics.</summary>
        public long? TotalDeltaPence { get; init; }
        /// <summary>True when Sales.Total was trusted and a reconciling line was added.</summary>
        public bool WasReconciled { get; init; }
        public static MapResult Ok(SaleV2 s, bool wasReconciled = false) => new() { Sale = s, WasReconciled = wasReconciled };
        public static MapResult Quarantine(string reason, long? totalDeltaPence = null)
            => new() { QuarantineReason = reason, TotalDeltaPence = totalDeltaPence };
    }

    /// <summary>
    /// Maps one Kapow sale (header + lines + tenders) to a sales-v2 <see cref="SaleV2"/>, applying
    /// the gap-analysis corrections: UUIDv7 saleId (legacy timestamp kept in LegacyRef, F1),
    /// decimal-text → pence (F3), per-line VAT reconstructed from the item's rate multiplier
    /// (F2, flagged VatReconstructed), UTC + BusinessDay (timestamps). Any parse failure or
    /// totals/tender mismatch → quarantine (F3 "validate SUM(lines)=Total, quarantine mismatches").
    /// </summary>
    public static class KapowSaleMapper
    {
        /// <summary>Sentinel ItemId for a legacy reconciliation adjustment line (the Δ absorbed
        /// when Sales.Total is trusted over the lossy line detail). Lets reporting isolate these.</summary>
        public static readonly Guid ReconciliationItemId = new Guid("0000da7a-0000-7000-8000-000000000001");

        public static MapResult MapSale(KapowSaleInput input, IdRemap<string> itemRemap, TimeZoneInfo? zone = null)
        {
            try
            {
                var saleId = Uuid7.New();

                var declaredTotal = KapowMoney.ParsePence(input.TotalText);
                var occurredAtUtc = KapowTime.ToUtc(input.CreatedLocal, zone);
                var businessDay = KapowTime.BusinessDay(input.DateOfSaleLocal ?? input.CreatedLocal);

                var lines = new List<SaleLine>();
                int lineNo = 0;
                foreach (var l in input.Lines)
                {
                    lineNo++;
                    var unit = KapowMoney.ParsePence(l.UnitPriceText);
                    var grossBeforeDiscount = unit * l.Qty;
                    // Discount recorded at sale (F-note §2): a rate fraction takes precedence;
                    // otherwise an explicit pence amount if supplied.
                    var disc = l.DiscountRate > 0
                        ? (long)Math.Round(grossBeforeDiscount * (decimal)l.DiscountRate, MidpointRounding.AwayFromZero)
                        : (string.IsNullOrWhiteSpace(l.DiscountText) ? 0 : KapowMoney.ParsePence(l.DiscountText));
                    var lineGross = grossBeforeDiscount - disc;
                    lines.Add(new SaleLine
                    {
                        Id = Uuid7.New(),
                        TenantId = input.TenantId,
                        SaleId = saleId,
                        LineNo = lineNo,
                        ItemId = itemRemap.GetOrMint(l.OldItemId),
                        ItemIdOne = l.OldItemId,   // barcode preserved for item-level reporting
                        Qty = l.Qty,
                        UnitPricePence = unit,
                        DiscountPence = disc,
                        LineGrossPence = lineGross,
                        VatRateBp = KapowVat.ToBasisPoints(l.VatMultiplier),
                        VatAmountPence = KapowVat.VatFromInclusive(lineGross, l.VatMultiplier),
                        OverriddenFromPence = l.OverriddenFromPence,
                    });
                }

                // Trust Sales.Total (decision 2026-07-24). The legacy line detail is lossy —
                // Σlines ≠ Sales.Total for ~38% of sales (uncaptured discounts + the DiscountRate=0
                // regression), and the live app itself never equated them. Sales.Total is the
                // amount actually charged (and matched by the tenders), so it is authoritative.
                // Rather than drop those sales, keep the original lines and absorb the difference
                // in one reconciling line, so GrossPence == Sales.Total and the invariant holds.
                // Flagged VatReconstructed + noted; this can never recur (T1.3 enforces it forward).
                var lineGrossSum = lines.Sum(x => x.LineGrossPence);
                string? note = input.Note;
                var wasReconciled = false;
                if (lineGrossSum != declaredTotal)
                {
                    var delta = declaredTotal - lineGrossSum;
                    lines.Add(new SaleLine
                    {
                        Id = Uuid7.New(),
                        TenantId = input.TenantId,
                        SaleId = saleId,
                        LineNo = ++lineNo,
                        ItemId = ReconciliationItemId,
                        Qty = 1,
                        UnitPricePence = delta,
                        DiscountPence = 0,
                        LineGrossPence = delta,
                        VatRateBp = 0,          // VAT band of the lost detail is unknown
                        VatAmountPence = 0,
                    });
                    var recon = $"legacy-reconciled: Σlines {lineGrossSum}p → Sales.Total {declaredTotal}p (Δ {delta}p)";
                    note = string.IsNullOrEmpty(note) ? recon : $"{note} | {recon}";
                    wasReconciled = true;
                }

                var gross = declaredTotal;                    // authoritative
                var vat = lines.Sum(x => x.VatAmountPence);

                var tenders = input.Tenders.Select(t => new SaleTender
                {
                    Id = Uuid7.New(),
                    TenantId = input.TenantId,
                    SaleId = saleId,
                    TenderType = (TenderType)t.TenderType,
                    AmountPence = KapowMoney.ParsePence(t.AmountText),
                    ChangePence = string.IsNullOrWhiteSpace(t.ChangeText) ? 0 : KapowMoney.ParsePence(t.ChangeText),
                }).ToList();

                // SaleV2.Create enforces the T1.3 invariants (incl. tender net == gross); an
                // unreconcilable legacy sale surfaces here as a quarantine rather than a throw.
                var sale = SaleV2.Create(
                    saleId, input.TenantId, input.TillId, input.DeviceId, input.DeviceSeq,
                    SaleChannel.Till, businessDay, occurredAtUtc, DateTime.UtcNow,
                    gross, vat, lines, tenders,
                    legacyRef: input.LegacyId, operatorUserId: input.OperatorUserId,
                    note: note, vatReconstructed: true);

                return MapResult.Ok(sale, wasReconciled);
            }
            catch (InvalidSaleException ex)
            {
                return MapResult.Quarantine($"Sale {input.LegacyId}: {ex.Message}");
            }
            catch (FormatException ex)
            {
                return MapResult.Quarantine($"Sale {input.LegacyId}: parse failure — {ex.Message}");
            }
        }
    }
}
