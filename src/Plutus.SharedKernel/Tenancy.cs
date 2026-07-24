namespace Plutus.SharedKernel;

/// <summary>Every tenant-owned entity carries its TenantId (D2). A single EF global query
/// filter is applied by convention in the owning DbContext — never per-entity by hand.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

/// <summary>
/// The current request's tenant/device identity, resolved from the JWT (`tid`, `did`) —
/// never from a request body or URL (D2). Platform-admin calls (provisioning) run without
/// a tenant.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    Guid? DeviceId { get; }
    bool IsPlatformAdmin { get; }
}

/// <summary>Well-known tenant IDs seeded during the evolve-in-place phase.</summary>
public static class WellKnownTenants
{
    /// <summary>The founding Kapow Comics tenant — all pre-platform data was backfilled to it
    /// (migration AddTenantIdToTenantOwned). Stable UUIDv7; the null-safe default when a request
    /// carries no <c>tid</c> claim, so the system behaves as single-tenant until real tenants
    /// are provisioned.</summary>
    public static readonly Guid Kapow = new Guid("0192b8a0-1a6f-7000-8000-000000000001");
}
