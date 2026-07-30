using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Identity;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// OP4 support tickets: a client raises + threads a ticket (tenant-isolated); the operator sees it
/// cross-tenant, replies (status → WaitingOnClient) and the client sees the reply; ticket volume
/// raises the support-heavy churn signal.
/// </summary>
public class SupportTicketsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public SupportTicketsE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = KnownTenants.Kapow;

    private HttpRequestMessage R(HttpMethod m, string url, string token, object body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", token);
        return req;
    }

    private sealed class FakeAlerter : IOperatorAlerter
    {
        public readonly HashSet<string> Open = new();
        public Task RaiseAsync(string k, string j, Guid? t, string kind, string m, CancellationToken ct = default) { Open.Add(k); return Task.CompletedTask; }
        public Task ClearAsync(string k, CancellationToken ct = default) { Open.Remove(k); return Task.CompletedTask; }
    }

    /// <summary>WP6.3: support endpoints are now gated on support.tickets — assign the staff user a
    /// built-in role (Cashier carries it) so their token resolves the permission from RBAC.</summary>
    private async Task<Guid> SeedStaffAsync()
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "support-e2e-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var cashier = await db.RbacRoles.FirstAsync(r => r.Name == "Cashier");
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Kapow, UserId = userId, RoleId = cashier.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task Client_raises_operator_replies_and_isolation_holds()
    {
        var client = _f.CreateClient();
        // Kapow staff (ambient tenant = Kapow via the fallback) — holds support.tickets via Cashier.
        var staff = PlutusAppFactory.OperatorTokenFor(await SeedStaffAsync(), "pos.sell");
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);

        // client raises a ticket
        Guid ticketId;
        using (var resp = await client.SendAsync(R(HttpMethod.Post, "/api/v1/support/tickets", staff,
            new { subject = "Card reader offline", body = "The terminal won't connect.", severity = 2 })))
        {
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            ticketId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        }

        // client sees it; a DIFFERENT tenant's user does not
        Assert.Contains("Card reader offline", await (await client.SendAsync(R(HttpMethod.Get, "/api/v1/support/tickets", staff))).Content.ReadAsStringAsync());
        var otherTenant = PlutusAppFactory.OperatorToken("pos.sell", Guid.NewGuid());
        Assert.DoesNotContain("Card reader offline", await (await client.SendAsync(R(HttpMethod.Get, "/api/v1/support/tickets", otherTenant))).Content.ReadAsStringAsync());

        // operator sees it cross-tenant + replies
        Assert.Contains("Card reader offline", await (await client.SendAsync(R(HttpMethod.Get, "/api/v1/platform/tickets", admin))).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(R(HttpMethod.Post, $"/api/v1/platform/tickets/{ticketId}/reply", admin, new { body = "Reboot the reader and try again." }))).StatusCode);

        // client sees the operator reply in the thread
        var thread = await (await client.SendAsync(R(HttpMethod.Get, $"/api/v1/support/tickets/{ticketId}/messages", staff))).Content.ReadAsStringAsync();
        Assert.Contains("Reboot the reader", thread);
        Assert.Contains("Plutus support", thread);

        // non-admin cannot reach the operator inbox
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(R(HttpMethod.Get, "/api/v1/platform/tickets", staff))).StatusCode);
    }

    [Fact]
    public async Task Ticket_volume_raises_support_heavy_signal()
    {
        var tenant = Guid.NewGuid();
        var alerter = new FakeAlerter();
        using var scope = _f.Services.CreateScope();
        await using var db = new MySqlDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(), new FixedTenantContext(Guid.Empty)) { CurrentUser = "op4-seed" };
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Needy Co", Status = 1, Plan = "std", Entitlements = "[]", ConnectionRef = "", IsSandbox = false, CreatedAtUtc = DateTime.UtcNow });
        for (var i = 0; i < 5; i++) // ≥ threshold, all within 28 days
            db.SupportTickets.Add(new SupportTicket
            {
                Id = Uuid7.New(), TenantId = tenant, Subject = $"t{i}", Status = 0, Severity = 0,
                RaisedByUserId = Guid.NewGuid(), RaisedByName = "x", CreatedAtUtc = DateTime.UtcNow.AddDays(-i), UpdatedAtUtc = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        await ChurnSweep.EvaluateAsync(db, alerter, DateTime.UtcNow);
        var open = await db.TenantSignals.Where(s => s.TenantId == tenant && s.ClearedAtUtc == null).Select(s => s.Signal).ToListAsync();
        Assert.Contains(TenantSignals.SupportHeavy, open);
    }
}
