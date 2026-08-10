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

    // ── reading a sale back: the endpoint cross-till refunds need (2026-08-10) ──
    //
    // ⚠ `GET /api/v1/sales/{saleId}` DID NOT EXIST. `PlutusApiClient.GetSaleAsync` has targeted it
    // since it was written; `ReturnLookup.TryServerAsync` calls it and — by design — swallows the
    // failure and falls back to this till's own record. So refunding goods bought at ANOTHER branch
    // silently became "we have no record of that sale", on a platform holding the sale all along.
    // Nothing errored. No test anywhere called `GetSaleAsync`, and the client contract was fully
    // specified, so everything read as built.

    /// <summary>
    /// ⚠ THROUGH THE REAL CLIENT, not a hand-rolled request. That is the whole value of this test:
    /// the server declares its own twin of `SaleDto` (the contracts project ships onto tills and so
    /// has no references), and deserialising the server's JSON into the CLIENT's record is the only
    /// thing that actually pins the two shapes together. A field renamed on either side fails here.
    /// </summary>
    [Fact]
    public async Task A_committed_sale_can_be_READ_BACK_through_the_client_contract()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "readback@acme.test");

        var lines = new[]
        {
            new BasketLine(Guid.Empty, "5010001", "Comic", 1499, 1249, 1),
            new BasketLine(Guid.Empty, "5010002", "Newspaper", 250, 250, 2),   // zero-rated
        };
        var totals = SaleAssembler.Total(lines);
        var saleId = Uuid7.New();
        var sale = SaleAssembler.Assemble(
            saleId, deviceId, 1, businessId, lines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = totals.GrossPence } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.Created,
            (await api.PostSaleAsync(JsonSerializer.Serialize(sale, PlutusApiClient.Json))).Status);

        var read = await api.GetSaleAsync(saleId);

        Assert.NotNull(read);
        Assert.Equal(saleId, read!.Id);
        Assert.Equal(totals.GrossPence, read.GrossPence);
        Assert.Equal("2026-08-09", read.BusinessDay);
        Assert.Equal(2, read.Lines.Count);

        // ⚠ The EX-VAT unit price must survive the round trip inside the line meta. The client reads
        // it from there rather than re-deriving one from the rate — re-running VAT arithmetic the
        // sale already settled disagrees by a penny on some lines, on a refund, against a receipt
        // the customer is holding.
        var comic = read.Lines.Single(l => l.ItemIdOne == "5010001");
        Assert.Equal(1499, comic.UnitPricePence);
        Assert.Equal(1249, comic.UnitExPence);

        // Nothing has been given back yet.
        Assert.Equal(0, read.AlreadyRefundedPence);
    }

    /// <summary>
    /// ⚠ THE SERVER'S HALF OF THE REFUND CAP. A till knows only what IT has refunded; this is how
    /// it learns what another counter already gave back. Without it the cap is decided from one
    /// till's memory and a customer only has to walk to a different till (binding default 12).
    /// </summary>
    [Fact]
    public async Task Reading_a_sale_back_reports_what_has_ALREADY_been_refunded()
    {
        var http = _f.CreateClient();
        var (api, deviceId, businessId) = await EnrolAsync(http, "readback-refunded@acme.test");

        var originId = Uuid7.New();
        var original = SaleAssembler.Assemble(
            originId, deviceId, 1, businessId,
            new[] { new BasketLine(Guid.Empty, "5030005", "Item", 1000, 833, 2) },
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = 2000 } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Created,
            (await api.PostSaleAsync(JsonSerializer.Serialize(original, PlutusApiClient.Json))).Status);

        // one of the two given back, on this or any other till
        var refundLines = new[]
        {
            new BasketLine(Guid.Empty, "5030005", "Item", 1000, 833, 1, IsReturn: true, OriginSaleId: originId),
        };
        var refund = SaleAssembler.Assemble(
            Uuid7.New(), deviceId, 2, businessId, refundLines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = SaleAssembler.Total(refundLines).GrossPence } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Created,
            (await api.PostSaleAsync(JsonSerializer.Serialize(refund, PlutusApiClient.Json))).Status);

        var read = await api.GetSaleAsync(originId);

        Assert.NotNull(read);
        // ⚠ POSITIVE, however the adjustment was signed — the client sums magnitudes.
        Assert.Equal(1000, read!.AlreadyRefundedPence);
        Assert.Single(read.Adjustments);
    }

    /// <summary>⚠ 404, never an empty sale. A till that cannot tell "no such sale" from "a sale
    /// with nothing on it" refuses a legitimate refund and blames the customer's receipt.</summary>
    [Fact]
    public async Task An_unknown_sale_id_is_a_404_rather_than_an_empty_sale()
    {
        var http = _f.CreateClient();
        var (api, _, _) = await EnrolAsync(http, "readback-missing@acme.test");

        Assert.Null(await api.GetSaleAsync(Uuid7.New()));
    }
}
