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
            var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("PendingRemoval", body.GetProperty("status").GetString());

            // ⚠ AND WHICH TILL IT IS. A device knows its own id because it holds the secret, but
            // everything per-till — the operator roster most of all — is keyed by tillId. A device
            // enrolled before that was stored locally held a working identity and could not fetch
            // a single operator; without this field its only escape was re-enrolling a machine
            // that was already correctly enrolled. Added 2026-08-09.
            Assert.True(body.TryGetProperty("tillId", out var till), "device status must say which till this is");
            Assert.NotEqual(Guid.Empty, till.GetGuid());
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

    // ── cutover step 19: the caller each endpoint was written for can reach it ────────────────

    /// <summary>
    /// ⚠ THE ENDPOINT'S OWN DOC COMMENT SAYS "a till asks to be un-enrolled", and a till holds a
    /// DEVICE token — which cannot carry `portal.tills.enrol`, the scope it was gated on. So the
    /// one caller it was written for was the one caller who could not call it, and the till's
    /// "remove this till" button had nothing to talk to.
    /// </summary>
    [Fact]
    public async Task A_till_can_request_its_own_removal_with_its_device_token()
    {
        var deviceId = await SeedDeviceAsync();
        var client = _f.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills/unenrol-request");
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.DeviceToken(deviceId, Kapow));
        req.Content = JsonContent.Create(new { deviceId });

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("PendingRemoval",
            JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// ⚠ AND ONLY ITS OWN. Without this, one enrolled till could start the removal of every other
    /// till in the estate — and un-enrolment is approved from a portal queue, so a flood of
    /// plausible-looking requests is exactly what gets waved through.
    /// </summary>
    [Fact]
    public async Task A_till_cannot_request_the_removal_of_a_different_till()
    {
        var mine = await SeedDeviceAsync();
        var someoneElses = await SeedDeviceAsync();
        var client = _f.CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills/unenrol-request");
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.DeviceToken(mine, Kapow));
        req.Content = JsonContent.Create(new { deviceId = someoneElses });

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);

        // and the other device is untouched
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var still = await db.Devices.AsNoTracking().FirstAsync(d => d.Id == someoneElses);
        Assert.Equal(DeviceStatus.Active, still.Status);
    }

    /// <summary>
    /// ⚠ A TILL COULD NOT READ ITS OWN TAKINGS. `GET /api/v1/sales` and `GET /api/v1/cash-events`
    /// — the second of which IS the X/Z drill an operator runs at the end of their own shift — were
    /// gated on PORTAL permissions that no till operator holds. They now accept `pos.reports.view`
    /// as well.
    /// </summary>
    [Fact]
    public async Task An_operator_with_pos_reports_view_can_read_the_tills_own_sales_and_cash_events()
    {
        var client = _f.CreateClient();

        var ownerId = Guid.NewGuid();
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "step19-seed";
            await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

            // ⚠ SUPERVISOR, and the role matters more than it looks. This said "Store Manager
            // holds pos.reports.view but no portal.* reporting permission" — which is FALSE:
            // `RbacSeeder` gives Store Manager `portal.financials.view` AND `portal.reports.view`
            // (RbacSeeder.cs, the Store Manager block). So this test passed on the PORTAL
            // permission and proved nothing whatever about the `pos.*` alternative it exists to
            // pin. Verified 2026-08-10 by reverting the gate on `/api/v1/reports/summary` and
            // watching the test stay green.
            //
            // Supervisor holds `pos.reports.view` and NOT ONE portal permission, which is exactly
            // the person this rule is for: they can close a day with a Z-read, so they must be able
            // to see the takings they counted against.
            var role = await db.RbacRoles.FirstAsync(r => r.TenantId == Kapow && r.Name == "Supervisor");
            db.RbacRoleAssignments.Add(new RbacRoleAssignment
            {
                Id = Guid.NewGuid(), TenantId = Kapow, RoleId = role.Id, UserId = ownerId,
                // ⚠ ScopeId is NOT NULL and "" means tenant-wide — the whole company, which is what
                // a Store Manager reading their own till's figures needs.
                ScopeType = RbacScopeType.Tenant, ScopeId = "",
            });
            await db.SaveChangesAsync();
        }

        var operatorToken = PlutusAppFactory.OperatorTokenFor(ownerId, "pos.sell", Kapow);

        foreach (var url in new[]
                 {
                     "/api/v1/sales?from=2026-08-01&to=2026-08-09",
                     $"/api/v1/cash-events?tillId={Guid.NewGuid()}&day=2026-08-09",

                     // ⚠ WP11 / step 26: the till's own X-report. Rollups are written per till per
                     // business day, so `level=till` is literally "what has this till taken today" —
                     // and it was gated on `portal.financials.view` alone, which a Store Manager
                     // does not hold. A supervisor could close the day with a Z-read and be refused
                     // the takings they had just counted against.
                     $"/api/v1/reports/summary?level=till&id={Guid.NewGuid()}&from=2026-08-01&to=2026-08-09",

                     // ⚠⚠ THE FIVE THE TILL'S **REPORTS TAB** CALLS — added 2026-08-18, after Matt
                     // found every one of them refusing: *"When I look at reports in MAUI, it is
                     // saying 'This report couldn't be read. You may not have permissions to see it.
                     // Or the till is offline'"*.
                     //
                     // ⚠ The step-26 fix above landed on `/reports/summary` ALONE while these five
                     // kept a portal-only gate — a fix applied to the endpoint that was reported
                     // rather than to the rule. Every report on `ReportCatalogue` is now here, so the
                     // next one added to that screen has a place it must appear.
                     //
                     // ⚠ These fail for a SECOND, independent reason too, which this test cannot
                     // see: the till was sending its DEVICE token, and `perm:*` resolves RBAC by the
                     // token's NameIdentifier — the device id on a device token, which holds no
                     // grants. Fixed in `ReportsViewModel` by asking as the operator. A green test
                     // here does NOT prove the screen works; only opening it does.
                     "/api/v1/reports/summary-rich?from=2026-08-01&to=2026-08-09",
                     $"/api/v1/reports/vat?level=store&id=1&from=2026-08-01&to=2026-08-09&granularity=month",
                     "/api/v1/reports/items-sold?from=2026-08-01&to=2026-08-09&storeId=1&take=50",
                     "/api/v1/reports/category-sales?from=2026-08-01&to=2026-08-09",
                     "/api/v1/reports/best-sellers?from=2026-08-01&to=2026-08-09&by=qty&take=20",
                 })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new("Bearer", operatorToken);
            var resp = await client.SendAsync(req);

            Assert.True(resp.StatusCode != HttpStatusCode.Forbidden,
                $"{url} returned 403 — a till operator still cannot read the till's own figures.");
        }
    }
}
