using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// WP13.2 per-tenant request health: one row per (TenantId, MinuteUtc, RouteGroup), flushed
    /// once a minute from the in-memory accumulator the request-health middleware feeds. Latency
    /// percentiles are derived at flush time from a fixed-bucket histogram (no dependency). This
    /// is the noisy-neighbour / per-tenant error-rate surface the operator dashboard reads.
    /// Tenant-owned, so platform-admin (Guid.Empty context) reads across every tenant.
    /// </summary>
    public class TenantRequestStats
    {
        public Guid TenantId { get; set; }
        public DateTime MinuteUtc { get; set; }   // truncated to the minute
        public string RouteGroup { get; set; }
        public long Count { get; set; }
        public long Err4xx { get; set; }
        public long Err5xx { get; set; }
        public int P50Ms { get; set; }
        public int P95Ms { get; set; }
        public int MaxMs { get; set; }
    }
}
