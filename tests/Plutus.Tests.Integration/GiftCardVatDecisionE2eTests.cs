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
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// FE7 amendment (Matt, 2026-07-31): the HMRC voucher-treatment DECISION is the gate on the whole
/// gift-card feature. Until the store owner declares whether VAT is charged when a card is sold
/// (single-purpose: one rate across the catalogue) or when it is spent (multi-purpose: mixed rates),
/// nothing works — because a silent default files someone's VAT return for them. Once the first card
/// is sold, the choice locks: its VAT has been declared under that treatment.
///
/// Own fixture (fresh in-memory DB) so this class fully controls whether a decision exists yet —
/// the other gift-card tests declare one as part of their setup.
/// </summary>
public class GiftCardVatDecisionE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public GiftCardVatDecisionE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private async Task<Guid> SeedManagerAsync()
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "giftcard-vat-e2e-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var role = await db.RbacRoles.FirstAsync(r => r.Name == "Store Manager");
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Plutus.SharedKernel.Uuid7.New(), TenantId = Kapow, UserId = userId,
            RoleId = role.Id, ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(
        HttpClient client, HttpMethod method, string url, string token, object body = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new("Bearer", token);
            if (body != null) req.Content = JsonContent.Create(body);
            var resp = await client.SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt < 4)
            {
                resp.Dispose();
                await Task.Delay(1100);   // shared per-tenant limiter, 1s window
                continue;
            }
            var text = await resp.Content.ReadAsStringAsync();
            return (resp.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement);
        }
    }

    /// <summary>The whole lifecycle of the decision, in the order a real tenant hits it.</summary>
    [Fact]
    public async Task No_decision_means_no_gift_cards_and_the_first_sale_locks_the_choice()
    {
        var client = _f.CreateClient();
        var manager = PlutusAppFactory.OperatorTokenFor(await SeedManagerAsync(), "pos.sell");

        // 1. before any decision: settings say so, and every money endpoint refuses
        var (s0, b0) = await SendAsync(client, HttpMethod.Get, "/api/v1/giftcards/settings", manager);
        Assert.Equal(HttpStatusCode.OK, s0);
        Assert.Equal(JsonValueKind.Null, b0.GetProperty("treatment").ValueKind);

        var (gen, genBody) = await SendAsync(client, HttpMethod.Post, "/api/v1/giftcards/generate", manager, new { count = 1 });
        Assert.Equal(HttpStatusCode.Conflict, gen);
        Assert.Contains("VAT", genBody.GetProperty("detail").GetString());

        Assert.Equal(HttpStatusCode.Conflict,
            (await SendAsync(client, HttpMethod.Post, "/api/v1/giftcards/AAAA2222BBBB3/activate", manager, new { amountPence = 100 })).Status);
        Assert.Equal(HttpStatusCode.Conflict,
            (await SendAsync(client, HttpMethod.Post, "/api/v1/giftcards/AAAA2222BBBB3/redeem", manager, new { amountPence = 100 })).Status);

        // 2. garbage is rejected; a cashier with no role cannot decide VAT policy
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(client, HttpMethod.Put, "/api/v1/giftcards/settings", manager, new { treatment = "sometimes" })).Status);
        var cashier = PlutusAppFactory.OperatorTokenFor(Guid.NewGuid(), "pos.sell");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Put, "/api/v1/giftcards/settings", cashier, new { treatment = "multi" })).Status);

        // 3. decide single-purpose... then change the mind to multi — allowed while nothing is sold
        var (s1, b1) = await SendAsync(client, HttpMethod.Put, "/api/v1/giftcards/settings", manager, new { treatment = "single" });
        Assert.Equal(HttpStatusCode.OK, s1);
        Assert.Equal("single", b1.GetProperty("treatment").GetString());
        Assert.False(b1.GetProperty("locked").GetBoolean());

        var (s2, b2) = await SendAsync(client, HttpMethod.Put, "/api/v1/giftcards/settings", manager, new { treatment = "multi" });
        Assert.Equal(HttpStatusCode.OK, s2);
        Assert.Equal("multi", b2.GetProperty("treatment").GetString());

        // 4. the feature now works, and the till is told the treatment on every lookup
        var (s3, cards) = await SendAsync(client, HttpMethod.Post, "/api/v1/giftcards/generate", manager, new { count = 1, batch = "vat-e2e" });
        Assert.Equal(HttpStatusCode.Created, s3);
        var code = cards[0].GetProperty("code").GetString();

        var (s4, look) = await SendAsync(client, HttpMethod.Get, $"/api/v1/giftcards/{code}/lookup", manager);
        Assert.Equal(HttpStatusCode.OK, s4);
        Assert.Equal("multi", look.GetProperty("vatTreatment").GetString());

        // 5. the first SALE locks the decision — its VAT is now declared under it
        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Post, $"/api/v1/giftcards/{code}/activate", manager, new { amountPence = 1000 })).Status);

        var (s5, b5) = await SendAsync(client, HttpMethod.Put, "/api/v1/giftcards/settings", manager, new { treatment = "single" });
        Assert.Equal(HttpStatusCode.Conflict, s5);
        Assert.Contains("locked", b5.GetProperty("detail").GetString());

        // ...but re-affirming the SAME value stays a harmless no-op (test seeding relies on this)
        var (s6, b6) = await SendAsync(client, HttpMethod.Put, "/api/v1/giftcards/settings", manager, new { treatment = "multi" });
        Assert.Equal(HttpStatusCode.OK, s6);
        Assert.True(b6.GetProperty("locked").GetBoolean());

        var (s7, b7) = await SendAsync(client, HttpMethod.Get, "/api/v1/giftcards/settings", manager);
        Assert.Equal(HttpStatusCode.OK, s7);
        Assert.Equal("multi", b7.GetProperty("treatment").GetString());
        Assert.True(b7.GetProperty("locked").GetBoolean());
    }
}
