using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// **A session must be scoped to its OWN tenant, and the fallback is what made that untrue.**
///
/// ⚠⚠ THIS IS THE TEST THAT WOULD HAVE CAUGHT THE 403, AND IT DID NOT EXIST. The first person to
/// sign into a tenant that was not Kapow got 403 on every tab (Matt, 2026-08-24). The login token
/// carried `EmployeeId`, `Name` and `Scope` and **no `Tid`**; `HttpTenantContext` finds no `tid`
/// claim and falls back to Kapow — deliberately, "so the platform behaves as single-tenant until
/// real tenants are provisioned". Real tenants are now provisioned, so every query in that session
/// was scoped to somebody else's shop, the user's own assignments were invisible, and every `perm:`
/// gate refused.
///
/// ⚠ NOTHING CAUGHT IT BECAUSE THERE WAS ONLY EVER ONE TENANT. Every existing test, and the whole
/// of production, ran as Kapow — where the fallback happens to be right. A default that is correct
/// for the only case you have is indistinguishable from a working feature.
///
/// ⚠ It failed CLOSED, which is the only reason it was a support ticket and not an incident.
/// </summary>
public class LoginTenantClaimTests
{
    private static HttpTenantContext ContextFor(params Claim[] claims)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "PlutusToken")),
        };
        return new HttpTenantContext(new HttpContextAccessor { HttpContext = http });
    }

    /// <summary>⚠ The behaviour that bit: no `tid` means Kapow, whoever you are.</summary>
    [Fact]
    public void A_session_with_no_tenant_claim_still_falls_back_to_the_default_tenant()
    {
        var ctx = ContextFor(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        Assert.Equal(WellKnownTenants.Kapow, ctx.TenantId);
        // ⚠ Pinned as CURRENT BEHAVIOUR, not as desirable behaviour. The fallback is why a second
        // tenant's user was scoped to the first tenant's data. It is left in place because tills and
        // older sessions still rely on it, and removing it is a separate, riskier change — but the
        // fix is to stop RELYING on it, which the two tests below are about.
    }

    /// <summary>
    /// ⚠⚠ THE FIX: a token that carries its tenant is scoped to that tenant. Both login paths now
    /// stamp it — `AuthController` (password) and `RbacClaimsTransformation` (OIDC), which had the
    /// identical gap and would have reproduced the same 403 for a Keycloak user.
    /// </summary>
    [Fact]
    public void A_session_carrying_its_tenant_is_scoped_to_that_tenant()
    {
        var mine = Guid.NewGuid();
        var ctx = ContextFor(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tid", mine.ToString()));

        Assert.Equal(mine, ctx.TenantId);
        Assert.NotEqual(WellKnownTenants.Kapow, ctx.TenantId);
    }

    /// <summary>
    /// ⚠⚠ AND THE ONE THAT MATTERS COMMERCIALLY: a second tenant's session must not resolve to the
    /// first tenant. Stated as its own test because "not Kapow" is the property that was violated,
    /// and the next multi-tenant regression will violate exactly this.
    /// </summary>
    [Fact]
    public void A_second_tenants_session_is_never_scoped_to_the_first()
    {
        var testBusiness = Guid.Parse("01a0343b-e88d-7c00-ba8b-ee0e231c6616"); // the real one, 2026-08-24
        var ctx = ContextFor(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tid", testBusiness.ToString()));

        Assert.Equal(testBusiness, ctx.TenantId);
        Assert.NotEqual(WellKnownTenants.Kapow, ctx.TenantId);
    }

    /// <summary>
    /// ⚠ Platform-admin still runs UNSCOPED, and must: provisioning has to see across tenants, and
    /// the operator console lists every one of them. ⚠ A `tid` alongside platform-admin is the
    /// IMPERSONATION case and it must win, or an operator acting as a shop would read all shops.
    /// </summary>
    [Fact]
    public void Platform_admin_runs_unscoped_unless_it_is_impersonating()
    {
        var plain = ContextFor(new Claim("scope", PlutusPolicies.PlatformAdmin));
        Assert.True(plain.IsPlatformAdmin);
        Assert.Equal(Guid.Empty, plain.TenantId);

        // ⚠ CURRENT BEHAVIOUR, pinned so a change is deliberate: platform-admin wins over tid, so an
        // impersonation token is unscoped here and the boundary middleware is what confines it.
        var impersonating = ContextFor(
            new Claim("scope", PlutusPolicies.PlatformAdmin),
            new Claim("tid", Guid.NewGuid().ToString()));
        Assert.Equal(Guid.Empty, impersonating.TenantId);
    }

    /// <summary>⚠ A malformed `tid` must not be read as "no tenant, use the default" silently —
    /// it does today, and that is worth knowing rather than discovering.</summary>
    [Fact]
    public void A_malformed_tenant_claim_falls_back_rather_than_throwing()
    {
        var ctx = ContextFor(new Claim("tid", "not-a-guid"));
        Assert.Equal(WellKnownTenants.Kapow, ctx.TenantId);
    }
}
