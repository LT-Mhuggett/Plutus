using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Phase 9 (IdP swap): the provider-agnostic claims bridge. A real IdP (Entra/Keycloak) only
/// authenticates — <see cref="RbacClaimsTransformation"/> must turn a validated JWT's email into
/// the SAME internal claim set (Plutus EmployeeId + RBAC scopes + legacy scp) the HMAC login path
/// emits, so authorization is untouched by the swap. Device/test principals must pass through
/// unchanged (till client-credentials unaffected — the phase DoD).
/// </summary>
public class Phase9AuthTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid Ada = Guid.NewGuid();
    private const string AdaEmail = "ada@shop.test";

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "phase9-test" };

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["OpenAPI:Scopes:APIRead:Name"] = "API.Read",
            ["OpenAPI:Scopes:APIWrite:Name"] = "API.Write",
        }).Build();

    /// <summary>A user with a real RBAC assignment (pos.sell + portal.tills.enrol at tenant scope)
    /// and a matching web login row keyed by email.</summary>
    private static SqliteConnection OpenSeeded()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        ctx.WebCredentials.Add(new WebCredential { Email = AdaEmail, EmployeeId = Ada, HashedPassword = "", Salt = "" });

        var role = new RbacRole
        {
            Id = Uuid7.New(), TenantId = Tenant, Name = "Cashier+", IsBuiltIn = false, CreatedAtUtc = DateTime.UtcNow,
            Grants = new[]
            {
                new RbacRoleGrant { Id = Uuid7.New(), TenantId = Tenant, PermissionCode = PermissionCatalogue.PosSell, MaxPence = null },
                new RbacRoleGrant { Id = Uuid7.New(), TenantId = Tenant, PermissionCode = PermissionCatalogue.PortalTillsEnrol, MaxPence = null },
            }.ToList(),
        };
        ctx.RbacRoles.Add(role);
        ctx.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Tenant, UserId = Ada, RoleId = role.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        ctx.SaveChanges();
        return conn;
    }

    private static RbacClaimsTransformation Transform(SqliteConnection conn)
    {
        var db = Ctx(conn);
        return new RbacClaimsTransformation(db, new EffectivePermissionsService(db), Config,
            NullLogger<RbacClaimsTransformation>.Instance);
    }

    private static ClaimsPrincipal Jwt(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Bearer"));

    [Fact]
    public async Task Idp_email_is_mapped_to_the_plutus_user_with_their_rbac_scopes()
    {
        using var conn = OpenSeeded();
        var principal = Jwt(new Claim("email", AdaEmail), new Claim("sub", "idp-subject-xyz"));

        var result = await Transform(conn).TransformAsync(principal);

        // The Plutus EmployeeId — NOT the IdP subject — is what every controller reads.
        Assert.Equal(Ada.ToString(), result.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        Assert.Equal(Ada.ToString(), result.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")!.Value);

        var scopes = result.FindAll("scope").Select(c => c.Value).ToArray();
        Assert.Contains(PlutusPolicies.PosSell, scopes);
        Assert.Contains(PlutusPolicies.PortalTillsEnrol, scopes);

        // Legacy [RequiredScope] filters still see their B2C API scopes.
        var scp = result.FindFirst("scp")!.Value.Split(' ');
        Assert.Contains("API.Read", scp);
        Assert.Contains("API.Write", scp);
    }

    [Fact]
    public async Task Unknown_idp_email_is_authenticated_but_gets_no_scopes()
    {
        using var conn = OpenSeeded();
        var principal = Jwt(new Claim("email", "stranger@elsewhere.test"));

        var result = await Transform(conn).TransformAsync(principal);

        Assert.Null(result.FindFirst(ClaimTypes.NameIdentifier));
        Assert.Empty(result.FindAll("scope"));
    }

    // ── WP18.1: the Keycloak `platform-admin` realm role (operators group) → the scope ──

    [Fact]
    public async Task Operators_realm_role_maps_to_the_platform_admin_scope()
    {
        using var conn = OpenSeeded();
        // Keycloak's default shape: realm roles arrive as the realm_access JSON claim. An
        // operator need not exist as a tenant user — the Platform surface must still open.
        var principal = Jwt(
            new Claim("email", "ops@plutus.test"),
            new Claim("realm_access", "{\"roles\":[\"default-roles-plutus\",\"platform-admin\"]}"));

        var result = await Transform(conn).TransformAsync(principal);

        Assert.Contains(result.FindAll("scope"), c => c.Value == PlutusPolicies.PlatformAdmin);
    }

    [Fact]
    public async Task Idp_token_without_the_role_gets_no_platform_admin()
    {
        using var conn = OpenSeeded();
        // A tenant user with ordinary RBAC — and a realm_access WITHOUT the role — must not
        // gain platform-admin (the role, not the group name or any other claim, is the key).
        var principal = Jwt(
            new Claim("email", AdaEmail),
            new Claim("realm_access", "{\"roles\":[\"default-roles-plutus\"]}"));

        var result = await Transform(conn).TransformAsync(principal);

        var scopes = result.FindAll("scope").Select(c => c.Value).ToArray();
        Assert.Contains(PlutusPolicies.PosSell, scopes);
        Assert.DoesNotContain(PlutusPolicies.PlatformAdmin, scopes);
    }

    [Fact]
    public async Task Device_and_test_principals_pass_through_untouched()
    {
        using var conn = OpenSeeded();
        // A device token is validated under the "PlutusToken" scheme, not "Bearer".
        var device = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("scope", "device"), new Claim("did", Guid.NewGuid().ToString()) },
            authenticationType: PlutusTokenAuthHandler.SchemeName));

        var result = await Transform(conn).TransformAsync(device);

        Assert.Null(result.FindFirst(ClaimTypes.NameIdentifier));
        Assert.DoesNotContain(result.Claims, c => c.Type == "scp");
        Assert.Equal("device", result.FindFirst("scope")!.Value); // its own claim intact
    }

    [Fact]
    public async Task Transform_is_idempotent()
    {
        using var conn = OpenSeeded();
        var xform = Transform(conn);
        var once = await xform.TransformAsync(Jwt(new Claim("email", AdaEmail)));
        var twice = await xform.TransformAsync(once);

        Assert.Single(twice.FindAll(ClaimTypes.NameIdentifier));
        Assert.Single(twice.FindAll("scp"));
    }

    [Theory]
    // A compact HMAC device/enrolment token (2 segments) MUST route to the HMAC handler even
    // under a real IdP — this is what keeps till client-credentials working (phase DoD).
    [InlineData("Bearer aGVhZGVy.c2ln", PlutusTokenAuthHandler.SchemeName)]
    // A real OIDC JWT (3 segments) routes to the IdP.
    [InlineData("Bearer aGVhZGVy.cGF5bG9hZA.c2ln", AuthSchemeRouter.JwtBearerScheme)]
    // No / non-bearer header falls through to the IdP (which then 401-challenges).
    [InlineData("", AuthSchemeRouter.JwtBearerScheme)]
    [InlineData("Basic abc", AuthSchemeRouter.JwtBearerScheme)]
    public void Router_sends_device_tokens_to_hmac_and_jwts_to_the_idp(string header, string expectedScheme)
        => Assert.Equal(expectedScheme, AuthSchemeRouter.Select(header));

    /// <summary>
    /// Regression (Matt, 2026-07-31): an OWNER opened the till's Settings and was told they lacked
    /// permission to change device settings. The resolver used to emit only pos.sell +
    /// portal.tills.enrol, so the token LIED to the till's UI (which can only read the Scope claim)
    /// — the server's perm:* gates would have allowed it all along. The token must carry the user's
    /// full effective permission set.
    /// </summary>
    [Fact]
    public async Task Login_scopes_carry_the_full_effective_permission_set()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();

        var owner = Guid.NewGuid();
        await RbacSeeder.EnsureBuiltInRolesAsync(ctx, Tenant);
        var role = await ctx.RbacRoles.FirstAsync(r => r.Name == "Owner");
        ctx.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Tenant, UserId = owner, RoleId = role.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var scopes = await new EffectivePermissionsService(ctx).ResolveLoginScopesAsync(owner, DateTime.Now);

        // the ones the till's UI actually checks (canManageSettings / canManageCustomers /
        // canEnrolTills), and the baseline
        Assert.Contains(PermissionCatalogue.PosSettingsManage, scopes);
        Assert.Contains(PermissionCatalogue.CustomersManage, scopes);
        Assert.Contains(PermissionCatalogue.PortalTillsEnrol, scopes);
        Assert.Contains(PermissionCatalogue.PosSell, scopes);
        // every emitted scope is a known catalogue code — and NEVER platform-admin, which is not a
        // permission and must only ever arrive via a real IdP token
        Assert.All(scopes, s => Assert.True(PermissionCatalogue.IsKnown(s), s));
        Assert.DoesNotContain(PlutusPolicies.PlatformAdmin, scopes);
        conn.Dispose();
    }

    [Fact]
    public async Task Login_scope_resolver_falls_back_to_pos_sell_without_assignments()
    {
        // No RBAC assignments, no legacy Admin/Management → the WP2.2 pre-seed default.
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();

        var scopes = await new EffectivePermissionsService(ctx).ResolveLoginScopesAsync(Guid.NewGuid(), DateTime.Now);

        Assert.Equal(new[] { PlutusPolicies.PosSell }, scopes);
        conn.Dispose();
    }
}
