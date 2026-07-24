using System;
using Plutus.SharedKernel;

namespace Plutus.Entities.Tenancy
{
    /// <summary>
    /// Well-known tenant IDs for the evolve-in-place phase. Kapow is the single founding
    /// tenant that all pre-platform data was backfilled to (migration AddTenants).
    /// </summary>
    public static class KnownTenants
    {
        /// <summary>The founding Kapow Comics tenant (backfilled into Tenants + every
        /// tenant-owned row). Stable UUIDv7 — matches the seeded row and is the null-safe
        /// default when no <c>tid</c> claim is present.</summary>
        public static readonly Guid Kapow = new Guid("0192b8a0-1a6f-7000-8000-000000000001");
    }

    /// <summary>
    /// A concrete <see cref="ITenantContext"/> with values supplied directly — used for the
    /// null-safe default (Kapow), for tests (two-tenant isolation), and for tools/design-time
    /// where there is no HTTP request/JWT. The request-scoped, JWT-backed implementation that
    /// reads <c>tid</c>/<c>did</c> lives in the platform host and also implements this interface.
    /// </summary>
    public sealed class FixedTenantContext : ITenantContext
    {
        public FixedTenantContext(Guid tenantId, Guid? deviceId = null, bool isPlatformAdmin = false)
        {
            TenantId = tenantId;
            DeviceId = deviceId;
            IsPlatformAdmin = isPlatformAdmin;
        }

        public Guid TenantId { get; }
        public Guid? DeviceId { get; }
        public bool IsPlatformAdmin { get; }

        /// <summary>The default ambient context when nothing else is supplied: the Kapow tenant.
        /// Keeps every non-DI call site (tests, SeedMigrator, design-time factory, legacy code)
        /// working as single-tenant Kapow.</summary>
        public static readonly FixedTenantContext KapowDefault = new FixedTenantContext(KnownTenants.Kapow);
    }
}
