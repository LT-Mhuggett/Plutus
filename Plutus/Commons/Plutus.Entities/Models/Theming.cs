using System;

namespace Plutus.Entities.Models
{
    // FE10 till theming: colour schemes defined in the portal and pushed to tills — per tenant,
    // per store, per till, or per named group of tills. Server-only (MySqlDbContext), tenant-owned.
    // The COLOURS are an opaque JSON blob (shape owned by the frontends, like ReceiptTemplateJson);
    // the SCOPING is real columns, because precedence is resolved server-side.

    /// <summary>A named till colour scheme. The two built-ins ("Plutus Light" / "Plutus Dark")
    /// are code-defined in the clients and never stored — only custom schemes get a row.</summary>
    public class TillTheme
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>Unique per tenant (case-insensitive via collation + app-side guard).</summary>
        public string Name { get; set; }
        /// <summary>"light" | "dark" — which base the colour overrides sit on. A custom theme
        /// always forces a mode; only the built-in default follows the device setting.</summary>
        public string BaseMode { get; set; }
        /// <summary>Colour slots as JSON {accent, accentInk, surface, surface2, ink, inkMuted,
        /// line} — all optional hex values; missing slots keep the base mode's defaults. Shape
        /// owned by the frontends; the API stores/echoes it opaquely (cap: 2 KB). Null = no
        /// overrides — the theme is just a named forced mode.</summary>
        public string? ColorsJson { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>A named set of tills, for assigning one theme to many tills that aren't a whole
    /// store (e.g. "Counter tills" vs "Back office"). Membership in TillGroupMembers.</summary>
    public class TillGroup
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>Unique per tenant (case-insensitive via collation + app-side guard).</summary>
        public string Name { get; set; }
    }

    /// <summary>Group membership, composite-keyed — a till is in a group at most once.
    /// A till MAY be in several groups; the newest group assignment wins (ThemeResolution).</summary>
    public class TillGroupMember
    {
        public Guid GroupId { get; set; }
        public Guid TillId { get; set; }
        public Guid TenantId { get; set; }
    }

    /// <summary>Which theme a target uses. One row per target (unique on Scope+ScopeKey within
    /// the tenant) — assigning replaces, un-assigning deletes. Precedence at resolve time:
    /// Till > Group (latest UpdatedAtUtc when a till is in several themed groups) > Store >
    /// Tenant > built-in default.</summary>
    public class TillThemeAssignment
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>0 Tenant · 1 Store · 2 Group · 3 Till (ThemeResolution constants).</summary>
        public byte Scope { get; set; }
        /// <summary>"" for tenant scope; the store's int id; the group/till guid ("D", lower).</summary>
        public string ScopeKey { get; set; }
        /// <summary>"builtin:system" | "builtin:light" | "builtin:dark" | a TillTheme Id ("D").
        /// builtin:system is assignable on purpose: it lets one till follow the device setting
        /// while its store or tenant default forces something else.</summary>
        public string ThemeKey { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
