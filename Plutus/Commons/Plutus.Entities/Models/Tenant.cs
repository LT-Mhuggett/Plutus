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
    }
}
