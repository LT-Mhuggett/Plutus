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
}
