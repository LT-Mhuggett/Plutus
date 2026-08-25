using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP1.2: tenant/device identity is resolved from the principal's claims only, with a
/// null-safe Kapow fallback and unscoped platform-admin.</summary>
public class HttpTenantContextTests
{
    private static HttpTenantContext ContextWith(params Claim[] claims)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test")),
        };
        return new HttpTenantContext(new HttpContextAccessor { HttpContext = http });
    }

    [Fact]
    public void No_claims_falls_back_to_Kapow_single_tenant()
    {
        var ctx = new HttpTenantContext(new HttpContextAccessor { HttpContext = null });
        Assert.Equal(WellKnownTenants.Kapow, ctx.TenantId);
        Assert.Null(ctx.DeviceId);
        Assert.False(ctx.IsPlatformAdmin);
    }

    [Fact]
    public void Tid_and_did_claims_are_honoured()
    {
        var tid = Guid.NewGuid();
        var did = Guid.NewGuid();
        var ctx = ContextWith(new Claim("tid", tid.ToString()), new Claim("did", did.ToString()));
        Assert.Equal(tid, ctx.TenantId);
        Assert.Equal(did, ctx.DeviceId);
        Assert.False(ctx.IsPlatformAdmin);
    }

    /// <summary>
    /// ⚠ A platform-admin with NO tenant claim is unscoped, and must stay so: provisioning writes
    /// across tenants and the operator console lists all of them.
    /// </summary>
    [Fact]
    public void Platform_admin_scope_is_unscoped()
    {
        var ctx = ContextWith(new Claim("scope", "platform-admin"));
        Assert.True(ctx.IsPlatformAdmin);
        Assert.Equal(Guid.Empty, ctx.TenantId); // sees all tenants via the query filter's Guid.Empty branch
    }

    /// <summary>
    /// ⚠⚠ THIS TEST USED TO BE THE ONE ABOVE, WITH A `tid` PASSED IN AND `Guid.Empty` ASSERTED — so
    /// it pinned the 2026-08-25 cross-tenant leak in a SECOND place, under a name that sounded
    /// right. Matt: *"I logged into the 'New store' and I could see Kapow data, e.g. number of
    /// tills."* Impersonation stamps a `tid` and `OperatorBoundaryMiddleware` steps aside on the
    /// strength of it; if this property then answers `Guid.Empty`, the global query filter shows
    /// **every tenant** to an operator who asked to see one shop.
    /// </summary>
    [Fact]
    public void Platform_admin_IMPERSONATING_is_scoped_to_that_tenant()
    {
        var shop = Guid.NewGuid();
        var ctx = ContextWith(new Claim("scope", "platform-admin"), new Claim("tid", shop.ToString()));
        Assert.True(ctx.IsPlatformAdmin);         // still an operator…
        Assert.Equal(shop, ctx.TenantId);         // …but confined to the shop they are acting as
        Assert.NotEqual(Guid.Empty, ctx.TenantId); // the property that was violated
    }

    [Fact]
    public void Garbage_tid_falls_back_to_Kapow()
    {
        var ctx = ContextWith(new Claim("tid", "not-a-guid"));
        Assert.Equal(WellKnownTenants.Kapow, ctx.TenantId);
    }
}
