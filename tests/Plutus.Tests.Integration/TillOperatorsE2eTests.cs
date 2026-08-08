using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP8 — the roster endpoint, against the real controller.
///
/// This is the payload that turns an enrolled till from "connected" into "usable", and it carries
/// password hashes to shop-floor hardware. The tests that matter most here are the ones about what
/// it must NOT contain.
/// </summary>
public class TillOperatorsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public TillOperatorsE2eTests(PlutusAppFactory f) => _f = f;

    private sealed record Tenant(HttpClient Http, Guid TenantId, Guid TillId, int StoreId, string DeviceToken);

    private MySqlDbContext Db(IServiceScope scope, Guid tenantId)
    {
        var db = new MySqlDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
            new Plutus.Entities.Tenancy.FixedTenantContext(tenantId));
        db.CurrentUser = "operators-e2e";
        return db;
    }

    private async Task<Tenant> ProvisionAsync(string email)
    {
        var http = _f.CreateClient();

        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using var pReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        { Content = JsonContent.Create(new { name = "Ops " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" }) };
        pReq.Headers.Authorization = new("Bearer", admin);
        var pRes = await http.SendAsync(pReq);
        Assert.Equal(HttpStatusCode.Created, pRes.StatusCode);
        var pBody = JsonDocument.Parse(await pRes.Content.ReadAsStringAsync()).RootElement;
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        using var tReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills")
        { Content = JsonContent.Create(new { storeId, name = "Counter 1" }) };
        tReq.Headers.Authorization = new("Bearer", portal);
        var tRes = await http.SendAsync(tReq);
        Assert.Equal(HttpStatusCode.Created, tRes.StatusCode);
        var tBody = JsonDocument.Parse(await tRes.Content.ReadAsStringAsync()).RootElement;
        var tillId = tBody.GetProperty("tillId").GetGuid();
        var code = tBody.GetProperty("enrolmentCode").GetString()!;

        var api = new PlutusApiClient(http);
        var enrolled = await api.EnrolAsync(code);
        var token = (await api.GetDeviceTokenAsync(enrolled.DeviceId, enrolled.ClientSecret)).AccessToken;

        return new Tenant(http, tenantId, tillId, storeId, token);
    }

    /// <summary>Seed a staff member with a web login and a role granting <paramref name="code"/> at
    /// the given scope.</summary>
    private async Task<Guid> SeedOperatorAsync(
        Tenant t, string email, string password, int storeId, string code, RbacScopeType scopeType, string scopeId,
        long? maxPence = null)
    {
        using var scope = _f.Services.CreateScope();
        var db = Db(scope, t.TenantId);

        // Employee carries FKs to BOTH the store and the legacy Business — ⚠ businessId is not the
        // tenant id, the distinction that runs through this whole codebase.
        var businessId = await db.Stores.AsNoTracking()
            .Where(s => s.Id == storeId).Select(s => s.BusinessId).FirstAsync();

        var userId = Uuid7.New();
        db.Employees.Add(new Employee
        {
            Id = userId, BusinessId = businessId, FName = "Test", LName = email.Split('@')[0], Email = email,
            Active = true, StoreId = storeId, Mobile = "-", NIN = "-",
            AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
        });

        var (hash, salt) = Pbkdf2.Hash(password);
        db.WebCredentials.Add(new WebCredential
        {
            Email = email, EmployeeId = userId,
            HashedPassword = Convert.ToBase64String(hash), Salt = Convert.ToBase64String(salt),
        });

        var role = new RbacRole { Id = Uuid7.New(), TenantId = t.TenantId, Name = "Role " + email, IsBuiltIn = false };
        db.RbacRoles.Add(role);
        db.RbacRoleGrants.Add(new RbacRoleGrant { Id = Uuid7.New(), RoleId = role.Id, PermissionCode = code, MaxPence = maxPence });
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = t.TenantId, UserId = userId, RoleId = role.Id,
            ScopeType = scopeType, ScopeId = scopeId, CreatedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return userId;
    }

    private async Task<TillOperatorsResult> RosterAsync(Tenant t)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tills/{t.TillId}/operators");
        req.Headers.Authorization = new("Bearer", t.DeviceToken);
        var res = await t.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<TillOperatorsResult>(PlutusApiClient.Json))!;
    }

    [Fact]
    public async Task A_DEVICE_token_can_read_the_roster()
    {
        // ⚠ Load-bearing: the till must refresh its roster BEFORE anyone signs in, or the first
        // sign-in of the day would need a sign-in.
        var t = await ProvisionAsync("ops-device@acme.test");
        await SeedOperatorAsync(t, "sam@acme.test", "S3cret!", t.StoreId, PermissionCatalogue.PosSell,
            RbacScopeType.Store, t.StoreId.ToString());

        var roster = await RosterAsync(t);

        Assert.Equal(t.TillId, roster.TillId);
        Assert.Contains(roster.Operators, o => o.Email == "sam@acme.test");
    }

    [Fact]
    public async Task The_payload_carries_a_VERIFIABLE_hash_and_the_RAW_window()
    {
        var t = await ProvisionAsync("ops-hash@acme.test");
        await SeedOperatorAsync(t, "sup@acme.test", "S3cret!", t.StoreId, PermissionCatalogue.PosRefund,
            RbacScopeType.Till, t.TillId.ToString("D").ToLowerInvariant(), maxPence: 2500);

        var op = Assert.Single(
            (await RosterAsync(t)).Operators.ToList(), o => o.Email == "sup@acme.test");

        // The hash must actually verify with the SAME KDF the till uses — that is the whole
        // premise of offline sign-in, and a mismatch here would look like everyone typing their
        // password wrong.
        Assert.True(Pbkdf2.Verify("S3cret!",
            Convert.FromBase64String(op.CredentialSaltBase64!),
            Convert.FromBase64String(op.CredentialHashBase64!)));

        var grant = Assert.Single(op.Grants.Where(g => g.Code == PermissionCatalogue.PosRefund).ToList());
        Assert.Equal(2500, grant.MaxPence);
    }

    [Fact]
    public async Task A_till_NEVER_receives_another_tenants_operators_or_their_hashes()
    {
        // ⚠ THE ONE THAT MATTERS MOST. WebCredentials is a GLOBAL table with no tenant query filter
        // — login is by email, before a tenant is known. So this endpoint has to join through
        // Employee to stay inside the tenant. Get it wrong and every till in the estate receives
        // every other customer's password hashes.
        var a = await ProvisionAsync("ops-iso-a@acme.test");
        var b = await ProvisionAsync("ops-iso-b@acme.test");
        await SeedOperatorAsync(a, "only-a@acme.test", "S3cret!", a.StoreId, PermissionCatalogue.PosSell,
            RbacScopeType.Store, a.StoreId.ToString());
        await SeedOperatorAsync(b, "only-b@acme.test", "S3cret!", b.StoreId, PermissionCatalogue.PosSell,
            RbacScopeType.Store, b.StoreId.ToString());

        var seenByB = (await RosterAsync(b)).Operators.Select(o => o.Email).ToList();

        Assert.Contains("only-b@acme.test", seenByB);
        Assert.DoesNotContain("only-a@acme.test", seenByB);
    }

    [Fact]
    public async Task A_DEACTIVATED_employee_is_not_shipped_to_the_till()
    {
        // Expiry is the only revocation that reaches an offline till, so the roster must not be
        // handing it people who have already gone.
        var t = await ProvisionAsync("ops-inactive@acme.test");
        var userId = await SeedOperatorAsync(t, "gone@acme.test", "S3cret!", t.StoreId, PermissionCatalogue.PosSell,
            RbacScopeType.Store, t.StoreId.ToString());

        using (var scope = _f.Services.CreateScope())
        {
            var db = Db(scope, t.TenantId);
            var e = await db.Employees.FirstAsync(x => x.Id == userId);
            e.Active = false;
            await db.SaveChangesAsync();
        }

        Assert.DoesNotContain((await RosterAsync(t)).Operators, o => o.Email == "gone@acme.test");
    }

    [Fact]
    public async Task An_operator_synced_from_the_server_can_then_sign_in_OFFLINE()
    {
        // The end-to-end claim of WP8: pull the roster once while online, then sign in with no
        // network at all.
        var t = await ProvisionAsync("ops-offline@acme.test");
        await SeedOperatorAsync(t, "sam-offline@acme.test", "S3cret!", t.StoreId, PermissionCatalogue.PosSell,
            RbacScopeType.Store, t.StoreId.ToString());

        var store = new InMemoryOperatorStore();
        var api = new PlutusApiClient(t.Http, new FixedToken(t.DeviceToken));
        var count = await new OperatorSync(api, store).RefreshAsync(t.TillId);
        Assert.True(count > 0);

        // No further server calls from here — everything below is local.
        var result = await new OperatorLogin(store).SignInAsync("sam-offline@acme.test", "S3cret!");

        Assert.True(result.Succeeded);
        Assert.True(result.Operator!.Can(PermissionCatalogue.PosSell));
        Assert.False(result.Operator.Can(PermissionCatalogue.PosRefund));
    }

    private sealed class FixedToken : IDeviceTokenProvider
    {
        private readonly string _token;
        public FixedToken(string token) => _token = token;
        public Task<string?> GetAccessTokenAsync(System.Threading.CancellationToken ct = default) => Task.FromResult<string?>(_token);
        public void Invalidate() { }
    }

    private sealed class InMemoryOperatorStore : IOperatorStore
    {
        private TillOperatorsResult? _roster;
        public Task<TillOperatorsResult?> LoadAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(_roster);
        public Task SaveAsync(TillOperatorsResult roster, System.Threading.CancellationToken ct = default) { _roster = roster; return Task.CompletedTask; }
        public Task ClearAsync(System.Threading.CancellationToken ct = default) { _roster = null; return Task.CompletedTask; }
    }
}
