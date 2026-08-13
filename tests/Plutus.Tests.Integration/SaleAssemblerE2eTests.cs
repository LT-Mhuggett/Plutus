using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Identity;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// Cutover step 9, against the REAL ingest endpoint.
///
/// ⚠ WHY THIS EXISTS ON TOP OF THE UNIT TESTS. `SaleAssemblerTests` proves the assembler obeys the
/// rules as this repo understands them; it cannot prove the SERVER agrees. `SalesIngestService`
/// enforces reconcile invariants (header vs lines), VAT band plausibility, and its own idea of a
/// well-formed payload — and the assembler's whole reason to exist is producing something that
/// endpoint accepts. A payload that is internally consistent and rejected on arrival is still a
/// till that cannot sell.
///
/// This is the first time in the repo's history that a basket assembled by client code has been
/// posted to `/api/v1/sales`.
/// </summary>
public class SaleAssemblerE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public SaleAssemblerE2eTests(PlutusAppFactory f) => _f = f;

    private sealed class Credentials : IDeviceCredentialStore
    {
        public Guid? DeviceId { get; private set; }
        public string? ClientSecret { get; private set; }
        public void Save(Guid deviceId, string clientSecret) { DeviceId = deviceId; ClientSecret = clientSecret; }
        public void Clear() { DeviceId = null; ClientSecret = null; }
    }

    private static async Task<JsonElement> PostAsync(HttpClient c, string url, string token, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", token);
        var res = await c.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"POST {url} returned {(int)res.StatusCode}. Body: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<(PlutusApiClient Api, Guid DeviceId, Guid BusinessId)> EnrolAsync(
        HttpClient http, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        var tenant = await PostAsync(http, "/api/v1/tenants", admin,
            new { name = "Assemble " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" });
        var tenantId = tenant.GetProperty("tenantId").GetGuid();
        var storeId = tenant.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        var till = await PostAsync(http, "/api/v1/tills", portal, new { storeId, name = "Assemble till" });

        var bootstrap = new PlutusApiClient(http);
        var enrolled = await bootstrap.EnrolAsync(till.GetProperty("enrolmentCode").GetString()!);
        var creds = new Credentials();
        creds.Save(enrolled.DeviceId, enrolled.ClientSecret);
        var api = new PlutusApiClient(http, new DeviceTokenProvider(bootstrap, creds));

        // ⚠ The LEGACY business id, from the server — not the tenant id. Item ids derive from it.
        var businessId = (await api.GetStoreInfoAsync(storeId))!.BusinessId!.Value;
        return (api, enrolled.DeviceId, businessId);
    }

    /// <summary>
    /// ⚠ THE ONE THAT MATTERS: a mixed-rate basket, assembled by client code, ACCEPTED by the real
    /// endpoint. Mixed rates are where a header recomputed independently of its lines diverges
    /// first, and the reconcile invariant is what rejects it.
    /// </summary>
    [Fact]
    public async Task A_basket_assembled_by_the_client_is_accepted_by_the_real_ingest_endpoint()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "assemble1@acme.test");

        var lines = new[]
        {
            new BasketLine(Guid.Empty, "5010001", "Comic", 1499, 1249, 1),
            new BasketLine(Guid.Empty, "5010002", "Newspaper", 250, 250, 2),          // zero-rated
            new BasketLine(Guid.Empty, "5010003", "Mug", 999, 832, 3, DiscountPence: 150),
        };

        var totals = SaleAssembler.Total(lines);
        var sale = SaleAssembler.Assemble(
            Uuid7.New(), deviceId, 1, businessId, lines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = totals.GrossPence } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);

        var (status, body) = await api.PostSaleAsync(
            JsonSerializer.Serialize(sale, PlutusApiClient.Json));

        // ⚠ 201 Recorded — not 202. A 202 means QUARANTINED: the server took it but could not
        // explain it, which for this test is a failure dressed as a success.
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("recorded", body?.Status?.ToLowerInvariant());

        // and the header the server accepted really is the sum of the lines
        Assert.Equal(sale.Lines.Sum(l => l.LineGrossPence), sale.GrossPence);
        Assert.Equal(sale.Lines.Sum(l => l.VatAmountPence), sale.VatPence);
    }

    /// <summary>
    /// A return assembled by the client is accepted too, with every money figure negative.
    /// ⚠ Posted as its own sale referencing the original — the shape the platform expects — rather
    /// than as an edit of the original, which is what a legacy till would have done.
    /// </summary>
    [Fact]
    public async Task A_return_assembled_by_the_client_is_accepted_and_is_negative()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "assemble2@acme.test");

        var originalId = Uuid7.New();
        var sold = new[] { new BasketLine(Guid.Empty, "5020001", "Comic", 1499, 1249, 1) };
        var original = SaleAssembler.Assemble(
            originalId, deviceId, 1, businessId, sold,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = 1499 } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Created,
            (await api.PostSaleAsync(JsonSerializer.Serialize(original, PlutusApiClient.Json))).Status);

        var returned = new[]
        {
            new BasketLine(Guid.Empty, "5020001", "Comic", 1499, 1249, 1, IsReturn: true, OriginSaleId: originalId),
        };
        var refund = SaleAssembler.Assemble(
            Uuid7.New(), deviceId, 2, businessId, returned,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = -1499 } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);

        Assert.True(refund.GrossPence < 0);
        Assert.True(refund.VatPence < 0);

        var (status, body) = await api.PostSaleAsync(JsonSerializer.Serialize(refund, PlutusApiClient.Json));
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("recorded", body?.Status?.ToLowerInvariant());
    }

    // ── cutover step 17: the server-side refund cap ──────────────────────────────────────────

    /// <summary>Post a sale of one line and return its id.</summary>
    private static async Task<Guid> SellAsync(
        PlutusApiClient api, Guid deviceId, Guid businessId, long seq, string idOne, long incPence, long exPence, int qty)
    {
        var id = Uuid7.New();
        var lines = new[] { new BasketLine(Guid.Empty, idOne, "Item " + idOne, incPence, exPence, qty) };
        var sale = SaleAssembler.Assemble(
            id, deviceId, seq, businessId, lines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = incPence * qty } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.Created,
            (await api.PostSaleAsync(JsonSerializer.Serialize(sale, PlutusApiClient.Json))).Status);
        return id;
    }

    private static async Task<(HttpStatusCode Status, string Body)> RefundAsync(
        PlutusApiClient api, Guid deviceId, Guid businessId, long seq,
        Guid originId, string idOne, long incPence, long exPence, int qty)
    {
        var lines = new[]
        {
            new BasketLine(Guid.Empty, idOne, "Item " + idOne, incPence, exPence, qty,
                IsReturn: true, OriginSaleId: originId),
        };
        var totals = SaleAssembler.Total(lines);
        var refund = SaleAssembler.Assemble(
            Uuid7.New(), deviceId, seq, businessId, lines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = totals.GrossPence } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);

        var (status, body) = await api.PostSaleAsync(JsonSerializer.Serialize(refund, PlutusApiClient.Json));
        return (status, body?.Status?.ToLowerInvariant() ?? "");
    }

    /// <summary>
    /// An operator who may read a sale's detail — the endpoint is gated on `perm:pos.refund` (among
    /// others), which a DEVICE token can never satisfy (WP4's two-token rule).
    /// </summary>
    private async Task<(Guid OperatorId, Guid TenantId)> SeedRefundingOperatorAsync(Guid saleId)
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();

        // ⚠⚠ AN UNSCOPED CONTEXT, and worth reading before copying this: the DI one falls back to Kapow,
        // and `StampAndGuardTenant` then refuses to write a role for the tenant this test just created —
        // *"Cross-tenant write blocked: RbacRole.TenantId … != context …"* (runbook pitfall 3).
        // `DrawerVarianceE2eTests.OwnerTokenAsync` hit the same wall and solved it the same way;
        // `Guid.Empty` is the deliberate cross-tenant bypass for tooling like this, and it also lets the
        // sale lookup below see a row the tenant filter would otherwise hide.
        var db = new MySqlDbContext(
            scope.ServiceProvider.GetRequiredService<
                Microsoft.EntityFrameworkCore.DbContextOptions<MySqlDbContext>>(),
            new Plutus.Entities.Tenancy.FixedTenantContext(Guid.Empty));

        db.CurrentUser = "refund-split-e2e-seed";

        // ⚠ THE SALE'S OWN TENANT, not Kapow. Every test in this class creates a fresh tenant, and an
        // assignment on the wrong one reads exactly like "the platform sends no tenders" — which is how
        // the first version of this test lied to me.
        // ⚠ `.AsQueryable()` first — a DbSet from the EF 3.1-era model is ambiguous between
        // `IQueryable` and `IAsyncEnumerable` on .NET 10 (repo-runbook pitfall).
        var tenant = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(
            db.SalesV2.AsQueryable().Where(s => s.Id == saleId).Select(s => s.TenantId));

        await RbacSeeder.EnsureBuiltInRolesAsync(db, tenant);

        // Store Manager holds the till permissions including pos.refund.
        var role = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstAsync(db.RbacRoles, r => r.Name == "Store Manager" && r.TenantId == tenant);

        db.RbacRoleAssignments.Add(new Plutus.Entities.Models.RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = tenant, UserId = userId, RoleId = role.Id,
            ScopeType = Plutus.Entities.Models.RbacScopeType.Tenant, ScopeId = "",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (userId, tenant);
    }

    /// <summary>Read `GET /api/v1/sales/{id}` as an operator, through the real contract type.</summary>
    private static async Task<SaleDto?> GetSaleAsDtoAsync(HttpClient http, Guid operatorId, Guid tenantId, Guid saleId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/sales/{saleId:D}");
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorTokenFor(operatorId, "pos.sell", tenantId));

        var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;

        return JsonSerializer.Deserialize<SaleDto>(
            await resp.Content.ReadAsStringAsync(), PlutusApiClient.Json);
    }

    /// <summary>Sell one line, paid with the tenders given — so a test can make a SPLIT-paid sale.</summary>
    private static async Task<Guid> SellPaidWithAsync(
        PlutusApiClient api, Guid deviceId, Guid businessId, long seq, string idOne,
        long incPence, long exPence, int qty, params IngestTender[] tenders)
    {
        var id = Uuid7.New();
        var lines = new[] { new BasketLine(Guid.Empty, idOne, "Item " + idOne, incPence, exPence, qty) };
        var sale = SaleAssembler.Assemble(
            id, deviceId, seq, businessId, lines, tenders, new DateOnly(2026, 8, 9), DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.Created,
            (await api.PostSaleAsync(JsonSerializer.Serialize(sale, PlutusApiClient.Json))).Status);
        return id;
    }

    /// <summary>Refund a line, putting the money back on the tenders given.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> RefundToTendersAsync(
        PlutusApiClient api, Guid deviceId, Guid businessId, long seq,
        Guid originId, string idOne, long incPence, long exPence, int qty, params IngestTender[] tenders)
    {
        var lines = new[]
        {
            new BasketLine(Guid.Empty, idOne, "Item " + idOne, incPence, exPence, qty,
                IsReturn: true, OriginSaleId: originId),
        };
        var refund = SaleAssembler.Assemble(
            Uuid7.New(), deviceId, seq, businessId, lines, tenders,
            new DateOnly(2026, 8, 9), DateTime.UtcNow);

        var (status, body) = await api.PostSaleAsync(JsonSerializer.Serialize(refund, PlutusApiClient.Json));
        return (status, body?.Status?.ToLowerInvariant() ?? "");
    }

    /// <summary>
    /// ⚠⚠ FINDING Y (Matt, 2026-08-13): *"when I try to return an item that was split, it wants to put
    /// the full amount to that card. It needs to be aware of how the payments were split."*
    ///
    /// A £4.40 sale paid £2.00 cash + £2.40 card, refunded £4.40 ENTIRELY TO THE CARD. Every existing
    /// gate waves it through: the sale-level cap sees £4.40 of a £4.40 sale, the per-item cap sees one
    /// item fully returned, and neither has any notion of a tender. The result is a card credited £2.40
    /// more than it ever took and £2.00 still in the drawer — a till that balances, and a shop £2 down
    /// with nothing in any report to show it.
    ///
    /// ⚠ This is the SERVER half. The tills are being fixed too, but a cap that only a till enforces is
    /// no cap at all — that is binding default 12's whole reasoning, applied to the split.
    /// </summary>
    [Fact]
    public async Task A_split_paid_sale_cannot_be_refunded_entirely_to_one_tender()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refundsplit1@acme.test");

        // £4.40, paid £2.00 cash + £2.40 card
        var originId = await SellPaidWithAsync(api, deviceId, businessId, 1, "5040001", 440, 367, 1,
            new IngestTender { TenderType = Tenders.Cash, AmountPence = 200 },
            new IngestTender { TenderType = Tenders.Card, AmountPence = 240 });

        // …refunded £4.40 to the card alone
        var (status, body) = await RefundToTendersAsync(api, deviceId, businessId, 2, originId,
            "5040001", 440, 367, 1,
            new IngestTender { TenderType = Tenders.Card, AmountPence = -440 });

        Assert.Equal(HttpStatusCode.Accepted, status);   // 202
        Assert.Equal("quarantined", body);
    }

    /// <summary>
    /// ⚠⚠ FINDING Y PIECE 4b: the platform tells a till HOW a sale was paid, and the contract now reads
    /// it. `GET /api/v1/sales/{saleId}` has projected `tenders` since it was written and `SaleDto` had no
    /// property for them — so a till refunding a sale rung up on ANOTHER till could not tell £2.00 cash
    /// + £2.40 card from £4.40 on a card, and offered the whole refund wherever the operator tapped.
    /// **The data was there and nobody asked for it.**
    ///
    /// ⚠ This is an END-TO-END assertion on purpose: the names are the `TenderType` enum's
    /// (`"Cash"`, `"GiftCard"`), not the wire bytes, so a unit test against a hand-written DTO would
    /// have proved nothing about what the server actually sends.
    /// </summary>
    [Fact]
    public async Task The_platform_tells_a_till_how_a_sale_was_paid()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refundsplit4@acme.test");

        var saleId = await SellPaidWithAsync(api, deviceId, businessId, 1, "5040004", 440, 367, 1,
            new IngestTender { TenderType = Tenders.Cash, AmountPence = 200 },
            new IngestTender { TenderType = Tenders.Card, AmountPence = 240 });

        // ⚠ THE SALE-DETAIL ENDPOINT IS `perm:*` GATED, so a DEVICE token cannot read it — WP4's
        // two-token rule, and the reason this test seeds an operator with a role rather than reusing
        // the enrolment client. Getting this wrong reads as "the platform sends no tenders".
        var (operatorId, tenantId) = await SeedRefundingOperatorAsync(saleId);
        var dto = await GetSaleAsDtoAsync(http, operatorId, tenantId, saleId);
        Assert.NotNull(dto);

        // Straight into the refund rule, which is the only reason the till wants them.
        var capacities = RefundRules.RefundCapacities(SaleDtoTenders.TenderPairs(dto));

        Assert.Equal(2, capacities.Count);
        Assert.Equal(200, capacities.Single(c => c.TenderType == Tenders.Cash).RemainingPence);
        Assert.Equal(240, capacities.Single(c => c.TenderType == Tenders.Card).RemainingPence);

        // …and the rule then refuses the whole £4.40 on the card, at the till, before any money moves.
        Assert.False(RefundRules.AuthoriseSplit(capacities,
            new[] { new KeyValuePair<byte, long>(Tenders.Card, 440) }).IsAllowed);
    }

    /// <summary>The same refund, split the way the customer actually paid, is recorded.</summary>
    [Fact]
    public async Task A_split_paid_sale_refunded_the_way_it_was_paid_is_recorded()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refundsplit2@acme.test");

        var originId = await SellPaidWithAsync(api, deviceId, businessId, 1, "5040002", 440, 367, 1,
            new IngestTender { TenderType = Tenders.Cash, AmountPence = 200 },
            new IngestTender { TenderType = Tenders.Card, AmountPence = 240 });

        var (status, _) = await RefundToTendersAsync(api, deviceId, businessId, 2, originId,
            "5040002", 440, 367, 1,
            new IngestTender { TenderType = Tenders.Cash, AmountPence = -200 },
            new IngestTender { TenderType = Tenders.Card, AmountPence = -240 });

        Assert.Equal(HttpStatusCode.Created, status);
    }

    /// <summary>
    /// ⚠ THE FRAUD FINDING G EXISTS TO STOP, now refused by the SERVER as well as by MAUI's action
    /// sheet: a card sale refunded out of the cash drawer. A day of card sales refunded in cash empties
    /// the drawer and leaves the card takings untouched.
    /// </summary>
    [Fact]
    public async Task A_card_sale_cannot_be_refunded_in_cash()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refundsplit3@acme.test");

        var originId = await SellPaidWithAsync(api, deviceId, businessId, 1, "5040003", 440, 367, 1,
            new IngestTender { TenderType = Tenders.Card, AmountPence = 440 });

        var (status, body) = await RefundToTendersAsync(api, deviceId, businessId, 2, originId,
            "5040003", 440, 367, 1,
            new IngestTender { TenderType = Tenders.Cash, AmountPence = -440 });

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal("quarantined", body);
    }

    /// <summary>
    /// ⚠ THE ONE THAT MATTERS — Matt's binding default 12: *"You should not be able to refund MORE
    /// than the price paid for it."* Two part-refunds inside the total are fine; the one that tips
    /// past what was taken is QUARANTINED, not recorded. Each refund looks perfectly reasonable on
    /// its own — only the running total says otherwise, and only the server can see it, because a
    /// till knows what IT refunded and never what another till did.
    /// </summary>
    [Fact]
    public async Task Part_refunds_are_recorded_until_they_exceed_what_was_paid_then_quarantined()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refundcap1@acme.test");

        // sold: 3 × £10 = £30
        var originId = await SellAsync(api, deviceId, businessId, 1, "5030001", 1000, 833, qty: 3);

        // £10 back, then another £10 — both legitimate, both inside the total
        Assert.Equal(HttpStatusCode.Created,
            (await RefundAsync(api, deviceId, businessId, 2, originId, "5030001", 1000, 833, 1)).Status);
        Assert.Equal(HttpStatusCode.Created,
            (await RefundAsync(api, deviceId, businessId, 3, originId, "5030001", 1000, 833, 1)).Status);

        // £20 more would make £40 out of a £30 sale
        var (status, body) = await RefundAsync(api, deviceId, businessId, 4, originId, "5030001", 1000, 833, 2);

        Assert.Equal(HttpStatusCode.Accepted, status);   // 202
        Assert.Equal("quarantined", body);
    }

    /// <summary>
    /// ⚠ THE PER-LINE HALF, which is the gap the till's own cap cannot close. Its local history is
    /// per ORIGIN SALE, so refunding one £10 line three times inside a £210 sale passes a
    /// sale-level test comfortably — and that is the actual shape of refund fraud.
    /// </summary>
    [Fact]
    public async Task One_line_cannot_be_refunded_repeatedly_inside_a_larger_sale_total()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refundcap2@acme.test");

        // ⚠ TWO ITEMS, and the cheap one is what gets refunded twice. A £10 comic alongside a £200
        // console: giving the comic back twice is £20 against a £210 sale, which the SALE-level cap
        // waves through without blinking. Only the per-ITEM cap can see it — and this test fails if
        // that half is removed, which the single-item version of it did not.
        var originId = Uuid7.New();
        var sold = new[]
        {
            new BasketLine(Guid.Empty, "5030002", "Comic", 1000, 833, 1),
            new BasketLine(Guid.Empty, "5030012", "Console", 20000, 16667, 1),
        };
        var totals = SaleAssembler.Total(sold);
        var sale = SaleAssembler.Assemble(
            originId, deviceId, 1, businessId, sold,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = totals.GrossPence } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Created,
            (await api.PostSaleAsync(JsonSerializer.Serialize(sale, PlutusApiClient.Json))).Status);

        // the comic comes back — entirely legitimate
        Assert.Equal(HttpStatusCode.Created,
            (await RefundAsync(api, deviceId, businessId, 2, originId, "5030002", 1000, 833, 1)).Status);

        // and again. £20 of £210 is nothing at sale level; the comic itself is now double-refunded.
        var (status, body) = await RefundAsync(api, deviceId, businessId, 3, originId, "5030002", 1000, 833, 1);

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal("quarantined", body);
    }

    /// <summary>An item that was never on the sale cannot be returned against it, however small
    /// the amount — the sale-level total would happily absorb it.</summary>
    [Fact]
    public async Task An_item_that_was_not_on_the_sale_cannot_be_refunded_against_it()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refundcap3@acme.test");

        var originId = await SellAsync(api, deviceId, businessId, 1, "5030003", 5000, 4167, qty: 1);

        var (status, body) = await RefundAsync(api, deviceId, businessId, 2, originId, "5030099", 100, 83, 1);

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal("quarantined", body);
    }

    /// <summary>
    /// ⚠ A refund whose ORIGIN the platform has never seen is ACCEPTED, deliberately. Quarantine is
    /// terminal — the outbox never retries a 202 — and a refund can legitimately arrive before the
    /// sale it refunds, because another till's outbox drains on its own schedule. Destroying a real
    /// refund to guard against an unverifiable one is the worse trade.
    /// </summary>
    [Fact]
    public async Task A_refund_against_a_sale_the_platform_has_not_seen_yet_is_still_accepted()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refundcap4@acme.test");

        var neverIngested = Uuid7.New();
        var (status, body) = await RefundAsync(api, deviceId, businessId, 1, neverIngested, "5030004", 1000, 833, 1);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("recorded", body);
    }

    /// <summary>⚠ Re-posting the SAME refund must not count itself as prior history and tip the
    /// sale over its own cap — an idempotent retry is exactly what the outbox does after a
    /// timeout.</summary>
    [Fact]
    public async Task Re_posting_the_same_refund_stays_idempotent_rather_than_becoming_an_over_refund()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refundcap5@acme.test");

        var originId = await SellAsync(api, deviceId, businessId, 1, "5030005", 1000, 833, qty: 1);

        var lines = new[]
        {
            new BasketLine(Guid.Empty, "5030005", "Item", 1000, 833, 1, IsReturn: true, OriginSaleId: originId),
        };
        var totals = SaleAssembler.Total(lines);
        var refund = SaleAssembler.Assemble(
            Uuid7.New(), deviceId, 2, businessId, lines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = totals.GrossPence } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);
        var payload = JsonSerializer.Serialize(refund, PlutusApiClient.Json);

        Assert.Equal(HttpStatusCode.Created, (await api.PostSaleAsync(payload)).Status);

        // the same payload again — a duplicate, never a second refund
        var (status, body) = await api.PostSaleAsync(payload);
        Assert.Equal(HttpStatusCode.OK, status);                     // 200 duplicate
        Assert.NotEqual("quarantined", body?.Status?.ToLowerInvariant());
    }

    /// <summary>
    /// ⚠ A REFUND IS NOT SOMETHING YOU CAN REFUND — the gate that does not depend on the till.
    ///
    /// On 2026-08-10 £13.99 left the drawer twice on a £13.99 sale. A refund is stored as its own
    /// sale with a NEGATIVE gross, the till offered it in a "which sale?" picker, and the operator
    /// tapped the newest entry. The server's cap took `Math.Abs(origin.GrossPence)`, so the refund
    /// looked exactly like a £13.99 sale with nothing yet returned against it, and waved it through.
    ///
    /// The till's picker was fixed the same day. This is the half that survives a till being wrong,
    /// which is the entire reason the cap is enforced in two places (binding default 12).
    /// </summary>
    [Fact]
    public async Task A_refund_cannot_itself_be_refunded()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "refund-a-refund@acme.test");

        var originId = await SellAsync(api, deviceId, businessId, 1, "5040001", 1399, 1166, 1);

        // a legitimate full refund of it
        var refundId = Uuid7.New();
        var refundLines = new[]
        {
            new BasketLine(Guid.Empty, "5040001", "Item", 1399, 1166, 1, IsReturn: true, OriginSaleId: originId),
        };
        var refund = SaleAssembler.Assemble(
            refundId, deviceId, 2, businessId, refundLines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = SaleAssembler.Total(refundLines).GrossPence } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Created,
            (await api.PostSaleAsync(JsonSerializer.Serialize(refund, PlutusApiClient.Json))).Status);

        // now try to refund THE REFUND — exactly what the picker let an operator do
        var secondLines = new[]
        {
            new BasketLine(Guid.Empty, "5040001", "Item", 1399, 1166, 1, IsReturn: true, OriginSaleId: refundId),
        };
        var second = SaleAssembler.Assemble(
            Uuid7.New(), deviceId, 3, businessId, secondLines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = SaleAssembler.Total(secondLines).GrossPence } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);

        var (status, body) = await api.PostSaleAsync(JsonSerializer.Serialize(second, PlutusApiClient.Json));

        // ⚠ 202 QUARANTINED, not 400. The money may already have left a drawer on a till that broke
        // the rule; a 400 makes the evidence vanish into that till's Failed queue, quarantine keeps
        // it where the portal can see it.
        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal("quarantined", body?.Status?.ToLowerInvariant());
    }
}
