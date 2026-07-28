using System;

namespace Plutus.Entities.Models
{
    public enum AnnouncementSeverity : byte { Info = 0, Maintenance = 1, Incident = 2 }

    /// <summary>
    /// WP15.1 in-app announcement (GLOBAL table). Shown while StartsAtUtc ≤ now ≤ EndsAtUtc to the
    /// targeted tenants (TenantIds = JSON guid array, or null = every tenant). The portal + till
    /// poll GET /api/v1/announcements/active on their existing sync cadence — no push machinery.
    /// </summary>
    public class PlatformAnnouncement
    {
        public Guid Id { get; set; }
        public AnnouncementSeverity Severity { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public string TenantIds { get; set; }   // JSON array of guids, or null = all tenants
        public Guid CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
