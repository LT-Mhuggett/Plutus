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

    /// <summary>WP13.5: the numeric value of a "key:value" entitlement (e.g. "ratelimit.rps:50",
    /// "stores.max:5"), or null when absent — null means "unlimited / use the default". Platform-admin
    /// (Guid.Empty) → null (unlimited).</summary>
    Task<long?> GetLimitAsync(System.Guid tenantId, string key, System.Threading.CancellationToken ct = default);
}

/// <summary>Well-known entitlement keys (kept together so billing and features agree). Boolean
/// feature flags plus WP13.5 "key:value" valued limits.</summary>
public static class Entitlements
{
    public const string WooConnector = "woo-connector";
    public const string MultiStore = "multi-store";
    public const string AdvancedReports = "advanced-reports";

    // WP13.5 valued limits (stored as "key:value" strings in the entitlements array).
    public const string RateLimitRps = "ratelimit.rps";
    public const string StoresMax = "stores.max";
    public const string UsersMax = "users.max";
    public const string TillsMax = "tills.max";

    /// <summary>Extract the numeric value of a "key:value" entitlement, or null if not present.</summary>
    public static long? ParseLimit(System.Collections.Generic.IEnumerable<string> entitlements, string key)
    {
        if (entitlements == null) return null;
        foreach (var e in entitlements)
        {
            if (string.IsNullOrEmpty(e)) continue;
            var i = e.IndexOf(':');
            if (i > 0 &&
                e.Substring(0, i).Trim().Equals(key, System.StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(e.Substring(i + 1).Trim(), out var v))
                return v;
        }
        return null;
    }
}

/// <summary>WP13.5 provisioning-quota seam: enforce a per-tenant creation limit (stores/users/
/// tills) sourced from a valued entitlement. Interface in SharedKernel so provisioning code
/// depends only on this; the implementation reads entitlements. Over-limit throws
/// <see cref="QuotaExceededException"/> which controllers map to 409.</summary>
public interface IQuotaGuard
{
    /// <summary>Throws if <paramref name="currentCount"/> is already at/over the tenant's limit for
    /// <paramref name="limitKey"/>. No-op when the entitlement is absent (unlimited) or platform-admin.</summary>
    Task EnforceAsync(System.Guid tenantId, string limitKey, long currentCount, System.Threading.CancellationToken ct = default);
}

public sealed class QuotaExceededException : System.Exception
{
    public string LimitKey { get; }
    public long Limit { get; }
    public QuotaExceededException(string limitKey, long limit)
        : base($"Quota for '{limitKey}' reached ({limit}). Upgrade the plan or raise the limit to add more.")
    { LimitKey = limitKey; Limit = limit; }
}
