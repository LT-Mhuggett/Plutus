using System;
using System.Collections.Generic;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Migration.Kapow
{
    /// <summary>Reconciliation summary (gap-analysis §5.7): counts + pence totals, source vs
    /// migrated, for sign-off before the old till is retired.</summary>
    public sealed class KapowReconciliation
    {
        public int SalesRead { get; set; }
        public int Recorded { get; set; }
        public int Quarantined { get; set; }
        public long SourceGrossPence { get; set; }   // Σ parsed Sales.Total (readable rows)
        public long RecordedGrossPence { get; set; }  // Σ SaleV2.GrossPence written
        public long RecordedVatPence { get; set; }
        public List<string> QuarantineReasons { get; } = new();

        // Total-mismatch delta buckets (diagnostics for the discount/rounding investigation).
        public int MismatchOther { get; set; }         // quarantined for a non-total reason
        public int MismatchWithin2p { get; set; }      // |Σlines − Total| ≤ 2p (rounding)
        public int Mismatch3to50p { get; set; }
        public int MismatchOver50p { get; set; }
        public int MismatchLinesHigh { get; set; }     // Σlines > Total (discount not captured)
        public int MismatchLinesLow { get; set; }      // Σlines < Total (missing value)

        public override string ToString() =>
            $"Kapow→v2 reconciliation:\n" +
            $"  sales read       : {SalesRead}\n" +
            $"  recorded         : {Recorded}\n" +
            $"  quarantined      : {Quarantined}\n" +
            $"  source gross     : £{SourceGrossPence / 100m:0.00}\n" +
            $"  recorded gross   : £{RecordedGrossPence / 100m:0.00}\n" +
            $"  recorded VAT     : £{RecordedVatPence / 100m:0.00}\n" +
            $"  gross recovered  : {(SourceGrossPence == 0 ? 0 : 100m * RecordedGrossPence / SourceGrossPence):0.0}%\n" +
            $"  quarantine delta : within2p={MismatchWithin2p} 3-50p={Mismatch3to50p} >50p={MismatchOver50p} other={MismatchOther}\n" +
            $"  quarantine dir   : lines>total={MismatchLinesHigh} lines<total={MismatchLinesLow}";
    }

    /// <summary>
    /// Orchestrates the Kapow→v2 sale migration (gap-analysis §5.4): map each sale, write a
    /// SaleV2 (+ lines/tenders) or a SaleQuarantine row, and produce a reconciliation report.
    /// The reusable core the SeedMigrator runner (and the MAUI cutover tooling) call.
    /// </summary>
    public static class KapowMigrator
    {
        public static KapowReconciliation Migrate(
            IReadOnlyList<KapowSaleInput> inputs, MySqlDbContext target, IdRemap<string> itemRemap,
            int saveEvery = 500, Action<string>? log = null)
        {
            target.CurrentUser = "kapow-migrator";
            var recon = new KapowReconciliation { SalesRead = inputs.Count };
            var pending = 0;

            foreach (var input in inputs)
            {
                if (KapowMoney.TryParsePence(input.TotalText, out var srcTotal)) recon.SourceGrossPence += srcTotal;

                var result = KapowSaleMapper.MapSale(input, itemRemap);
                if (result.IsQuarantined)
                {
                    recon.Quarantined++;
                    if (result.TotalDeltaPence is long delta)
                    {
                        var abs = Math.Abs(delta);
                        if (abs <= 2) recon.MismatchWithin2p++;
                        else if (abs <= 50) recon.Mismatch3to50p++;
                        else recon.MismatchOver50p++;
                        if (delta > 0) recon.MismatchLinesHigh++; else recon.MismatchLinesLow++;
                    }
                    else recon.MismatchOther++;
                    if (recon.QuarantineReasons.Count < 50) recon.QuarantineReasons.Add(result.QuarantineReason!);
                    target.SaleQuarantine.Add(new SaleQuarantine
                    {
                        Id = Uuid7.New(), TenantId = input.TenantId, SaleId = Uuid7.New(),
                        PayloadJson = input.LegacyId, Reason = Trunc(result.QuarantineReason, 500),
                        ReceivedAtUtc = DateTime.UtcNow,
                    });
                }
                else
                {
                    var s = result.Sale!;
                    recon.Recorded++;
                    recon.RecordedGrossPence += s.GrossPence;
                    recon.RecordedVatPence += s.VatPence;
                    target.SalesV2.Add(s);
                }

                if (++pending >= saveEvery)
                {
                    target.SaveChanges();
                    target.ChangeTracker.Clear();
                    pending = 0;
                    log?.Invoke($"  … {recon.Recorded + recon.Quarantined}/{recon.SalesRead}");
                }
            }
            if (pending > 0) target.SaveChanges();
            return recon;
        }

        private static string Trunc(string? s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));
    }
}
