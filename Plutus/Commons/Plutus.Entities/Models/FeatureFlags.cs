using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// WP14.2 operator-set entitlement override (GLOBAL table — TenantId is data). A grant
    /// (Deny=false) turns a feature on for one tenant regardless of plan (beta access) or supplies
    /// a valued limit ("ratelimit.rps:100"); a deny (Deny=true) turns it off (temporary disable),
    /// and deny wins over a plan grant. Effective = plan ∪ grants − denies.
    /// </summary>
    public class TenantEntitlementOverride
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Entitlement { get; set; }  // "woo-connector" | "ratelimit.rps:100"
        public bool Deny { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>
    /// WP14.2 global kill switch (GLOBAL table). If a row exists for a feature with Enabled=false,
    /// that feature is off for EVERY tenant instantly — checked before plan/overrides. One switch
    /// to disable e.g. "woo-outbound" platform-wide, no restart, no token reissue.
    /// </summary>
    public class PlatformFlag
    {
        public string FlagName { get; set; }   // the feature key it governs (PK)
        public bool Enabled { get; set; }       // false = killed globally
        public string Reason { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
