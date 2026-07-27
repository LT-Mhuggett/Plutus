using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Identity;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP3.2 HTTP-level RBAC gate: the perm:* policies resolve against RbacRoleAssignments (not
/// token scope claims) — an operator without the permission gets 403 on an admin endpoint, an
/// operator holding it (via a seeded assignment) gets 200, and admin mutations leave audit
/// rows readable from GET /api/v1/audit.
/// </summary>
public class AdminRbacE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public AdminRbacE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private async Task<Guid> SeedManagerAsync()
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "rbac-e2e-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var staffAdmin = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstAsync(db.RbacRoles, r => r.Name == "Staff Admin");
        db.RbacRoleAssignments.Add(new Plutus.Entities.Models.RbacRoleAssignment
        {
            Id = Plutus.SharedKernel.Uuid7.New(), TenantId = Kapow, UserId = userId,
            RoleId = staffAdmin.Id, ScopeType = Plutus.Entities.Models.RbacScopeType.Tenant,
            ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task Perm_gated_endpoint_403_without_assignment_200_with_and_mutations_audit()
    {
        var client = _f.CreateClient();

        // no RBAC assignment → 403 even though the operator is authenticated with pos.sell
        var outsider = PlutusAppFactory.OperatorTokenFor(Guid.NewGuid(), "pos.sell");
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/roles"))
        {
            req.Headers.Authorization = new("Bearer", outsider);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // seeded Staff Admin (portal.users.manage) → 200
        var managerId = await SeedManagerAsync();
        var manager = PlutusAppFactory.OperatorTokenFor(managerId, "pos.sell");
        JsonElement roles;
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/roles"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            roles = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        // assign Cashier to a new user via the API, then the audit trail shows it
        Guid cashierId = default;
        foreach (var r in roles.EnumerateArray())
            if (r.GetProperty("name").GetString() == "Cashier") cashierId = r.GetProperty("id").GetGuid();
        Assert.NotEqual(default, cashierId);

        var newUser = Guid.NewGuid();
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/users/{newUser}/role-assignments"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { roleId = cashierId, scope = "tenant" });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(req)).StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit?entityType=RbacRoleAssignment"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("role.assign", body);
            Assert.Contains(managerId.ToString(), body); // the actor is recorded
        }

        // and the new user's effective permissions now include pos.sell (self-service read)
        var newUserToken = PlutusAppFactory.OperatorTokenFor(newUser, "pos.sell");
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{newUser}/effective-permissions"))
        {
            req.Headers.Authorization = new("Bearer", newUserToken);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("pos.sell", await resp.Content.ReadAsStringAsync());
        }
    }
}
