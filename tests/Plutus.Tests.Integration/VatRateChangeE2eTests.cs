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
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// MAUI retrofit WP2b — Definition of Done, end to end through the real ingest endpoint.
///
/// The scenario: a shop's till loses its connection, keeps trading through a government VAT-rate
/// change, and reconnects days later with a queue of sales computed at the old rate. Those must be
/// caught — but the sales it made legitimately BEFORE the change, at that same old rate, must sail
/// through. Getting that second half wrong is what makes people ignore a quarantine queue.
/// </summary>
public class VatRateChangeE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public VatRateChangeE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly DateTime Change = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private async Task<(string DeviceToken, Guid TenantId)> ProvisionEnrolAndSeedRatesAsync(HttpClient c, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using var pReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        { Content = JsonContent.Create(new { name = "Vat " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" }) };
        pReq.Headers.Authorization = new("Bearer", admin);
        var pRes = await c.SendAsync(pReq);
        var pBody = JsonDocument.Parse(await pRes.Content.ReadAsStringAsync()).RootElement;
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        using var tReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills")
        { Content = JsonContent.Create(new { storeId, name = "Vat till" }) };
        tReq.Headers.Authorization = new("Bearer", portal);
        var tBody = JsonDocument.Parse(await (await c.SendAsync(tReq)).Content.ReadAsStringAsync()).RootElement;
        var code = tBody.GetProperty("enrolmentCode").GetString();

        using var eReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills/enrol")
        { Content = JsonContent.Create(new { enrolmentCode = code }) };
        var eBody = JsonDocument.Parse(await (await c.SendAsync(eReq)).Content.ReadAsStringAsync()).RootElement;
        var deviceId = eBody.GetProperty("deviceId").GetGuid();
        var secret = eBody.GetProperty("clientSecret").GetString();

        using var kReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tokens/device")
        { Content = JsonContent.Create(new { deviceId, clientSecret = secret }) };
        var kBody = JsonDocument.Parse(await (await c.SendAsync(kReq)).Content.ReadAsStringAsync()).RootElement;

        // Seed this tenant's bands: zero/reduced from epoch, standard 20% → 17.5% on 1 Sept.
        // ⚠ Needs an UNSCOPED context (runbook pitfall #3) — StampAndGuardTenant refuses writes
        // for a tenant other than the ambient one, and this test provisions a fresh tenant.
        using (var scope = _f.Services.CreateScope())
        {
            var db = new MySqlDbContext(
                scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
                new Plutus.Entities.Tenancy.FixedTenantContext(Guid.Empty));
            db.CurrentUser = "vat-e2e-seed";
            foreach (var p in new[]
            {
                new Plutus.Entities.Models.VatRatePoint { Band = VatRateHistory.Zero, RateBp = 0, EffectiveFromUtc = DateTime.UnixEpoch },
                new Plutus.Entities.Models.VatRatePoint { Band = VatRateHistory.Reduced, RateBp = 500, EffectiveFromUtc = DateTime.UnixEpoch },
                new Plutus.Entities.Models.VatRatePoint { Band = VatRateHistory.Standard, RateBp = 2000, EffectiveFromUtc = DateTime.UnixEpoch },
                new Plutus.Entities.Models.VatRatePoint { Band = VatRateHistory.Standard, RateBp = 1750, EffectiveFromUtc = Change },
            })
            {
                p.Id = Uuid7.New();
                p.TenantId = tenantId;
                db.VatRatePoints.Add(p);
            }
            await db.SaveChangesAsync();
        }

        return (kBody.GetProperty("accessToken").GetString()!, tenantId);
    }

    /// <summary>A £6.00 sale at the given rate and moment. Gross/VAT are consistent so the T1.3
    /// arithmetic invariants pass and the ONLY thing under test is the rate's legality.</summary>
    private static object Sale(long seq, int rateBp, DateTime occurredAtUtc, string itemIdOne = "5010000000001")
    {
        var gross = 600L;
        var vat = (long)Math.Round(gross * rateBp / (10000.0 + rateBp), MidpointRounding.AwayFromZero);
        return new
        {
            saleId = Uuid7.New(),
            deviceSeq = seq,
            channel = 0,
            businessDay = DateOnly.FromDateTime(occurredAtUtc).ToString("yyyy-MM-dd"),
            occurredAtUtc,
            grossPence = gross,
            vatPence = vat,
            lines = new[]
            {
                new
                {
                    itemId = Guid.NewGuid(), qty = 1, unitPricePence = gross, discountPence = 0L,
                    lineGrossPence = gross, vatRateBp = rateBp, vatAmountPence = vat,
                    discountsJson = "{\"itemIdOne\":\"" + itemIdOne + "\"}",
                },
            },
            tenders = new[] { new { tenderType = 0, amountPence = gross, changePence = 0L } },
        };
    }

    private async Task<(HttpStatusCode Status, string Body)> PostSale(HttpClient c, object sale, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sales") { Content = JsonContent.Create(sale) };
        req.Headers.Authorization = new("Bearer", token);
        var res = await c.SendAsync(req);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_stale_rate_after_the_change_is_quarantined_but_correct_sales_both_sides_are_not()
    {
        var c = _f.CreateClient();
        var (token, tenantId) = await ProvisionEnrolAndSeedRatesAsync(c, "vat1@acme.test");

        // 1. THE CASE THIS EXISTS FOR: offline across the change, pushed at the old 20% → quarantined
        var (staleStatus, staleBody) = await PostSale(c, Sale(1, 2000, Change.AddDays(2)), token);
        Assert.Equal(HttpStatusCode.Accepted, staleStatus);
        Assert.Contains("quarantined", staleBody);

        // 2. the same moment at the CORRECT new rate → recorded normally
        var (goodStatus, _) = await PostSale(c, Sale(2, 1750, Change.AddDays(2)), token);
        Assert.Equal(HttpStatusCode.Created, goodStatus);

        // 3. NO FALSE POSITIVES: a sale from BEFORE the change, at the then-correct 20% → recorded.
        //    A till draining a backlog must not have its legitimate history quarantined.
        var (pastStatus, _) = await PostSale(c, Sale(3, 2000, Change.AddDays(-3)), token);
        Assert.Equal(HttpStatusCode.Created, pastStatus);

        // 4. coexisting bands are untouched — a zero-rated book sells fine after the change
        var (zeroStatus, _) = await PostSale(c, Sale(4, 0, Change.AddDays(2)), token);
        Assert.Equal(HttpStatusCode.Created, zeroStatus);

        // the quarantine row explains itself rather than saying "invalid"
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var q = await db.SaleQuarantine.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).FirstAsync();
        Assert.Contains("not in force", q.Reason);
        Assert.Contains("offline", q.Reason);
    }

    [Fact]
    public async Task A_tenant_with_no_configured_rates_is_skipped_not_quarantined()
    {
        // Turning a compliance guard on must never become an outage for unseeded tenants.
        var c = _f.CreateClient();
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using var pReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        { Content = JsonContent.Create(new { name = "Unseeded", plan = "standard", adminEmail = "vat2@acme.test", adminPassword = "S3cret!" }) };
        pReq.Headers.Authorization = new("Bearer", admin);
        var pBody = JsonDocument.Parse(await (await c.SendAsync(pReq)).Content.ReadAsStringAsync()).RootElement;
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        using var tReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills")
        { Content = JsonContent.Create(new { storeId, name = "Unseeded till" }) };
        tReq.Headers.Authorization = new("Bearer", portal);
        var tBody = JsonDocument.Parse(await (await c.SendAsync(tReq)).Content.ReadAsStringAsync()).RootElement;

        using var eReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills/enrol")
        { Content = JsonContent.Create(new { enrolmentCode = tBody.GetProperty("enrolmentCode").GetString() }) };
        var eBody = JsonDocument.Parse(await (await c.SendAsync(eReq)).Content.ReadAsStringAsync()).RootElement;

        using var kReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tokens/device")
        { Content = JsonContent.Create(new { deviceId = eBody.GetProperty("deviceId").GetGuid(), clientSecret = eBody.GetProperty("clientSecret").GetString() }) };
        var token = JsonDocument.Parse(await (await c.SendAsync(kReq)).Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;

        // a rate that exists in NO history at all — still recorded, because this tenant has none
        var (status, _) = await PostSale(c, Sale(1, 1234, DateTime.UtcNow), token);
        Assert.Equal(HttpStatusCode.Created, status);
    }

    [Fact]
    public async Task A_gift_card_line_is_exempt_from_the_band_check()
    {
        // A gift card's VAT is fixed by the tenant's voucher treatment (zero under multi-purpose),
        // which is a different rule from the catalogue bands — without the exemption every card
        // sold after a rate change would false-positive into quarantine.
        var c = _f.CreateClient();
        var (token, _) = await ProvisionEnrolAndSeedRatesAsync(c, "vat3@acme.test");

        var (status, _) = await PostSale(c, Sale(1, 0, Change.AddDays(2), itemIdOne: "GIFT-CARD"), token);
        Assert.Equal(HttpStatusCode.Created, status);
    }
}
