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
        public static MapResult Ok(SaleV2 s) => new() { Sale = s };
        public static MapResult Quarantine(string reason) => new() { QuarantineReason = reason };
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
                    var disc = string.IsNullOrWhiteSpace(l.DiscountText) ? 0 : KapowMoney.ParsePence(l.DiscountText);
                    var lineGross = unit * l.Qty - disc;
                    lines.Add(new SaleLine
                    {
                        Id = Uuid7.New(),
                        TenantId = input.TenantId,
                        SaleId = saleId,
                        LineNo = lineNo,
                        ItemId = itemRemap.GetOrMint(l.OldItemId),
                        Qty = l.Qty,
                        UnitPricePence = unit,
                        DiscountPence = disc,
                        LineGrossPence = lineGross,
                        VatRateBp = KapowVat.ToBasisPoints(l.VatMultiplier),
                        VatAmountPence = KapowVat.VatFromInclusive(lineGross, l.VatMultiplier),
                        OverriddenFromPence = l.OverriddenFromPence,
                    });
                }

                var gross = lines.Sum(x => x.LineGrossPence);
                if (gross != declaredTotal)
                    return MapResult.Quarantine(
                        $"Sale {input.LegacyId}: Σ line gross {gross}p != Sales.Total {declaredTotal}p.");

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
                    note: input.Note, vatReconstructed: true);

                return MapResult.Ok(sale);
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
