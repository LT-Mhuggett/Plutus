using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
/// 16.4 billing config (operator, platform-wide: stripe/paddle/chargebee/manual, secrets
/// write-only) + 17.2 per-tenant payment gateway (client-managed, standalone default, till reads
/// /active only, cross-tenant isolated).
/// </summary>
public class CommerceConfigE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public CommerceConfigE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private HttpRequestMessage Req(HttpMethod m, string url, string token, object body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", token);
        return req;
    }

    // ── 16.4 billing ──

    [Fact]
    public async Task Billing_defaults_to_manual_and_secret_is_write_only()
    {
        var client = _f.CreateClient();
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);

        // non-admin → 403
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/platform/billing/config", PlutusAppFactory.OperatorToken("pos.sell")))).StatusCode);

        // default = manual
        var def = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/platform/billing/config", admin))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"manual\"", def);

        // catalogue carries all four options
        var cat = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/platform/billing/catalogue", admin))).Content.ReadAsStringAsync();
        foreach (var k in new[] { "manual", "stripe", "paddle", "chargebee" }) Assert.Contains(k, cat);

        // configure stripe with a secret → redacted on read; unknown provider → 400
        var put = new { provider = "stripe", enabled = true, config = new Dictionary<string, string> { ["secretKey"] = "sk_test_abc", ["webhookSecret"] = "whsec_xyz" } };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/platform/billing/config", admin, put))).StatusCode);
        var got = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/platform/billing/config", admin))).Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk_test_abc", got);
        Assert.Contains("__set__", got);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/platform/billing/config", admin, new { provider = "bitcoin", enabled = true, config = new Dictionary<string, string>() }))).StatusCode);
    }

    // ── 17.2 payment gateway ──

    private async Task<Guid> SeedOwnerAsync()
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "commerce-e2e-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var owner = await db.RbacRoles.FirstAsync(r => r.Name == "Owner");
        db.RbacRoleAssignments.Add(new Plutus.Entities.Models.RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Kapow, UserId = userId, RoleId = owner.Id,
            ScopeType = Plutus.Entities.Models.RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task Gateway_defaults_to_standalone_is_perm_gated_and_tenant_isolated()
    {
        var client = _f.CreateClient();

        // /active for ANY authenticated user defaults to standalone, not integrated
        var till = PlutusAppFactory.OperatorToken("pos.sell");
        var active = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway/active", till))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"standalone\"", active);
        Assert.Contains("\"integrated\":false", active);

        // a pos.sell user without company.manage cannot write
        var put = new { provider = "sumup", config = new Dictionary<string, string> { ["apiKey"] = "sup_secret", ["merchantCode"] = "M123" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/payments/gateway", till, put))).StatusCode);

        // an Owner (portal.company.manage) can select SumUp; secret is write-only on read-back
        var ownerId = await SeedOwnerAsync();
        var owner = PlutusAppFactory.OperatorTokenFor(ownerId, "pos.sell");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/payments/gateway", owner, put))).StatusCode);
        var got = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway", owner))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"sumup\"", got);
        Assert.DoesNotContain("sup_secret", got);
        Assert.Contains("M123", got); // non-secret field visible

        // the till now sees sumup for Kapow — still not integrated (standalone flow continues)
        var activeNow = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway/active", till))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"sumup\"", activeNow);
        Assert.Contains("\"integrated\":false", activeNow);

        // ISOLATION: a different tenant's till still sees standalone
        var otherTill = PlutusAppFactory.OperatorToken("pos.sell", Guid.NewGuid());
        var otherActive = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway/active", otherTill))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"standalone\"", otherActive);
    }

    /// <summary>
    /// The card surcharge rides the gateway settings: written by an owner, read back, and — the
    /// part the till depends on — carried on `/active` so every till learns the fee from the SAME
    /// row. ⚠ Defaults to zero everywhere, because surcharging consumer cards is banned in the UK
    /// and a fee nobody chose is money taken unlawfully at every counter at once.
    /// </summary>
    [Fact]
    public async Task Surcharge_rides_the_gateway_row_and_reaches_the_till_on_active()
    {
        var client = _f.CreateClient();
        var till = PlutusAppFactory.OperatorToken("pos.sell");

        // Default: no surcharge, on /active, without any row existing.
        var active = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway/active", till))).Content.ReadAsStringAsync();
        Assert.Contains("\"surchargeBp\":0", active);
        Assert.Contains("\"surchargeFlatPence\":0", active);

        // An owner sets 1.69% + 20p (the shape of an acquirer's own fee).
        var ownerId = await SeedOwnerAsync();
        var owner = PlutusAppFactory.OperatorTokenFor(ownerId, "pos.sell");
        var put = new
        {
            provider = "standalone", config = new Dictionary<string, string>(),
            surchargeBp = 169, surchargeFlatPence = 20,
        };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/payments/gateway", owner, put))).StatusCode);

        // The till sees it on /active; the portal sees it on GET.
        var activeNow = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway/active", till))).Content.ReadAsStringAsync();
        Assert.Contains("\"surchargeBp\":169", activeNow);
        Assert.Contains("\"surchargeFlatPence\":20", activeNow);
        var got = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway", owner))).Content.ReadAsStringAsync();
        Assert.Contains("\"surchargeBp\":169", got);

        // ⚠ The typo guards: 10%+ or £5+ is a slipped digit, refused before it reaches a counter.
        var fatFingered = new { provider = "standalone", config = new Dictionary<string, string>(), surchargeBp = 1690, surchargeFlatPence = 20 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/payments/gateway", owner, fatFingered))).StatusCode);
        var fatFlat = new { provider = "standalone", config = new Dictionary<string, string>(), surchargeBp = 0, surchargeFlatPence = 2000 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/payments/gateway", owner, fatFlat))).StatusCode);

        // ISOLATION: another tenant still charges nothing.
        var otherTill = PlutusAppFactory.OperatorToken("pos.sell", Guid.NewGuid());
        var otherActive = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway/active", otherTill))).Content.ReadAsStringAsync();
        Assert.Contains("\"surchargeBp\":0", otherActive);

        // Hygiene: back to zero so other tests in the shared fixture see the default.
        var reset = new { provider = "standalone", config = new Dictionary<string, string>(), surchargeBp = 0, surchargeFlatPence = 0 };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/payments/gateway", owner, reset))).StatusCode);
    }

    /// <summary>
    /// The catalogue row a surcharge is rung through — same discipline as the gift-card item.
    /// ⚠ Its band is descriptive only (the fee's REAL VAT follows the basket via
    /// `CardSurchargeVat`), but it must still be the zero band: a 20% band here would tempt
    /// something downstream to read it, and the item must never carry a VAT decision of its own.
    /// </summary>
    [Fact]
    public async Task The_surcharge_item_is_provisioned_zero_banded_untracked_and_idempotent()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "surcharge-e2e-item";

        var bizId = Guid.NewGuid();
        db.Business.Add(new Business { Id = bizId, Name = "Fee E2E", NameAbbr = "FEE2E", VatIN = "GB1" });
        db.Taxes.Add(new Tax { IdOne = 1, IdTwo = bizId, Name = "20%", Rate = 1.2 });
        db.Taxes.Add(new Tax { IdOne = 2, IdTwo = bizId, Name = "Zero", Rate = 1.0 });
        await db.SaveChangesAsync();

        Assert.True(await Plutus.Payments.CardSurchargeSaleItem.EnsureAsync(db) >= 1);
        Assert.Equal(0, await Plutus.Payments.CardSurchargeSaleItem.EnsureAsync(db));   // safe on every boot

        var item = await db.Items.IgnoreQueryFilters()
            .FirstAsync(i => i.IdOne == Plutus.Payments.CardSurchargeSaleItem.ItemIdOne && i.IdTwo == bizId);
        var band = await db.Taxes.IgnoreQueryFilters().FirstAsync(t => t.IdOne == item.TaxId && t.IdTwo == bizId);

        Assert.Equal(1.0, band.Rate);            // the zero band, never 1.2
        Assert.True(item.StockUntracked);        // a fee is not inventory
        Assert.Equal(0m, item.Price);            // the till prices the line from the setting
        Assert.False(string.IsNullOrWhiteSpace(item.Desc));

        // ⚠ The natural key is the SHARED constant — the till derives the item id from the same
        // string, and two spellings would be a fee that breaks the sale bridge on one till only.
        Assert.Equal(Plutus.SharedKernel.CardSurchargeVat.ItemIdOne, item.IdOne);
    }
}
