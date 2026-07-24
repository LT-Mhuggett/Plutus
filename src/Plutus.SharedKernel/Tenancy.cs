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
