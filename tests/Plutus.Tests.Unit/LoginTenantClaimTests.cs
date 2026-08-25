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
    }

    /// <summary>
    /// ⚠⚠ THE CROSS-TENANT LEAK, 2026-08-25. Matt: *"I logged into the 'New store' and I could see
    /// Kapow data, e.g. number of tills."*
    ///
    /// ⚠⚠ AND THIS TEST USED TO ASSERT THE BUG. Its name said *"unless it is impersonating"* while
    /// its body asserted `Guid.Empty` for the impersonating case and called it "CURRENT BEHAVIOUR,
    /// pinned so a change is deliberate" — so the suite was green, the name described the intent,
    /// and the assertion held the fault in place. **A test whose name and assertion disagree is
    /// worse than no test: it reads as coverage.**
    ///
    /// The mechanism: `OperatorBoundaryMiddleware` 403s a platform-admin from tenant data only while
    /// they carry no `tid` and are not impersonating. Impersonation stamps a `tid`, so the middleware
    /// steps aside and relies on this property to scope them — and it returned `Guid.Empty`, which
    /// the query filter reads as *every tenant*.
    /// </summary>
    [Fact]
    public void An_impersonating_operator_is_scoped_to_the_tenant_they_are_impersonating()
    {
        var theShop = Guid.Parse("01a0343b-e88d-7c00-ba8b-ee0e231c6616"); // Test Business, the real one
        var ctx = ContextFor(
            new Claim("scope", PlutusPolicies.PlatformAdmin),
            new Claim("tid", theShop.ToString()));

        Assert.Equal(theShop, ctx.TenantId);

        // ⚠ The property that was actually violated, stated on its own: an unscoped context is what
        // the global query filter treats as "show all tenants", so this must NEVER be Empty while a
        // tenant is being impersonated.
        Assert.NotEqual(Guid.Empty, ctx.TenantId);
        Assert.NotEqual(WellKnownTenants.Kapow, ctx.TenantId);
    }

    /// <summary>⚠ The other half must still hold, or provisioning and the operator console break:
    /// a platform-admin with NO tenant claim stays unscoped deliberately.</summary>
    [Fact]
    public void A_plain_platform_admin_is_still_unscoped_so_provisioning_works()
    {
        var ctx = ContextFor(new Claim("scope", PlutusPolicies.PlatformAdmin));
        Assert.Equal(Guid.Empty, ctx.TenantId);
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
