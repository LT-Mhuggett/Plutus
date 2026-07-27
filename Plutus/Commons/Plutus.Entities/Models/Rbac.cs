using System;
using System.Collections.Generic;

namespace Plutus.Entities.Models
{
    // WP3.1 RBAC (architecture §7.2): Role = named permission bundle; RoleAssignment =
    // User × Role × Scope (+ optional time window). Server-only tables on MySqlDbContext,
    // tenant-owned (query-filtered). Tables are prefixed "Rbac" because the LEGACY `Role`
    // table still exists during evolve-in-place — renamed when the legacy tables drop.
    // The permission catalogue itself is code-defined (SharedKernel.PermissionCatalogue);
    // grants store catalogue codes plus an optional pence ceiling (pos.refund.max:{pence}).

    /// <summary>Scope node kinds, matching the reporting spine Tenant → Company → Store → Till.
    /// Company is the legacy Business during evolve-in-place.</summary>
    public enum RbacScopeType : byte { Tenant = 0, Company = 1, Store = 2, Till = 3 }

    public class RbacRole
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; }
        /// <summary>Built-ins (Owner, Company Admin, Store Manager, Supervisor, Cashier,
        /// Auditor) are seeded per tenant and not tenant-editable.</summary>
        public bool IsBuiltIn { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public List<RbacRoleGrant> Grants { get; set; } = new();
    }

    public class RbacRoleGrant
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid RoleId { get; set; }
        /// <summary>A SharedKernel.PermissionCatalogue code — validated at write time.</summary>
        public string PermissionCode { get; set; }
        /// <summary>Pence ceiling for ceiling-capable permissions (pos.refund, pos.discount);
        /// null = unlimited / not amount-based.</summary>
        public long? MaxPence { get; set; }
    }

    public class RbacRoleAssignment
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>The user (evolve-in-place: the Employee/People id).</summary>
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }

        public RbacScopeType ScopeType { get; set; }
        /// <summary>The scope node id as a string ("" for Tenant scope; Business Guid for
        /// Company; Store int for Store; Till Guid for Till) — one column fits the mixed
        /// legacy key types.</summary>
        public string ScopeId { get; set; }

        // Optional time window (architecture §7.2: e.g. Sat staff 09:00–17:30), evaluated in
        // the store's local wall-clock at token issue and by the till locally.
        /// <summary>Bit mask of allowed days, bit 0 = Sunday … bit 6 = Saturday; null = any day.</summary>
        public byte? DaysOfWeekMask { get; set; }
        public TimeOnly? WindowStartLocal { get; set; }
        public TimeOnly? WindowEndLocal { get; set; }

        // Optional validity bounds (contract dates), UTC.
        public DateTime? ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public RbacRole Role { get; set; }
    }
}
