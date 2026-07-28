using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP14.1 support impersonation: minting is platform-admin only + audited; the minted token drops
/// deny-listed permissions even though the target holds them (both in the token's scopes AND —
/// the real gate — in the permission handler, which resolves perm:* from RBAC, not the token).
/// </summary>
public class ImpersonationE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public ImpersonationE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    /// <summary>A target user who is a Company Admin (holds portal.users.manage + pos.sell etc.).</summary>
    private async Task<Guid> SeedTargetAsync()
    {
        var uid = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "imp-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        db.People.Add(new Person
        {
            Id = uid, FName = "Target", LName = "User", Email = $"{uid:N}@t.local",
            Mobile = "0", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
        });
        var admin = await db.RbacRoles.FirstAsync(r => r.Name == "Company Admin");
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Kapow, UserId = uid, RoleId = admin.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return uid;
    }

    [Fact]
    public async Task Impersonation_is_platform_admin_only_drops_denied_perms_and_is_audited()
    {
        var target = await SeedTargetAsync();
        var client = _f.CreateClient();
        var route = $"/api/v1/platform/tenants/{Kapow}/impersonate";

        // non-platform operator → 403
        using (var req = new HttpRequestMessage(HttpMethod.Post, route))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            req.Content = JsonContent.Create(new { userId = target, minutes = 30 });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // platform-admin → 200 + token
        string token;
        using (var req = new HttpRequestMessage(HttpMethod.Post, route))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
            req.Content = JsonContent.Create(new { userId = target, minutes = 30 });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            token = body.GetProperty("token").GetString();
            Assert.True(body.GetProperty("impersonating").GetBoolean());
        }

        // the minted token: impersonating, scoped to the tenant, granted minus deny-list
        var payload = TestTokenAuth.Validate(token, PlutusAppFactory.Secret);
        Assert.NotNull(payload);
        Assert.True(payload.Impersonating);
        Assert.Equal(Kapow, payload.Tid);
        var scopes = payload.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("pos.sell", scopes);                    // granted (not denied)
        Assert.DoesNotContain("portal.users.manage", scopes);   // denied
        Assert.DoesNotContain("pos.refund", scopes);            // denied

        // behavioural: the impersonation token cannot exercise a denied perm the target DOES hold
        // (perm:portal.users.manage on /api/v1/roles) — the handler blocks it for impersonators.
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/roles"))
        {
            req.Headers.Authorization = new("Bearer", token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // minting was audited
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            Assert.True(await db.AuditLogs.IgnoreQueryFilters()
                .AnyAsync(a => a.Action == "impersonation.start" && a.EntityId == target.ToString()));
        }
    }
}
