using System;

namespace Plutus.Entities.Models
{
    // WP3.2 admin-surface entities. Server-only (MySqlDbContext), tenant-owned.

    /// <summary>Append-only audit trail for admin mutations (WP3.2 DoD: "all actions
    /// audit-logged"). One row per mutation, written in the SAME SaveChanges as the change
    /// itself so the log can never disagree with the data.</summary>
    public class AuditLog
    {
        public long Id { get; set; }               // AUTO_INCREMENT — natural time order
        public Guid TenantId { get; set; }
        public Guid ActorUserId { get; set; }
        /// <summary>Verb, dot-namespaced: "store.update", "user.create", "role.assign"…</summary>
        public string Action { get; set; }
        public string EntityType { get; set; }
        public string EntityId { get; set; }
        /// <summary>What changed, as JSON (request payload or field diff) — for the drill-down.</summary>
        public string? DetailJson { get; set; }
        public DateTime AtUtc { get; set; }
    }

    /// <summary>Server-side extras for a Store that must NOT touch the shared legacy POCO
    /// (the MAUI SqliteDbContext maps Store — adding columns there breaks bi-modality).
    /// Holds the WP3.2 opening hours; more portal-only store fields land here later.</summary>
    public class StoreDetails
    {
        public int StoreId { get; set; }           // PK, 1:1 with legacy Store
        public Guid TenantId { get; set; }
        /// <summary>Human name for the store (the legacy Store POCO — shared with MAUI — has none).
        /// Unique per tenant (checked; case-insensitive).</summary>
        public string? Name { get; set; }
        /// <summary>Opening hours as JSON: {"mon":[{"open":"09:00","close":"17:30"}],…} —
        /// shape owned by the portal; the API stores/echoes it opaquely.</summary>
        public string? OpeningHoursJson { get; set; }
        /// <summary>WP11.2 receipt template as JSON: {headerLines,footerLines,showVatNumber,
        /// showOperator,showBarcode} — shape owned by the frontends; API stores/echoes opaquely.
        /// Cached by the till with its catalogue sync and applied to printed receipts.</summary>
        public string? ReceiptTemplateJson { get; set; }
    }

    /// <summary>WP11.1: a human name for a till. The legacy <c>Till</c> POCO has NO Name column
    /// (and is shared with the MAUI SqliteDbContext, so it must not gain one) — the name typed at
    /// creation was previously dropped. This server-only side table holds it, 1:1 with the legacy
    /// Till, unique per tenant (case-insensitive via the column collation + a unique index).</summary>
    public class TillDetails
    {
        public Guid TillId { get; set; }           // PK, 1:1 with legacy Till
        public Guid TenantId { get; set; }
        public string Name { get; set; }
    }
}
