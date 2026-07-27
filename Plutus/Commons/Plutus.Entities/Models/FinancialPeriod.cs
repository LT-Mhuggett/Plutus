using System;

namespace Plutus.Entities.Models
{
    public enum PeriodStatus : byte { Open = 0, Closed = 1 }

    /// <summary>
    /// WP3.4 (architecture §7.1): a reporting period with an explicit CLOSE — closing
    /// snapshots the rollup totals (SnapshotJson) and locks the day range: projection
    /// (consumer AND rebuild) redirects any late-arriving sale whose BusinessDay falls in a
    /// closed period to the first day after it (the next open period), flagged in the audit
    /// trail — so a published year never silently changes. Server-only, tenant-owned.
    /// </summary>
    public class FinancialPeriod
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid CompanyId { get; set; }
        public string Name { get; set; }           // e.g. "FY 2025/26"
        public DateOnly StartDay { get; set; }
        public DateOnly EndDay { get; set; }
        public PeriodStatus Status { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
        public Guid? ClosedBy { get; set; }
        /// <summary>Totals frozen at close: {grossPence, vatPence, txnCount, vatByRate:[…]}.</summary>
        public string? SnapshotJson { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
