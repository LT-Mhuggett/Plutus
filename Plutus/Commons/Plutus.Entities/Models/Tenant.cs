using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// Platform tenant (architecture §3). Sits above the existing Business/Store/Till
    /// hierarchy (evolve-in-place decision, 2026-07-24): a Tenant owns one or more
    /// Businesses (Business plays the Company role). Server-side only — mapped on
    /// MySqlDbContext, never on the MAUI SqliteDbContext.
    /// </summary>
    public class Tenant
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public byte Status { get; set; }        // 0 Trial, 1 Active, 2 PastDue, 3 Suspended, 4 Closed
        public string Plan { get; set; }
        public string Entitlements { get; set; } // JSON (e.g. ["woo-connector"])
        public string ConnectionRef { get; set; } // null = pooled DB; set = dedicated (escape hatch)
        public DateTime CreatedAtUtc { get; set; }
        public bool IsSandbox { get; set; }        // WP14.3: demo/sandbox tenant (resettable, excluded from commercial rollups)
        // WP18.2 residency & DPA registry — compliance state made visible (portability = WP10.3
        // export; retention deletion = WP10.4 sweeper). DataRegion is constant "UK" today but
        // modelled now; a null DpaSignedAtUtc raises the WP16.1-style "dpa-missing" signal.
        public string DataRegion { get; set; } = "UK";
        public DateTime? DpaSignedAtUtc { get; set; }
        public string DpaRef { get; set; }
    }
}
