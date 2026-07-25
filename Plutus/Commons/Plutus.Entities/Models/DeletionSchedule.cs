using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// WP10.4: a scheduled tenant deletion (offboarding). Global/platform record (carries TenantId
    /// as data, NOT tenant-owned) so platform-admin manages it across tenants. The retention
    /// sweeper hard-deletes the tenant's rows once the grace window (ExecuteAfterUtc) elapses;
    /// a Pending schedule can be cancelled before then.
    /// </summary>
    public class DeletionSchedule
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public DateTime RequestedAtUtc { get; set; }
        /// <summary>Grace window end — the sweeper won't execute before this.</summary>
        public DateTime ExecuteAfterUtc { get; set; }
        public byte Status { get; set; }          // 0 Pending, 1 Cancelled, 2 Executed
        public Guid RequestedBy { get; set; }
        public DateTime? ExecutedAtUtc { get; set; }
    }
}
