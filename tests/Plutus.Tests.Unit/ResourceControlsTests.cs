using System;
using System.Threading;
using System.Threading.Tasks;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP13.5 resource controls: valued-entitlement parsing and the provisioning quota guard — the
/// (limit+1)th create is refused, an absent limit is unlimited, platform-admin is unlimited.
/// </summary>
public class ResourceControlsTests
{
    [Theory]
    [InlineData("ratelimit.rps", 50)]
    [InlineData("stores.max", 5)]
    public void ParseLimit_reads_key_value_entitlements(string key, long expected)
    {
        var ents = new[] { "woo-connector", "ratelimit.rps:50", "stores.max:5", "malformed", "users.max:x" };
        Assert.Equal(expected, Entitlements.ParseLimit(ents, key));
    }

    [Theory]
    [InlineData("users.max")]   // malformed value "x"
    [InlineData("tills.max")]   // absent
    public void ParseLimit_returns_null_when_absent_or_malformed(string key)
    {
        var ents = new[] { "ratelimit.rps:50", "users.max:x" };
        Assert.Null(Entitlements.ParseLimit(ents, key));
    }

    private sealed class FakeEntitlements : IEntitlementService
    {
        private readonly long? _limit;
        public FakeEntitlements(long? limit) => _limit = limit;
        public Task<bool> IsEnabledAsync(Guid t, string f, CancellationToken ct = default) => Task.FromResult(true);
        public Task<long?> GetLimitAsync(Guid t, string k, CancellationToken ct = default) => Task.FromResult(_limit);
    }

    [Fact]
    public async Task Quota_guard_refuses_at_or_over_limit_and_allows_below()
    {
        var guard = new QuotaGuard(new FakeEntitlements(3)); // stores.max = 3
        var tenant = Guid.NewGuid();

        await guard.EnforceAsync(tenant, Entitlements.StoresMax, 0); // ok
        await guard.EnforceAsync(tenant, Entitlements.StoresMax, 2); // ok (creating the 3rd)

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(
            () => guard.EnforceAsync(tenant, Entitlements.StoresMax, 3)); // the 4th is refused
        Assert.Equal(Entitlements.StoresMax, ex.LimitKey);
        Assert.Equal(3, ex.Limit);
    }

    [Fact]
    public async Task Quota_guard_is_unlimited_when_entitlement_absent()
    {
        var guard = new QuotaGuard(new FakeEntitlements(null)); // no limit set
        await guard.EnforceAsync(Guid.NewGuid(), Entitlements.StoresMax, 9999); // never throws
    }
}
