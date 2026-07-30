using System;
using System.Linq;
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
/// WP6.2 un-enrol-with-approval + WP6.3 new RBAC permissions. The un-enrol flow: a portal.tills.enrol
/// holder marks a device PendingRemoval (it keeps trading), then approval revokes it; a token without
/// the scope is 403'd. The seeder grants support.tickets to every built-in role and pos.settings.manage
/// to the manager roles.
/// </summary>
public class TillHardeningE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public TillHardeningE2eTests(PlutusAppFactory f) => _f = f;
    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private async Task<Guid> SeedDeviceAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "till-hardening-seed";
        var id = Guid.NewGuid();
        db.Devices.Add(new Device
        {
            Id = id, TenantId = Kapow, TillId = Guid.NewGuid(),
            SecretHash = new byte[32], SecretSalt = new byte[16],
            Status = DeviceStatus.Active, LastSeenSeq = 0, CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Unenrol_request_is_gated_then_approval_revokes_the_device()
    {
        var deviceId = await SeedDeviceAsync();
        var client = _f.CreateClient();
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, Kapow); // scope policy
        var cashier = PlutusAppFactory.OperatorToken("pos.sell", Kapow);

        // without portal.tills.enrol → 403
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills/unenrol-request"))
        {
            req.Headers.Authorization = new("Bearer", cashier);
            req.Content = JsonContent.Create(new { deviceId });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // request removal → PendingRemoval
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills/unenrol-request"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            req.Content = JsonContent.Create(new { deviceId });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var status = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString();
            Assert.Equal("PendingRemoval", status);
        }

        // the till can read its own status (sales.ingest — a pos.sell operator satisfies it)
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tills/devices/{deviceId}/status"))
        {
            req.Headers.Authorization = new("Bearer", cashier);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("PendingRemoval", JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString());
        }

        // approve → Revoked
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tills/devices/{deviceId}/removal"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            req.Content = JsonContent.Create(new { approve = true });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("Revoked", JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task Seeder_grants_support_tickets_to_all_and_settings_to_managers()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "till-hardening-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

        var roles = await db.RbacRoles.AsNoTracking().Where(r => r.TenantId == Kapow).ToListAsync();
        var grants = await db.RbacRoleGrants.AsNoTracking().Where(g => g.TenantId == Kapow).ToListAsync();
        string[] codesFor(string name)
        {
            var role = roles.First(r => r.Name == name);
            return grants.Where(g => g.RoleId == role.Id).Select(g => g.PermissionCode).ToArray();
        }

        // support.tickets on EVERY built-in role (incl. the lone Cashier)
        foreach (var name in new[] { "Owner", "Company Admin", "Store Manager", "Supervisor", "Cashier", "Auditor", "Stock & Items", "Staff Admin" })
            Assert.Contains(PermissionCatalogue.SupportTickets, codesFor(name));

        // pos.settings.manage only on the manager roles
        Assert.Contains(PermissionCatalogue.PosSettingsManage, codesFor("Owner"));
        Assert.Contains(PermissionCatalogue.PosSettingsManage, codesFor("Store Manager"));
        Assert.DoesNotContain(PermissionCatalogue.PosSettingsManage, codesFor("Cashier"));
    }
}
