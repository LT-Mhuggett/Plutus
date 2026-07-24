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
        public int Reconciled { get; set; }           // recorded, but Sales.Total trusted (Δ line added)
        public int Quarantined { get; set; }
        public long SourceGrossPence { get; set; }    // Σ parsed Sales.Total (readable rows)
        public long RecordedGrossPence { get; set; }  // Σ SaleV2.GrossPence written
        public long RecordedVatPence { get; set; }
        public List<string> QuarantineReasons { get; } = new();

        public override string ToString() =>
            $"Kapow→v2 reconciliation:\n" +
            $"  sales read       : {SalesRead}\n" +
            $"  recorded         : {Recorded}  (of which reconciled-to-Total: {Reconciled})\n" +
            $"  quarantined      : {Quarantined}\n" +
            $"  source gross     : £{SourceGrossPence / 100m:0.00}\n" +
            $"  recorded gross   : £{RecordedGrossPence / 100m:0.00}\n" +
            $"  recorded VAT     : £{RecordedVatPence / 100m:0.00}\n" +
            $"  gross recovered  : {(SourceGrossPence == 0 ? 0 : 100m * RecordedGrossPence / SourceGrossPence):0.0}%";
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
                    if (result.WasReconciled) recon.Reconciled++;
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
