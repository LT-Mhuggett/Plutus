using System;

namespace Plutus.Entities.Models
{
    // WP3.3 reporting projections (architecture §7.1): pre-aggregated rollups the dashboards
    // read, folded from SaleRecorded events (incremental) and rebuildable by replaying the
    // SalesV2 table. Server-only, tenant-owned. Grain:
    //  - SalesRollup: per TILL per business day (store/company are denormalised so any level
    //    of the spine is a SUM over rows — shop-scale row counts make that instant).
    //  - VatRollup: per STORE per business day per VAT rate AND BAND (feeds the UK VAT return
    //    boxes). ⚠ The band is part of the grain, not decoration — see VatRollup.VatBand.
    // avgBasketPence is computed at query time (Gross/TxnCount), never stored.

    public class SalesRollup
    {
        public long Id { get; set; }               // AUTO_INCREMENT
        public Guid TenantId { get; set; }
        public Guid CompanyId { get; set; }
        public int StoreId { get; set; }
        public Guid TillId { get; set; }
        public DateOnly BusinessDay { get; set; }
        public long GrossPence { get; set; }
        public long VatPence { get; set; }
        public int TxnCount { get; set; }
    }

    public class VatRollup
    {
        public long Id { get; set; }               // AUTO_INCREMENT
        public Guid TenantId { get; set; }
        public Guid CompanyId { get; set; }
        public int StoreId { get; set; }
        public DateOnly BusinessDay { get; set; }
        public int VatRateBp { get; set; }
        /// <summary>
        /// WP2c-exempt: the BAND these takings were rung up under. Part of the rollup's grain.
        ///
        /// ⚠ THIS EXISTS BECAUSE TWO BANDS CAN SHARE A RATE. Zero-rated and exempt supplies are
        /// both 0%, so a rollup keyed on the rate alone MERGES them — and once merged, the
        /// partial-exemption figure is unrecoverable without replaying every sale line. Keying on
        /// the band keeps them apart at the grain where the VAT return reads them.
        ///
        /// Null for rows projected before this shipped, and for lines whose till didn't send a
        /// band. Reports fall back to snapping the rate.
        /// </summary>
        public string? VatBand { get; set; }
        public long GrossPence { get; set; }       // Σ line gross at this rate
        public long NetPence { get; set; }         // Σ (line gross − line VAT)
        public long VatPence { get; set; }         // Σ line VAT
    }
}
