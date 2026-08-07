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
/// FE10 till theming: writes gated on portal.company.manage; the till-facing /effective read on
/// sales.ingest; precedence Till > Group > Store > Tenant > default resolved server-side;
/// deleting a theme or group cascades its assignments; tenants fully isolated.
/// </summary>
public class ThemesE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public ThemesE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private HttpRequestMessage Req(HttpMethod m, string url, string token, object body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", token);
        return req;
    }

    private async Task<Guid> SeedOwnerAsync()
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "themes-e2e-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var owner = await db.RbacRoles.FirstAsync(r => r.Name == "Owner");
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Kapow, UserId = userId, RoleId = owner.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    /// <summary>Business → Store → Till chain, as a till fixture for precedence tests.</summary>
    private async Task<(int StoreId, Guid TillId)> SeedShopAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "themes-e2e-seed";
        var biz = new Business { Id = Guid.NewGuid(), Name = $"Theme E2E {Guid.NewGuid():N}", NameAbbr = "TH", VatIN = "GB0" };
        db.Business.Add(biz);
        var store = new Store
        {
            BusinessId = biz.Id, AdLine1 = "1 Theme St", AdLine2 = "", City = "Leeds",
            PostCode = "LS1 1AA", Country = "UK", ContactNumber = "0113 000000",
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync(); // identity store id
        var till = new Till { Id = Uuid7.New(), StoreId = store.Id, CashFloat = 0, LastOnline = DateTime.UtcNow };
        db.Till.Add(till);
        await db.SaveChangesAsync();
        return (store.Id, till.Id);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Writes_are_perm_gated_and_validated()
    {
        var client = _f.CreateClient();
        var cashier = PlutusAppFactory.OperatorToken("pos.sell", Kapow);

        // a cashier can read the EFFECTIVE theme (the till needs it) but cannot manage themes
        Assert.Equal(HttpStatusCode.OK,
            (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/themes/effective", cashier))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/themes", cashier))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.SendAsync(Req(HttpMethod.Post, "/api/v1/themes", cashier, new { name = "X", baseMode = "light" }))).StatusCode);

        var owner = PlutusAppFactory.OperatorTokenFor(await SeedOwnerAsync(), "pos.sell");

        // validation: bad mode, bad json, then a good create; duplicate name → 409
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Req(HttpMethod.Post, "/api/v1/themes", owner, new { name = "Bad", baseMode = "sepia" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Req(HttpMethod.Post, "/api/v1/themes", owner, new { name = "Bad", baseMode = "light", colorsJson = "not json" }))).StatusCode);
        var created = await client.SendAsync(Req(HttpMethod.Post, "/api/v1/themes", owner,
            new { name = "Kapow House", baseMode = "light", colorsJson = "{\"accent\":\"#7a1f7a\"}" }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.SendAsync(Req(HttpMethod.Post, "/api/v1/themes", owner, new { name = "kapow house", baseMode = "dark" }))).StatusCode);

        // assignment validation: unknown store, unknown theme key
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/themes/assignments", owner,
                new { scope = 1, scopeKey = "999999", themeKey = "builtin:dark" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/themes/assignments", owner,
                new { scope = 0, scopeKey = "", themeKey = Guid.NewGuid().ToString() }))).StatusCode);
    }

    [Fact]
    public async Task Precedence_till_group_store_tenant_with_cascading_deletes()
    {
        var client = _f.CreateClient();
        var owner = PlutusAppFactory.OperatorTokenFor(await SeedOwnerAsync(), "pos.sell");
        var till = PlutusAppFactory.OperatorToken("pos.sell", Kapow);
        var (storeId, tillId) = await SeedShopAsync();

        // two custom themes + a group containing the till
        var themeA = (await ReadJson(await client.SendAsync(Req(HttpMethod.Post, "/api/v1/themes", owner,
            new { name = "Store Scheme", baseMode = "light", colorsJson = "{\"accent\":\"#116611\"}" })))).GetProperty("id").GetString();
        var themeB = (await ReadJson(await client.SendAsync(Req(HttpMethod.Post, "/api/v1/themes", owner,
            new { name = "Group Scheme", baseMode = "dark", colorsJson = "{\"accent\":\"#661111\"}" })))).GetProperty("id").GetString();
        var groupId = (await ReadJson(await client.SendAsync(Req(HttpMethod.Post, "/api/v1/till-groups", owner,
            new { name = "Counter tills", tillIds = new[] { tillId } })))).GetProperty("id").GetString();

        // assign every scope: tenant=builtin:dark, store=A, group=B, till=builtin:light
        foreach (var a in new object[]
        {
            new { scope = 0, scopeKey = "", themeKey = "builtin:dark" },
            new { scope = 1, scopeKey = storeId.ToString(), themeKey = themeA },
            new { scope = 2, scopeKey = groupId, themeKey = themeB },
            new { scope = 3, scopeKey = tillId.ToString(), themeKey = "builtin:light" },
        })
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/themes/assignments", owner, a))).StatusCode);

        // till wins
        var eff = await ReadJson(await client.SendAsync(Req(HttpMethod.Get, $"/api/v1/themes/effective?tillId={tillId}", till)));
        Assert.Equal("till", eff.GetProperty("source").GetString());
        Assert.Equal("light", eff.GetProperty("baseMode").GetString());

        // clear the till assignment → group wins (and carries the custom colours)
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/themes/assignments", owner,
            new { scope = 3, scopeKey = tillId.ToString(), themeKey = (string)null }))).StatusCode);
        eff = await ReadJson(await client.SendAsync(Req(HttpMethod.Get, $"/api/v1/themes/effective?tillId={tillId}", till)));
        Assert.Equal("group", eff.GetProperty("source").GetString());
        Assert.Equal("Group Scheme", eff.GetProperty("name").GetString());
        Assert.Contains("#661111", eff.GetProperty("colorsJson").GetString());

        // delete the group → store wins
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.SendAsync(Req(HttpMethod.Delete, $"/api/v1/till-groups/{groupId}", owner))).StatusCode);
        eff = await ReadJson(await client.SendAsync(Req(HttpMethod.Get, $"/api/v1/themes/effective?tillId={tillId}", till)));
        Assert.Equal("store", eff.GetProperty("source").GetString());
        Assert.Equal("Store Scheme", eff.GetProperty("name").GetString());

        // delete theme A → its store assignment cascades away → tenant default (builtin:dark)
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.SendAsync(Req(HttpMethod.Delete, $"/api/v1/themes/{themeA}", owner))).StatusCode);
        eff = await ReadJson(await client.SendAsync(Req(HttpMethod.Get, $"/api/v1/themes/effective?tillId={tillId}", till)));
        Assert.Equal("tenant", eff.GetProperty("source").GetString());
        Assert.Equal("builtin:dark", eff.GetProperty("themeKey").GetString());
        Assert.Equal("dark", eff.GetProperty("baseMode").GetString());

        // an UN-ENROLLED till in the same store (storeId only, no tillId): store scope is gone,
        // so it also lands on the tenant default
        eff = await ReadJson(await client.SendAsync(Req(HttpMethod.Get, $"/api/v1/themes/effective?storeId={storeId}", till)));
        Assert.Equal("tenant", eff.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Tenants_are_isolated_and_default_is_system()
    {
        var client = _f.CreateClient();
        var owner = PlutusAppFactory.OperatorTokenFor(await SeedOwnerAsync(), "pos.sell");

        // Kapow sets a tenant default…
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/themes/assignments", owner,
            new { scope = 0, scopeKey = "", themeKey = "builtin:dark" }))).StatusCode);

        // …which another tenant's till never sees: it gets the built-in default, mode "system"
        var otherTill = PlutusAppFactory.OperatorToken("pos.sell", Guid.NewGuid());
        var eff = await ReadJson(await client.SendAsync(Req(HttpMethod.Get, "/api/v1/themes/effective", otherTill)));
        Assert.Equal("default", eff.GetProperty("source").GetString());
        Assert.Equal("system", eff.GetProperty("baseMode").GetString());
        Assert.Equal(JsonValueKind.Null, eff.GetProperty("themeKey").ValueKind);
    }
}
