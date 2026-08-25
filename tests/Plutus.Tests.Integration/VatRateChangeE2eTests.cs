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

            // ⚠⚠ UPSERT — since 2026-08-25 provisioning gives a tenant standard/reduced/zero at the
            // epoch, so re-adding them here collides on
            // `UNIQUE (TenantId, Band, EffectiveFromUtc)`. The three epoch rows below now match the
            // seeded defaults exactly (0 / 500 / 2000 bp), so this is a genuine no-op for them; the
            // 17.5% point at `Change` is what the test is really about and still inserts.
            //
            // ⚠ Kept as an explicit upsert rather than deleting the three lines, so the test still
            // STATES the rate history it depends on instead of inheriting it invisibly from
            // provisioning — if the seeded defaults ever change, this test keeps its own meaning.
            var existing = await db.VatRatePoints.IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId).ToListAsync();

            foreach (var p in new[]
            {
                new Plutus.Entities.Models.VatRatePoint { Band = VatRateHistory.Zero, RateBp = 0, EffectiveFromUtc = DateTime.UnixEpoch },
                new Plutus.Entities.Models.VatRatePoint { Band = VatRateHistory.Reduced, RateBp = 500, EffectiveFromUtc = DateTime.UnixEpoch },
                new Plutus.Entities.Models.VatRatePoint { Band = VatRateHistory.Standard, RateBp = 2000, EffectiveFromUtc = DateTime.UnixEpoch },
                new Plutus.Entities.Models.VatRatePoint { Band = VatRateHistory.Standard, RateBp = 1750, EffectiveFromUtc = Change },
            })
            {
                var row = existing.FirstOrDefault(e => e.Band == p.Band && e.EffectiveFromUtc == p.EffectiveFromUtc);
                if (row is null)
                {
                    p.Id = Uuid7.New();
                    p.TenantId = tenantId;
                    db.VatRatePoints.Add(p);
                }
                else
                {
                    row.RateBp = p.RateBp;
                }
            }
            await db.SaveChangesAsync();
        }

        return (kBody.GetProperty("accessToken").GetString()!, tenantId);
    }

    /// <summary>
    /// A one-line sale built EXACTLY as the web till builds one (api.ts:958-1019): the caller
    /// gives a real price pair, and the declared rate and VAT are DERIVED from it.
    ///
    /// ⚠ This helper used to take a clean rate and compute VAT arithmetically. That modelled a
    /// till this platform does not have, and hid the very regression these tests now cover: a
    /// genuine £14.99/£12.49 line declares 2002bp, not 2000.
    /// </summary>
    private static object Sale(long seq, long unitIncPence, long unitExPence, DateTime occurredAtUtc,
                               string itemIdOne = "5010000000001")
    {
        var declaredBp = unitExPence > 0
            ? (int)Math.Round((double)unitIncPence / unitExPence * 10000 - 10000)
            : 0;
        var vat = unitIncPence - unitExPence;              // api.ts:979 — lineGross − lineEx
        return new
        {
            saleId = Uuid7.New(),
            deviceSeq = seq,
            channel = 0,
            businessDay = DateOnly.FromDateTime(occurredAtUtc).ToString("yyyy-MM-dd"),
            occurredAtUtc,
            grossPence = unitIncPence,
            vatPence = vat,
            lines = new[]
            {
                new
                {
                    itemId = Guid.NewGuid(), qty = 1, unitPricePence = unitIncPence, discountPence = 0L,
                    lineGrossPence = unitIncPence, vatRateBp = declaredBp, vatAmountPence = vat,
                    discountsJson = "{\"itemIdOne\":\"" + itemIdOne + "\",\"exUnitPence\":" + unitExPence + "}",
                },
            },
            tenders = new[] { new { tenderType = 0, amountPence = unitIncPence, changePence = 0L } },
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
    public async Task A_REAL_webtill_line_ingests_even_though_its_declared_rate_is_2002bp()
    {
        // ⚠ THE REGRESSION MATT CAUGHT. £14.99 ex £12.49 is 20% priced to the penny, and the web
        // till ships it declaring 2002bp. The first implementation compared that number against
        // the clean band set and would have QUARANTINED ORDINARY KAPOW SALES the moment any
        // tenant's bands were seeded. Judged on the price pair, it is plainly fine.
        var c = _f.CreateClient();
        var (token, _) = await ProvisionEnrolAndSeedRatesAsync(c, "vat0@acme.test");

        // what the till actually declares for this pair — the number that used to be rejected
        Assert.Equal(2002, (int)Math.Round(1499d / 1249d * 10000 - 10000));

        var (status, _) = await PostSale(c, Sale(1, 1499, 1249, Change.AddDays(-1)), token);
        Assert.Equal(HttpStatusCode.Created, status);
    }

    [Fact]
    public async Task A_stale_BAND_after_the_change_is_quarantined_but_correct_sales_both_sides_are_not()
    {
        var c = _f.CreateClient();
        var (token, tenantId) = await ProvisionEnrolAndSeedRatesAsync(c, "vat1@acme.test");

        // 1. THE CASE THIS EXISTS FOR: a till still pricing at 20% after the standard rate moved
        //    to 17.5% — the pair is explained only by a RETIRED band → quarantined.
        var (staleStatus, staleBody) = await PostSale(c, Sale(1, 1499, 1249, Change.AddDays(2)), token);
        Assert.Equal(HttpStatusCode.Accepted, staleStatus);
        Assert.Contains("quarantined", staleBody);

        // 2. the same moment, correctly repriced at 17.5% (£11.75 ex £10.00) → recorded
        var (goodStatus, _) = await PostSale(c, Sale(2, 1175, 1000, Change.AddDays(2)), token);
        Assert.Equal(HttpStatusCode.Created, goodStatus);

        // 3. NO FALSE POSITIVES: the SAME 20% pair, sold BEFORE the change → recorded. A till
        //    draining a week-old backlog must not have its legitimate history quarantined.
        var (pastStatus, _) = await PostSale(c, Sale(3, 1499, 1249, Change.AddDays(-3)), token);
        Assert.Equal(HttpStatusCode.Created, pastStatus);

        // 4. coexisting bands untouched — a zero-rated book still sells after the change
        var (zeroStatus, _) = await PostSale(c, Sale(4, 800, 800, Change.AddDays(2)), token);
        Assert.Equal(HttpStatusCode.Created, zeroStatus);

        // the quarantine row names BOTH rates, so the reason is actionable rather than "invalid"
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var q = await db.SaleQuarantine.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).FirstAsync();
        Assert.Contains("2000bp", q.Reason);          // what it was priced at
        Assert.Contains("1750", q.Reason);            // what was actually in force
        Assert.Contains("offline", q.Reason);
    }

    [Fact]
    public async Task Legacy_OFF_BAND_damage_still_sells_it_is_reported_not_blocked()
    {
        // Owner decision (VAT-FixLater, 2026-07-23): off-band legacy items keep trading and are
        // SURFACED by the VatIntegrity report. £11.00 ex £10.00 is 10% — no band this tenant has
        // ever had. Blocking it would stop a shop selling stock it has always sold.
        var c = _f.CreateClient();
        var (token, _) = await ProvisionEnrolAndSeedRatesAsync(c, "vat4@acme.test");

        var (status, _) = await PostSale(c, Sale(1, 1100, 1000, Change.AddDays(2)), token);
        Assert.Equal(HttpStatusCode.Created, status);
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

        // a pair matching no band anywhere — still recorded, because this tenant has no history
        var (status, _) = await PostSale(c, Sale(1, 1100, 1000, DateTime.UtcNow), token);
        Assert.Equal(HttpStatusCode.Created, status);
    }

    [Fact]
    public async Task Gift_card_lines_are_exempt_under_BOTH_voucher_treatments()
    {
        // A gift card's VAT is pinned by the tenant's voucher treatment, not the catalogue bands:
        // activation is 0bp (multi-purpose) or 2000bp (single), and a single-purpose REDEMPTION is
        // a NEGATIVE standard-rated line (api.ts:997-1019). None of those price pairs need match a
        // band, so without the exemption every card sold after a rate change would quarantine.
        var c = _f.CreateClient();
        var (token, _) = await ProvisionEnrolAndSeedRatesAsync(c, "vat3@acme.test");

        // multi-purpose activation: priced ex == inc, so no VAT declared at sale
        var (activation, _) = await PostSale(c, Sale(1, 2000, 2000, Change.AddDays(2), itemIdOne: "GIFT-CARD"), token);
        Assert.Equal(HttpStatusCode.Created, activation);

        // ⚠ THE CASE THAT ACTUALLY NEEDS THE EXEMPTION: a SINGLE-purpose activation is priced with
        // VAT in at the standard rate — pinned to 2000bp by the treatment (api.ts:976), NOT by the
        // catalogue. Sold after the standard band moved to 17.5%, its pair (£20.00/£16.67) is
        // explained only by the retired 20% band, so a plain band check quarantines it. Only the
        // exemption keeps gift cards sellable across a rate change.
        var (singlePurpose, _) = await PostSale(c, Sale(2, 2000, 1667, Change.AddDays(2), itemIdOne: "GIFT-CARD"), token);
        Assert.Equal(HttpStatusCode.Created, singlePurpose);
        // (proof it would otherwise fire: the identical pair on an ordinary item is quarantined)
        var (ordinary, _) = await PostSale(c, Sale(3, 2000, 1667, Change.AddDays(2)), token);
        Assert.Equal(HttpStatusCode.Accepted, ordinary);

        // single-purpose redemption: the negative standard-rated line (api.ts:997-1019)
        var (redemption, _) = await PostSale(c, Sale(4, -1200, -1000, Change.AddDays(2), itemIdOne: "GIFT-CARD"), token);
        Assert.Equal(HttpStatusCode.Created, redemption);
    }
}
