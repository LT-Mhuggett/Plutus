using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Customers;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Identity;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// FE7 gift cards. The behaviours worth pinning are the ones where a bug creates or destroys money:
/// over-redeeming, double-activating, spending a voided or expired card, a replayed till redemption
/// spending twice — and the VAT trap, where an activation must post NO VAT because the VAT-able
/// supply happens when the card is spent on goods.
/// </summary>
public class GiftCardsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public GiftCardsE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    /// <summary>A user holding giftcards.manage (via the seeded Store Manager role).</summary>
    private async Task<Guid> SeedManagerAsync()
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "giftcard-e2e-seed";
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

    /// <summary>
    /// One authenticated call. Retries a 429 rather than failing: WP13.5 throttles per TENANT at 30
    /// rps and every test in the suite shares Kapow, so a chatty case (this file walks whole card
    /// lifecycles) can legitimately hit the real limiter. A fresh HttpRequestMessage per attempt —
    /// a sent one cannot be re-sent.
    /// </summary>
    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string url, string token, object body = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new("Bearer", token);
            if (body != null) req.Content = JsonContent.Create(body);
            var resp = await client.SendAsync(req);
            if (resp.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 4) return resp;
            resp.Dispose();
            await Task.Delay(1100);   // the limiter's window is one second
        }
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage resp) =>
        JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Asserts the status and returns the parsed body.</summary>
    private static async Task<JsonElement> ExpectAsync(
        HttpClient client, HttpStatusCode expected, HttpMethod method, string url, string token, object body = null)
    {
        var resp = await Send(client, method, url, token, body);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Equal(expected, resp.StatusCode);
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement;
    }

    private static async Task ExpectStatusAsync(
        HttpClient client, HttpStatusCode expected, HttpMethod method, string url, string token, object body = null)
    {
        var resp = await Send(client, method, url, token, body);
        // report the response body on failure — a 400-vs-409 mix-up is far easier to diagnose with
        // the endpoint's own "detail" message than with two bare status codes
        var text = await resp.Content.ReadAsStringAsync();
        Assert.True(expected == resp.StatusCode, $"{method} {url} → {(int)resp.StatusCode}, expected {(int)expected}. Body: {text}");
    }

    /// <summary>Generates one card and returns its code.</summary>
    private static async Task<string> GenerateOneAsync(HttpClient client, string token, int? expiresMonths = null)
    {
        var body = await ExpectAsync(client, HttpStatusCode.Created, HttpMethod.Post,
            "/api/v1/giftcards/generate", token, new { count = 1, expiresMonths, batch = "e2e" });
        return body[0].GetProperty("code").GetString();
    }

    private static async Task<JsonElement> LookupAsync(HttpClient client, string token, string code) =>
        await ExpectAsync(client, HttpStatusCode.OK, HttpMethod.Get, $"/api/v1/giftcards/{code}/lookup", token);

    /// <summary>
    /// Minting codes is a manager job (giftcards.manage); SELLING one is not. A cashier must be able
    /// to sell cards all day without being able to print money.
    /// </summary>
    [Fact]
    public async Task Generating_codes_needs_giftcards_manage_and_produces_unique_checksummed_codes()
    {
        var client = _f.CreateClient();

        // authenticated, can sell, holds no roles → 403 on generate.
        // ⚠ perm:* gates resolve from RBAC, NOT the token's scope string, so the token must belong to
        // a user with no role assignment — asking for a scope it doesn't need would prove nothing.
        var cashier = PlutusAppFactory.OperatorTokenFor(Guid.NewGuid(), "pos.sell");
        await ExpectStatusAsync(client, HttpStatusCode.Forbidden, HttpMethod.Post,
            "/api/v1/giftcards/generate", cashier, new { count = 1 });

        var manager = PlutusAppFactory.OperatorTokenFor(await SeedManagerAsync(), "pos.sell");
        var created = await ExpectAsync(client, HttpStatusCode.Created, HttpMethod.Post,
            "/api/v1/giftcards/generate", manager, new { count = 5, batch = "Christmas 2026" });

        var codes = created.EnumerateArray().Select(c => c.GetProperty("code").GetString()).ToList();
        Assert.Equal(5, codes.Count);
        Assert.Equal(5, codes.Distinct().Count());
        foreach (var code in codes)
        {
            // every accepted spelling of a real code lands on the same canonical value
            Assert.Equal(code, GiftCardCodes.TryCanonicalise(code));
            Assert.Equal(code, GiftCardCodes.TryCanonicalise("G" + code));                    // barcode payload
            Assert.Equal(code, GiftCardCodes.TryCanonicalise(GiftCardCodes.Pretty(code)));    // hyphenated
            Assert.Equal(code, GiftCardCodes.TryCanonicalise(code.ToLowerInvariant()));       // hand-typed
        }

        // batch size is bounded — a generate call is a print run, not a migration
        await ExpectStatusAsync(client, HttpStatusCode.BadRequest, HttpMethod.Post,
            "/api/v1/giftcards/generate", manager, new { count = 5000 });
    }

    /// <summary>
    /// The money path end to end: an unsold card holds nothing and cannot be spent; activation loads
    /// it once and only once; partial redemption leaves the remainder; over-redeeming is refused; and
    /// a REPLAYED redemption (same entry id, as a till draining its outbox would send) is a no-op
    /// rather than a second spend.
    /// </summary>
    [Fact]
    public async Task Activate_redeem_partially_and_replays_cannot_spend_twice()
    {
        var client = _f.CreateClient();
        var manager = PlutusAppFactory.OperatorTokenFor(await SeedManagerAsync(), "pos.sell");
        var code = await GenerateOneAsync(client, manager);

        // unsold: worth nothing, and refused as a tender
        var unsold = await LookupAsync(client, manager, code);
        Assert.Equal("unsold", unsold.GetProperty("status").GetString());
        Assert.Equal(0, unsold.GetProperty("balancePence").GetInt64());
        await ExpectStatusAsync(client, HttpStatusCode.Conflict, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/redeem", manager, new { amountPence = 100 });

        // sell it for £20
        var activated = await ExpectAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/activate", manager, new { amountPence = 2000, saleId = Guid.NewGuid() });
        Assert.Equal(2000, activated.GetProperty("balancePence").GetInt64());

        // a second activation would create £20 out of nothing
        await ExpectStatusAsync(client, HttpStatusCode.Conflict, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/activate", manager, new { amountPence = 2000 });

        // spend £7.50 → £12.50 left
        var entryId = Guid.NewGuid();
        var first = await ExpectAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/redeem", manager, new { amountPence = 750, saleId = Guid.NewGuid(), entryId });
        Assert.Equal(1250, first.GetProperty("balancePence").GetInt64());

        // the till re-sends that redemption (outbox replay): idempotent, balance unmoved
        var replay = await ExpectAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/redeem", manager, new { amountPence = 750, saleId = Guid.NewGuid(), entryId });
        Assert.Equal(1250, replay.GetProperty("balancePence").GetInt64());

        // over-redeem refused, to the penny
        await ExpectStatusAsync(client, HttpStatusCode.Conflict, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/redeem", manager, new { amountPence = 1251 });

        // spend the remainder → spent, and nothing more can come off it
        await ExpectStatusAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/redeem", manager, new { amountPence = 1250 });
        var spent = await LookupAsync(client, manager, code);
        Assert.Equal("spent", spent.GetProperty("status").GetString());
        Assert.Equal(0, spent.GetProperty("balancePence").GetInt64());
        await ExpectStatusAsync(client, HttpStatusCode.Conflict, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/redeem", manager, new { amountPence = 1 });

        // the history explains every penny: +2000, −750, −1250
        var detail = await ExpectAsync(client, HttpStatusCode.OK, HttpMethod.Get, $"/api/v1/giftcards/{code}", manager);
        var amounts = detail.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("amountPence").GetInt64()).ToList();
        Assert.Equal(new List<long> { 2000, -750, -1250 }, amounts);
        Assert.Equal(0, amounts.Sum());
    }

    /// <summary>A voided card is dead money until reinstated; an expired one stays dead.</summary>
    [Fact]
    public async Task Voided_and_expired_cards_are_refused_as_tender()
    {
        var client = _f.CreateClient();
        var manager = PlutusAppFactory.OperatorTokenFor(await SeedManagerAsync(), "pos.sell");

        // ── voided ──
        var lost = await GenerateOneAsync(client, manager);
        await ExpectStatusAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{lost}/activate", manager, new { amountPence = 1000 });
        await ExpectStatusAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{lost}/void", manager, new { reason = "reported lost" });
        await ExpectStatusAsync(client, HttpStatusCode.Conflict, HttpMethod.Post,
            $"/api/v1/giftcards/{lost}/redeem", manager, new { amountPence = 100 });

        // ...and reinstating it brings the balance back — the ledger was never touched
        var reinstated = await ExpectAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{lost}/unvoid", manager);
        Assert.Equal("active", reinstated.GetProperty("status").GetString());
        Assert.Equal(1000, reinstated.GetProperty("balancePence").GetInt64());
        await ExpectStatusAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{lost}/redeem", manager, new { amountPence = 100 });

        // ── expired ── (back-date the expiry rather than waiting a year)
        var stale = await GenerateOneAsync(client, manager, expiresMonths: 12);
        await ExpectStatusAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{stale}/activate", manager, new { amountPence = 500 });
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "giftcard-e2e-expire";
            var card = await db.GiftCards.IgnoreQueryFilters().FirstAsync(c => c.Code == stale);
            card.ExpiresAtUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        await ExpectStatusAsync(client, HttpStatusCode.Conflict, HttpMethod.Post,
            $"/api/v1/giftcards/{stale}/redeem", manager, new { amountPence = 100 });
        Assert.Equal("expired", (await LookupAsync(client, manager, stale)).GetProperty("status").GetString());
    }

    /// <summary>
    /// ⚠ THE VAT TRAP. Selling a gift card is not a VAT-able supply — it takes a deposit against goods
    /// chosen later, and the VAT falls out of THOSE goods at their own bands when the card is spent.
    /// So the catalogue row an activation is rung through must sit on a zero-rate band: ring a card
    /// through 20% and the same money is VAT-ed twice, once on the card and again on the goods.
    /// </summary>
    [Fact]
    public async Task The_activation_item_is_provisioned_at_a_zero_vat_band_and_is_not_stock_tracked()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "giftcard-e2e-item";

        // a business with a realistic band set: 20%, 5% and exempt (Tax.Rate is a MULTIPLIER)
        var bizId = Guid.NewGuid();
        db.Business.Add(new Business { Id = bizId, Name = "Card E2E", NameAbbr = "CDE2E", VatIN = "GB0" });
        db.Taxes.Add(new Tax { IdOne = 1, IdTwo = bizId, Name = "20%", Rate = 1.2 });
        db.Taxes.Add(new Tax { IdOne = 2, IdTwo = bizId, Name = "5%", Rate = 1.05 });
        db.Taxes.Add(new Tax { IdOne = 3, IdTwo = bizId, Name = "Exempt", Rate = 1.0 });
        await db.SaveChangesAsync();

        Assert.True(await GiftCardSaleItem.EnsureAsync(db) >= 1);
        Assert.Equal(0, await GiftCardSaleItem.EnsureAsync(db));   // idempotent — safe on every boot

        var item = await db.Items.IgnoreQueryFilters()
            .FirstAsync(i => i.IdOne == GiftCardSaleItem.ItemIdOne && i.IdTwo == bizId);
        var band = await db.Taxes.IgnoreQueryFilters().FirstAsync(t => t.IdOne == item.TaxId && t.IdTwo == bizId);

        Assert.Equal(1.0, band.Rate);            // 1.0 = no VAT. NOT 1.2.
        Assert.True(item.StockUntracked);        // a card is not inventory
        Assert.Null(item.BinnedAtUtc);           // it must stay sellable
        Assert.Equal(0m, item.Price);            // the till sets the line price to the amount loaded
        Assert.False(string.IsNullOrWhiteSpace(item.Desc));   // [Required] — an empty string fails on save

        // its own category, so gift cards never inflate a product category's sales
        var cat = await db.Category.IgnoreQueryFilters().FirstAsync(c => c.IdOne == item.CatId);
        Assert.Equal(GiftCardSaleItem.CategoryName, cat.Name);
    }

    /// <summary>
    /// The liability number is what the accounts need: money taken for cards that have not yet been
    /// spent. Voided and expired cards are NOT liabilities (the shop won't honour them), and that must
    /// be decided by the same rule the redeem path enforces — or the report promises money the till
    /// refuses to give.
    /// </summary>
    [Fact]
    public async Task Outstanding_liability_counts_only_spendable_balances()
    {
        var client = _f.CreateClient();
        var manager = PlutusAppFactory.OperatorTokenFor(await SeedManagerAsync(), "pos.sell");

        async Task<long> OutstandingAsync() =>
            (await ExpectAsync(client, HttpStatusCode.OK, HttpMethod.Get, "/api/v1/giftcards/liability", manager))
                .GetProperty("outstandingPence").GetInt64();

        var before = await OutstandingAsync();

        var live = await GenerateOneAsync(client, manager);
        var partly = await GenerateOneAsync(client, manager);
        var killed = await GenerateOneAsync(client, manager);
        await GenerateOneAsync(client, manager);   // never sold — holds no money, so no liability

        foreach (var code in new[] { live, partly, killed })
            await ExpectStatusAsync(client, HttpStatusCode.OK, HttpMethod.Post,
                $"/api/v1/giftcards/{code}/activate", manager, new { amountPence = 1000 });

        await ExpectStatusAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{partly}/redeem", manager, new { amountPence = 400 });
        await ExpectStatusAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{killed}/void", manager, new { reason = "stolen" });

        // £10 live + £6 remaining = £16. The voided £10 is excluded; the unsold card contributes 0.
        Assert.Equal(before + 1600, await OutstandingAsync());
    }

    /// <summary>A card can be tied to a loyalty customer so "I've lost my card" is answerable from the
    /// customer record — and a manual balance correction is refused without a reason.</summary>
    [Fact]
    public async Task Cards_link_to_a_customer_and_manual_adjustments_demand_a_reason()
    {
        var client = _f.CreateClient();
        var manager = PlutusAppFactory.OperatorTokenFor(await SeedManagerAsync(), "pos.sell");
        var code = await GenerateOneAsync(client, manager);

        var customer = await ExpectAsync(client, HttpStatusCode.Created, HttpMethod.Post,
            "/api/v1/customers", manager, new { name = "Gift Recipient" });
        var customerId = customer.GetProperty("id").GetGuid();

        await ExpectStatusAsync(client, HttpStatusCode.NoContent, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/customer", manager, new { customerId });
        var linked = await ExpectAsync(client, HttpStatusCode.OK, HttpMethod.Get, $"/api/v1/giftcards/{code}", manager);
        Assert.Equal(customerId, linked.GetProperty("customerId").GetGuid());
        Assert.Equal("Gift Recipient", linked.GetProperty("customerName").GetString());

        // adjust: needs a SOLD card, a non-zero amount AND a reason (it moves money by hand)
        await ExpectStatusAsync(client, HttpStatusCode.BadRequest, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/adjust", manager, new { amountPence = 500, reason = "goodwill" });

        await ExpectStatusAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/activate", manager, new { amountPence = 1000 });
        await ExpectStatusAsync(client, HttpStatusCode.BadRequest, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/adjust", manager, new { amountPence = 500, reason = "" });
        await ExpectStatusAsync(client, HttpStatusCode.BadRequest, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/adjust", manager, new { amountPence = -5000, reason = "oops" });

        var adjusted = await ExpectAsync(client, HttpStatusCode.OK, HttpMethod.Post,
            $"/api/v1/giftcards/{code}/adjust", manager, new { amountPence = 500, reason = "goodwill top-up" });
        Assert.Equal(1500, adjusted.GetProperty("balancePence").GetInt64());
    }

    /// <summary>An unknown or mis-scanned code must not resolve to SOMEONE ELSE'S card — the check
    /// character exists to stop exactly that.</summary>
    [Fact]
    public async Task A_mis_scanned_code_is_rejected_rather_than_resolved()
    {
        var client = _f.CreateClient();
        var manager = PlutusAppFactory.OperatorTokenFor(await SeedManagerAsync(), "pos.sell");
        var code = await GenerateOneAsync(client, manager);

        // flip the last body character — the check character no longer verifies
        var body = code[..GiftCardCodes.BodyLength];
        var flipped = body[..^1] + (body[^1] == 'Z' ? '0' : 'Z') + code[^1];
        Assert.Null(GiftCardCodes.TryCanonicalise(flipped));

        await ExpectStatusAsync(client, HttpStatusCode.NotFound, HttpMethod.Get,
            $"/api/v1/giftcards/{flipped}/lookup", manager);
        await ExpectStatusAsync(client, HttpStatusCode.NotFound, HttpMethod.Get,
            "/api/v1/giftcards/NOTACARD123/lookup", manager);
    }
}
