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

    [Fact]
    public void Platform_admin_scope_is_unscoped()
    {
        var ctx = ContextWith(new Claim("scope", "platform-admin"), new Claim("tid", Guid.NewGuid().ToString()));
        Assert.True(ctx.IsPlatformAdmin);
        Assert.Equal(Guid.Empty, ctx.TenantId); // sees all tenants via the query filter's Guid.Empty branch
    }

    [Fact]
    public void Garbage_tid_falls_back_to_Kapow()
    {
        var ctx = ContextWith(new Claim("tid", "not-a-guid"));
        Assert.Equal(WellKnownTenants.Kapow, ctx.TenantId);
    }
}
