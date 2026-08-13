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

    /// <summary>FE2 (DoD): a new customer is given a unique membership number, and scanning that
    /// card (the "C…" barcode payload) or typing the number resolves to exactly that customer —
    /// which is what makes scan-to-attach work at the till, since scanners are keyboard-wedge into
    /// the ordinary customer search.</summary>
    [Fact]
    public async Task Member_numbers_are_issued_and_a_scanned_card_finds_its_customer()
    {
        var client = _f.CreateClient();
        var managerId = await SeedCustomerManagerAsync();
        var manager = PlutusAppFactory.OperatorTokenFor(managerId, "pos.sell");
        var name = $"Card Carrier {Guid.NewGuid().ToString()[..8]}";

        Guid custId;
        string memberNo;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            custId = body.GetProperty("id").GetGuid();
            memberNo = body.GetProperty("memberNo").GetString()!;
        }
        Assert.True(Plutus.SharedKernel.MemberNumbers.IsValid(memberNo), memberNo);

        // the detail read exposes the number and the barcode payload to print on a card
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/customers/{custId}"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var body = JsonDocument.Parse(await (await client.SendAsync(req)).Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(memberNo, body.GetProperty("memberNo").GetString());
            Assert.Equal("C" + memberNo, body.GetProperty("memberBarcode").GetString());
        }

        async Task<System.Collections.Generic.List<Guid>> SearchAsync(string term)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/customers?search={Uri.EscapeDataString(term)}");
            req.Headers.Authorization = new("Bearer", manager);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement
                .EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        }

        // scan the card (barcode payload), type the bare number, or type just the sequence
        Assert.Contains(custId, await SearchAsync("C" + memberNo));
        Assert.Contains(custId, await SearchAsync(memberNo));
        Assert.Contains(custId, await SearchAsync(memberNo[..6]));
        // a mis-keyed digit must NOT silently attach the wrong customer
        var mistyped = memberNo[..5] + (memberNo[5] == '7' ? '8' : '7') + memberNo[^1];
        Assert.DoesNotContain(custId, await SearchAsync(mistyped));

        // a second customer gets a different number
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = name + " II" });
            var body = JsonDocument.Parse(await (await client.SendAsync(req)).Content.ReadAsStringAsync()).RootElement;
            Assert.NotEqual(memberNo, body.GetProperty("memberNo").GetString());
        }
    }

    /// <summary>FE1 (DoD): the tier catalogue is gated on customers.manage; names are unique;
    /// assigning a tier derives name/rate/renewal from it; and re-rating the tier moves every
    /// member of it at once (live-follow) without re-assignment.</summary>
    [Fact]
    public async Task Loyalty_tiers_are_gated_unique_and_live_follow_their_members()
    {
        var client = _f.CreateClient();
        var outsider = PlutusAppFactory.OperatorTokenFor(Guid.NewGuid(), "pos.sell");
        var managerId = await SeedCustomerManagerAsync();
        var manager = PlutusAppFactory.OperatorTokenFor(managerId, "pos.sell");
        var suffix = Guid.NewGuid().ToString()[..8]; // tier names are tenant-unique across cases

        // gated: no customers.manage → 403
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", outsider);
            req.Content = JsonContent.Create(new { name = $"Blocked-{suffix}", autoDiscountRate = 0.1m });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // create a tier (24-month duration so the renewal date is unmistakably tier-derived)
        var tierName = $"Gold-{suffix}";
        Guid tierId;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = tierName, autoDiscountRate = 0.10m, durationMonths = 24 });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            tierId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        }

        // duplicate name (different case) → 409
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = tierName.ToLower(), autoDiscountRate = 0.2m });
            Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(req)).StatusCode);
        }

        // rate out of range → 400
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = $"Bad-{suffix}", autoDiscountRate = 1.5m });
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(req)).StatusCode);
        }

        // a customer assigned the tier by id — no tier name or rate in the body at all
        Guid custId;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = $"Tiered Tina {suffix}" });
            var resp = await client.SendAsync(req);
            custId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        }
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{custId}/membership"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { tierId });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(req)).StatusCode);
        }

        async Task<JsonElement> MembershipAsync()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/customers/{custId}");
            req.Headers.Authorization = new("Bearer", manager);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("membership").Clone();
        }

        var m = await MembershipAsync();
        Assert.Equal(tierName, m.GetProperty("tier").GetString());
        Assert.Equal(0.10m, m.GetProperty("autoDiscountRate").GetDecimal());
        Assert.Equal(tierId, m.GetProperty("tierId").GetGuid());
        // renewal came from the tier's 24-month duration, not the legacy +1 year default
        var renewal = DateOnly.Parse(m.GetProperty("renewalDay").GetString()!);
        Assert.True(renewal > DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(18), $"renewal {renewal} should be ~24 months out");

        // re-rate + rename the tier → the member follows both, with no re-assignment
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/loyalty/tiers/{tierId}"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = $"Gold Plus-{suffix}", autoDiscountRate = 0.15m });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
        }

        m = await MembershipAsync();
        Assert.Equal($"Gold Plus-{suffix}", m.GetProperty("tier").GetString());
        Assert.Equal(0.15m, m.GetProperty("autoDiscountRate").GetDecimal());

        // the loyalty list shows the same live values, and the tier reports its member count
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/loyalty"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var body = JsonDocument.Parse(await (await client.SendAsync(req)).Content.ReadAsStringAsync()).RootElement;
            var row = body.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("id").GetGuid() == custId);
            Assert.Equal($"Gold Plus-{suffix}", row.GetProperty("tier").GetString());
            Assert.Equal(0.15m, row.GetProperty("autoDiscountRate").GetDecimal());
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/loyalty/tiers"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            var tiers = JsonDocument.Parse(await (await client.SendAsync(req)).Content.ReadAsStringAsync()).RootElement;
            var tier = tiers.EnumerateArray().First(t => t.GetProperty("id").GetGuid() == tierId);
            Assert.Equal(1, tier.GetProperty("memberCount").GetInt32());
        }

        // an inactive tier can no longer be assigned
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/loyalty/tiers/{tierId}"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { name = $"Gold Plus-{suffix}", autoDiscountRate = 0.15m, active = false });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{custId}/membership"))
        {
            req.Headers.Authorization = new("Bearer", manager);
            req.Content = JsonContent.Create(new { tierId });
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(req)).StatusCode);
        }
        // ...but its existing member keeps working (deactivation is not deletion)
        m = await MembershipAsync();
        Assert.Equal(0.15m, m.GetProperty("autoDiscountRate").GetDecimal());
    }
}
