using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Identity;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// Loyalty usability: customer writes are gated on the dedicated `customers.manage` permission
/// (not portal.users.manage). An operator without it is 403'd on create; a manager holding it
/// (via a seeded built-in role) can create AND edit (the new PUT), and the edit round-trips.
/// </summary>
public class CustomersLoyaltyE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public CustomersLoyaltyE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    /// <summary>Assigns a user a built-in role that carries customers.manage (Store Manager).</summary>
    private async Task<Guid> SeedCustomerManagerAsync()
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "loyalty-e2e-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var manager = await db.RbacRoles.FirstAsync(r => r.Name == "Store Manager");
        db.RbacRoleAssignments.Add(new Plutus.Entities.Models.RbacRoleAssignment
        {
            Id = Plutus.SharedKernel.Uuid7.New(), TenantId = Kapow, UserId = userId,
            RoleId = manager.Id, ScopeType = Plutus.Entities.Models.RbacScopeType.Tenant,
            ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task Customer_writes_are_gated_on_customers_manage_and_edit_round_trips()
    {
        var client = _f.CreateClient();

        // authenticated operator (pos.sell) but NO customers.manage assignment → 403 on create
        var outsider = PlutusAppFactory.OperatorTokenFor(Guid.NewGuid(), "pos.sell");
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", outsider);
            req.Content = JsonContent.Create(new { name = "Blocked" });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // a manager holding customers.manage (Store Manager) → create succeeds
        var managerId = await SeedCustomerManagerAsync();
        var manager = PlutusAppFactory.OperatorTokenFor(managerId, "pos.sell");

        Guid customerId;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = "Ada Lovelace", email = "ada@example.com" });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            customerId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
                .RootElement.GetProperty("id").GetGuid();
        }

        // edit (the new PUT) → 200 and the change round-trips on GET
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/customers/{customerId}"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = "Ada King", email = "ada@example.com", phone = "0700" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/customers/{customerId}"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("Ada King", body.GetProperty("name").GetString());
            Assert.Equal("0700", body.GetProperty("phone").GetString());
        }

        // the outsider still cannot edit
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/customers/{customerId}"))
        {
            req.Headers.Authorization = new("Bearer", outsider);
            req.Content = JsonContent.Create(new { name = "Hacked" });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }
    }
}
