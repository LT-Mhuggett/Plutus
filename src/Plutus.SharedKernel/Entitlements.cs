namespace Plutus.SharedKernel;

/// <summary>
/// Phase 10 (WP10.1): per-tenant feature entitlements — the gate a module checks at its boundary
/// before doing entitled work (e.g. the Woo connector checks <c>woo-connector</c>). The interface
/// lives in SharedKernel so any feature module can depend on it without a module→module reference;
/// the implementation (reading Tenant.Entitlements JSON) lives in Plutus.Tenancy. Billing writes
/// the entitlements; features only read them.
/// </summary>
public interface IEntitlementService
{
    /// <summary>Is <paramref name="feature"/> entitled for the tenant right now? Platform-admin
    /// (Guid.Empty) is always entitled. Unknown tenant / absent feature → false.</summary>
    Task<bool> IsEnabledAsync(System.Guid tenantId, string feature, System.Threading.CancellationToken ct = default);
}

/// <summary>Well-known entitlement feature keys (kept together so billing and features agree).</summary>
public static class Entitlements
{
    public const string WooConnector = "woo-connector";
    public const string MultiStore = "multi-store";
    public const string AdvancedReports = "advanced-reports";
}
