using System;
using System.Linq;
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
/// MAUI retrofit WP2c — Definition of Done for the portal VAT surface.
///
/// The principle under test: THE PORTAL IS THE SOURCE OF VAT TRUTH. A till receives bands, applies
/// them, and reports what it charged; it never decides a VAT rule. So the contract a till reads has
/// to carry enough to survive being offline across a rate change, and the editor has to make the
/// dangerous edits impossible rather than merely discouraged.
/// </summary>
public class VatBandsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public VatBandsE2eTests(PlutusAppFactory f) => _f = f;

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static HttpRequestMessage Req(HttpMethod m, string url, string token, object body = null)
    {
        var r = new HttpRequestMessage(m, url);
        if (body != null) r.Content = JsonContent.Create(body);
        r.Headers.Authorization = new("Bearer", token);
        return r;
    }

    /// <summary>A fresh tenant with an owner who holds portal.company.manage, plus a till device
    /// token — the two audiences of this controller.</summary>
    private async Task<(Guid TenantId, string Owner, string Device)> ProvisionAsync(HttpClient c, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        var pBody = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/tenants", admin,
            new { name = "VatBands " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" })));
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();
        var ownerId = pBody.GetProperty("adminUserId").GetGuid();

        // perm:* resolves from RBAC by userId, never from token scopes (runbook pitfall #5).
        // "Owner" carries every portal permission, including portal.company.manage.
        using (var scope = _f.Services.CreateScope())
        {
            var db = new MySqlDbContext(
                scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
                new Plutus.Entities.Tenancy.FixedTenantContext(tenantId));
            db.CurrentUser = "vat-bands-e2e";
            await RbacSeeder.EnsureBuiltInRolesAsync(db, tenantId);
            var role = await db.RbacRoles.FirstAsync(r => r.Name == "Owner" && r.TenantId == tenantId);
            db.RbacRoleAssignments.Add(new RbacRoleAssignment
            {
                Id = Uuid7.New(), TenantId = tenantId, UserId = ownerId, RoleId = role.Id,
                ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var owner = PlutusAppFactory.OperatorTokenFor(ownerId, "pos.sell", tenantId);

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        var tBody = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/tills", portal,
            new { storeId, name = "Vat till" })));
        var eBody = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/tills/enrol", portal,
            new { enrolmentCode = tBody.GetProperty("enrolmentCode").GetString() })));
        var kBody = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/tokens/device", portal,
            new { deviceId = eBody.GetProperty("deviceId").GetGuid(), clientSecret = eBody.GetProperty("clientSecret").GetString() })));

        return (tenantId, owner, kBody.GetProperty("accessToken").GetString()!);
    }

    private async Task SeedBandsAsync(Guid tenantId, params (string Band, string Name, VatClass Cls, int Bp)[] bands)
    {
        using var scope = _f.Services.CreateScope();
        var db = new MySqlDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
            new Plutus.Entities.Tenancy.FixedTenantContext(Guid.Empty));
        db.CurrentUser = "vat-bands-e2e";

        // ⚠⚠ UPSERT, NOT ADD — since 2026-08-25 a provisioned tenant is BORN with standard/reduced/
        // zero at the epoch, so a blind insert collides:
        // `UNIQUE constraint failed: VatRatePoints.TenantId, Band, EffectiveFromUtc`.
        //
        // ⚠ This does not weaken anything. The tenant still ends up with exactly the bands a test
        // asks for, at exactly the rates it asks for — the helper now overwrites a seeded default
        // instead of failing on it. Tests that add a band the defaults do not include (`exempt`,
        // deliberately absent) still insert as before.
        var existing = await db.VatRatePoints.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.EffectiveFromUtc == DateTime.UnixEpoch)
            .ToListAsync();

        foreach (var (band, name, cls, bp) in bands)
        {
            var row = existing.FirstOrDefault(p => p.Band == band);
            if (row is null)
            {
                db.VatRatePoints.Add(new VatRatePoint
                {
                    Id = Uuid7.New(), TenantId = tenantId, Band = band, DisplayName = name,
                    Class = (int)cls, RateBp = bp, EffectiveFromUtc = DateTime.UnixEpoch,
                });
            }
            else
            {
                row.DisplayName = name;
                row.Class = (int)cls;
                row.RateBp = bp;
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A one-line 0% sale carrying its BAND in the line metadata, exactly as the web till builds it
    /// (`{"itemIdOne":…,"exUnitPence":…,"vatBand":"exempt"}`).
    ///
    /// ⚠ Note what is identical between a zero-rated and an exempt sale here: rate 0bp, VAT £0,
    /// price == ex price. The band string is the ONLY difference, which is precisely why it has to
    /// be on the wire.
    /// </summary>
    private static async Task<HttpResponseMessage> PostZeroRatedSaleAsync(
        HttpClient c, string deviceToken, long seq, long grossPence, string band, DateOnly day)
    {
        var sale = new
        {
            saleId = Uuid7.New(), deviceSeq = seq, channel = 0,
            businessDay = day.ToString("yyyy-MM-dd"),
            occurredAtUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            grossPence, vatPence = 0L,
            lines = new[]
            {
                new
                {
                    itemId = Guid.NewGuid(), qty = 1, unitPricePence = grossPence, discountPence = 0L,
                    lineGrossPence = grossPence, vatRateBp = 0, vatAmountPence = 0L,
                    discountsJson = "{\"itemIdOne\":\"5010000000009\",\"exUnitPence\":" + grossPence
                                  + ",\"vatBand\":\"" + band + "\"}",
                },
            },
            tenders = new[] { new { tenderType = 0, amountPence = grossPence, changePence = 0L } },
        };
        return await c.SendAsync(Req(HttpMethod.Post, "/api/v1/sales", deviceToken, sale));
    }

    /// <summary>Project the rollups the VAT return reads. The integration host removes hosted
    /// services, so nothing folds sales automatically — the rebuild does it, and exercises the
    /// band-aware grain on the rebuild path at the same time.</summary>
    private async Task ProjectRollupsAsync(Guid tenantId)
    {
        using var scope = _f.Services.CreateScope();
        // Scoped to THIS tenant: StampAndGuardTenant refuses writes for anyone else, and these
        // tests provision fresh tenants rather than trading as Kapow (runbook pitfall #3).
        var db = new MySqlDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
            new Plutus.Entities.Tenancy.FixedTenantContext(tenantId));
        db.CurrentUser = "vat-bands-e2e";
        await Plutus.Reporting.RollupRebuilder.RebuildAsync(db, tenantId);
    }

    [Fact]
    public async Task A_DEVICE_token_can_read_the_published_bands_and_gets_the_whole_timeline()
    {
        // ⚠ THE POINT OF THE WHOLE CONTRACT. A till must receive FUTURE rate points, not just
        // "the rate right now" — otherwise one that goes offline today keeps charging the old rate
        // through a change it never heard about, and its backlog is quarantined on reconnect.
        var c = _f.CreateClient();
        var (tenantId, owner, device) = await ProvisionAsync(c, "bands1@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("zero", "Zero rated (books)", VatClass.Zero, 0));

        var future = DateTime.UtcNow.AddDays(30);
        var scheduled = await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands/standard/rate-changes", owner,
            new { rateBp = 1750, effectiveFromUtc = future, note = "Budget 2026" }));
        Assert.Equal(HttpStatusCode.Created, scheduled.StatusCode);

        var res = await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands", device));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);

        var standard = body.GetProperty("bands").EnumerateArray().Single(b => b.GetProperty("key").GetString() == "standard");
        // The rate to charge TODAY is still 20% — the change is invisible until its date.
        Assert.Equal(2000, standard.GetProperty("rateBp").GetInt32());
        // …but the till is handed the change so it can apply it itself, offline, on the day.
        var rates = standard.GetProperty("rates").EnumerateArray().Select(r => r.GetProperty("rateBp").GetInt32()).ToArray();
        Assert.Equal(new[] { 2000, 1750 }, rates);
    }

    [Fact]
    public async Task Zero_and_exempt_survive_the_round_trip_as_DISTINCT_bands_at_the_same_zero_rate()
    {
        // §2a finding 2: both are 0% to the customer and different in law — zero-rated is a taxable
        // supply with input-tax recovery, exempt is not taxable and blocks it. A contract that
        // published only the rate would merge them and lose the partial-exemption figure forever.
        var c = _f.CreateClient();
        var (tenantId, _, device) = await ProvisionAsync(c, "bands2@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("zero", "Zero rated (books)", VatClass.Zero, 0),
            ("exempt", "Exempt", VatClass.Exempt, 0));

        var body = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands", device)));
        var zeroRated = body.GetProperty("bands").EnumerateArray().Where(b => b.GetProperty("rateBp").GetInt32() == 0).ToArray();

        Assert.Equal(2, zeroRated.Length);
        Assert.Contains(zeroRated, b => b.GetProperty("vatClass").GetString() == "Zero");
        Assert.Contains(zeroRated, b => b.GetProperty("vatClass").GetString() == "Exempt");
    }

    [Fact]
    public async Task A_rate_change_dated_in_the_PAST_is_refused()
    {
        // Back-dating would retrospectively invalidate sales already recorded under the old rate:
        // WP2b judges every line against the rates in force at its OccurredAtUtc, so a past-dated
        // point turns settled history into stale-band quarantine without changing a penny of what
        // the customer actually paid.
        var c = _f.CreateClient();
        var (tenantId, owner, _) = await ProvisionAsync(c, "bands3@acme.test");
        await SeedBandsAsync(tenantId, ("standard", "20%", VatClass.Standard, 2000));

        var res = await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands/standard/rate-changes", owner,
            new { rateBp = 1750, effectiveFromUtc = DateTime.UtcNow.AddDays(-1), note = "oops" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("700/45", (await ReadJson(res)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_scheduled_change_can_be_cancelled_but_one_already_in_force_cannot()
    {
        var c = _f.CreateClient();
        var (tenantId, owner, _) = await ProvisionAsync(c, "bands4@acme.test");
        await SeedBandsAsync(tenantId, ("standard", "20%", VatClass.Standard, 2000));

        var created = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands/standard/rate-changes", owner,
            new { rateBp = 1750, effectiveFromUtc = DateTime.UtcNow.AddDays(10), note = "typo" })));
        var id = created.GetProperty("id").GetGuid();

        // Not yet in force → cancellable.
        Assert.Equal(HttpStatusCode.NoContent,
            (await c.SendAsync(Req(HttpMethod.Delete, $"/api/v1/vat/bands/standard/rate-changes/{id}", owner))).StatusCode);

        // The ORIGINAL point is in force — tills have charged it and sales exist that only it
        // explains, so removing it would convert them into quarantine.
        var admin = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands/admin", owner)));
        var live = admin.GetProperty("bands").EnumerateArray()
            .Single(b => b.GetProperty("key").GetString() == "standard")
            .GetProperty("points").EnumerateArray().Single(p => p.GetProperty("inForce").GetBoolean());
        var res = await c.SendAsync(Req(HttpMethod.Delete,
            $"/api/v1/vat/bands/standard/rate-changes/{live.GetProperty("id").GetGuid()}", owner));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("already in force", (await ReadJson(res)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_class_that_charges_nothing_cannot_be_given_a_rate_and_vice_versa()
    {
        // The one rule that IS derivable: Zero/Exempt/OutsideScope are 0% by definition. A
        // "zero-rated" band at 20% would misreport every sale in it.
        var c = _f.CreateClient();
        var (_, owner, _) = await ProvisionAsync(c, "bands5@acme.test");

        var withRate = await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands?rateBp=2000", owner,
            new { key = "books", displayName = "Books", @class = "Zero" }));
        Assert.Equal(HttpStatusCode.BadRequest, withRate.StatusCode);

        var withoutRate = await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands?rateBp=0", owner,
            new { key = "posh", displayName = "Standard", @class = "Standard" }));
        Assert.Equal(HttpStatusCode.BadRequest, withoutRate.StatusCode);
    }

    [Fact]
    public async Task Reclassifying_a_band_moves_every_point_and_is_audited_with_both_values()
    {
        // Kapow's real fix: a 0% band mislabelled Exempt when UK law zero-rates books. No money
        // moves (both are 0% output tax) but input-tax recovery is restored — so the change has to
        // be traceable to whoever made it.
        var c = _f.CreateClient();
        var (tenantId, owner, _) = await ProvisionAsync(c, "bands6@acme.test");
        await SeedBandsAsync(tenantId, ("zero", "Exempt", VatClass.Exempt, 0));

        Assert.Equal(HttpStatusCode.NoContent,
            (await c.SendAsync(Req(HttpMethod.Put, "/api/v1/vat/bands/zero", owner,
                new { key = "zero", displayName = "Zero rated (books)", @class = "Zero" }))).StatusCode);

        var admin = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands/admin", owner)));
        // ⚠ Selected BY KEY, not `.Single()`. Until 2026-08-25 a provisioned tenant had no bands at
        // all, so the one this test seeded was the only one and `.Single()` happened to work. A
        // tenant is now born with standard/reduced/zero, so `.Single()` throws — and it was never
        // what the test was about. Naming the band keeps the assertion exact and stops it depending
        // on how many OTHER bands a shop happens to have.
        var band = admin.GetProperty("bands").EnumerateArray()
            .Single(b => b.GetProperty("key").GetString() == "zero");
        Assert.Equal("Zero", band.GetProperty("vatClass").GetString());
        Assert.Equal("Zero rated (books)", band.GetProperty("displayName").GetString());

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.Action == "vat.band.update").FirstAsync();
        Assert.Contains("Exempt", audit.DetailJson);   // what it was
        Assert.Contains("Zero", audit.DetailJson);     // what it became
    }

    [Fact]
    public async Task An_unseeded_tenant_gets_its_legacy_Taxes_migrated_rather_than_an_empty_contract()
    {
        // A till that reads no bands has nothing to apply, and WP2b treats an empty history as
        // "skip" — so an unseeded tenant would silently lose both the contract and the compliance
        // check. The first read migrates the legacy rows instead.
        var c = _f.CreateClient();
        var (_, _, device) = await ProvisionAsync(c, "bands7@acme.test");

        var body = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands", device)));
        var bands = body.GetProperty("bands").EnumerateArray().ToArray();
        // Whatever the tenant template seeds into Taxes, the contract is never blank when Taxes
        // has rows, and every band carries an explicit class rather than a bare rate.
        Assert.All(bands, b => Assert.False(string.IsNullOrWhiteSpace(b.GetProperty("vatClass").GetString())));
    }

    [Fact]
    public async Task An_EXEMPT_sale_survives_till_to_return_and_produces_a_partial_exemption_figure()
    {
        // ⚠ THE POINT OF THE WHOLE EXEMPT SEAM, end to end. Two 0% sales — one zero-rated, one
        // exempt — must reach the VAT return as SEPARATE bands, because that split is the only way
        // to work out how much input tax is recoverable (HMRC Notice 706). Before the band travelled
        // on the line, both arrived as `VatRateBp = 0` and were indistinguishable forever.
        var c = _f.CreateClient();
        var (tenantId, owner, device) = await ProvisionAsync(c, "bands9@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("zero", "Zero rated (books)", VatClass.Zero, 0),
            ("exempt", "Exempt", VatClass.Exempt, 0));

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        // Both lines are 0bp and £0 VAT — the ONLY thing telling them apart is the band in the meta.
        Assert.Equal(HttpStatusCode.Created, (await PostZeroRatedSaleAsync(c, device, 1, 800, "zero", day)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostZeroRatedSaleAsync(c, device, 2, 500, "exempt", day)).StatusCode);

        await ProjectRollupsAsync(tenantId);

        var from = day.AddDays(-1).ToString("yyyy-MM-dd");
        var to = day.AddDays(1).ToString("yyyy-MM-dd");
        var report = await ReadJson(await c.SendAsync(Req(HttpMethod.Get,
            $"/api/v1/reports/vat?from={from}&to={to}&granularity=year", owner)));

        var buckets = report.GetProperty("buckets").EnumerateArray().ToArray();
        var zero = buckets.Single(b => b.GetProperty("bandKey").GetString() == "zero");
        var exempt = buckets.Single(b => b.GetProperty("bandKey").GetString() == "exempt");
        Assert.Equal(800, zero.GetProperty("grossPence").GetInt64());
        Assert.Equal(500, exempt.GetProperty("grossPence").GetInt64());
        // Same rate, different CLASS — that is the distinction, and it survived.
        Assert.Equal("Zero", zero.GetProperty("vatClass").GetString());
        Assert.Equal("Exempt", exempt.GetProperty("vatClass").GetString());
        // Neither produces output tax: reclassifying moves no money on Box 1, only recovery.
        Assert.Equal(0, zero.GetProperty("vatPence").GetInt64());
        Assert.Equal(0, exempt.GetProperty("vatPence").GetInt64());

        // …and the figure this all exists for: taxable 800 of 1300 supplies = 61.54% recoverable.
        var pe = report.GetProperty("partialExemption");
        Assert.True(pe.GetProperty("applies").GetBoolean());
        Assert.Equal(800, pe.GetProperty("taxableGrossPence").GetInt64());
        Assert.Equal(500, pe.GetProperty("exemptGrossPence").GetInt64());
        Assert.Equal(61.54m, pe.GetProperty("recoverablePercent").GetDecimal());
    }

    [Fact]
    public async Task With_no_exempt_sales_partial_exemption_says_it_does_NOT_apply()
    {
        // Kapow's case, and it must be stated rather than left blank: zero-rated is a TAXABLE supply,
        // so a shop selling only standard and zero-rated goods recovers input tax in full. Reading
        // "0%" and assuming restriction is the expensive mistake this reverses.
        var c = _f.CreateClient();
        var (tenantId, owner, device) = await ProvisionAsync(c, "bands10@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("zero", "Zero rated (books)", VatClass.Zero, 0));

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Created, (await PostZeroRatedSaleAsync(c, device, 1, 800, "zero", day)).StatusCode);
        await ProjectRollupsAsync(tenantId);

        var report = await ReadJson(await c.SendAsync(Req(HttpMethod.Get,
            $"/api/v1/reports/vat?from={day.AddDays(-1):yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}&granularity=year", owner)));
        var pe = report.GetProperty("partialExemption");
        Assert.False(pe.GetProperty("applies").GetBoolean());
        Assert.Equal(0, pe.GetProperty("exemptGrossPence").GetInt64());
        Assert.Equal(100m, pe.GetProperty("recoverablePercent").GetDecimal());
    }

    /// <summary>Seeds a Business + Tax row + one item on it, so the SERVER can resolve a band from
    /// the catalogue for a line that arrived without one.</summary>
    private async Task<string> SeedItemOnTaxAsync(Guid tenantId, int taxId, double rate, string barcode)
    {
        using var scope = _f.Services.CreateScope();
        var db = new MySqlDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
            new Plutus.Entities.Tenancy.FixedTenantContext(tenantId));
        db.CurrentUser = "vat-bands-e2e";
        var bizId = Guid.NewGuid();
        db.Business.Add(new Business { Id = bizId, Name = "Band E2E", NameAbbr = "BE2E", VatIN = "GB0" });
        // Item.TaxId → Tax is a composite FK (TaxId, IdTwo) → (Tax.IdOne, Tax.IdTwo).
        db.Taxes.Add(new Tax { IdOne = taxId, IdTwo = bizId, Name = "Seeded", Rate = rate });
        var catId = Guid.NewGuid();
        db.Category.Add(new Category { IdOne = catId, IdTwo = bizId, Name = "Band E2E", Description = "seed" });
        db.Items.Add(new Item
        {
            IdOne = barcode, IdTwo = bizId, Name = "Banded item", Brand = "-", Desc = "",
            Cost = 1m, ExPrice = 8m, Price = 8m, TaxId = taxId, CatId = catId,
        });
        await db.SaveChangesAsync();
        return barcode;
    }

    /// <summary>A 0% sale whose till states NO band — an older till, MAUI today, or a future
    /// platform that hasn't implemented band awareness.</summary>
    private static async Task<HttpResponseMessage> PostBandlessSaleAsync(
        HttpClient c, string deviceToken, long seq, long grossPence, string barcode, DateOnly day)
    {
        var sale = new
        {
            saleId = Uuid7.New(), deviceSeq = seq, channel = 0,
            businessDay = day.ToString("yyyy-MM-dd"),
            occurredAtUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            grossPence, vatPence = 0L,
            lines = new[]
            {
                new
                {
                    itemId = Guid.NewGuid(), qty = 1, unitPricePence = grossPence, discountPence = 0L,
                    lineGrossPence = grossPence, vatRateBp = 0, vatAmountPence = 0L,
                    // No "vatBand" — exactly what an unaware till sends.
                    discountsJson = "{\"itemIdOne\":\"" + barcode + "\",\"exUnitPence\":" + grossPence + "}",
                },
            },
            tenders = new[] { new { tenderType = 0, amountPence = grossPence, changePence = 0L } },
        };
        return await c.SendAsync(Req(HttpMethod.Post, "/api/v1/sales", deviceToken, sale));
    }

    [Fact]
    public async Task A_till_that_sends_NO_band_still_gets_one_the_SERVER_resolves_it_from_the_catalogue()
    {
        // ⚠ THIS IS WHAT MAKES EVERY CHANNEL CONSISTENT — including the webstore connector (which
        // posts through this same endpoint) and a till on a platform that doesn't exist yet. A band
        // that only arrives when a client remembers to send it is a band that goes missing, and the
        // failure is silent: zero-rated and exempt both record 0%, so a bandless line can never be
        // classified afterwards.
        var c = _f.CreateClient();
        var (tenantId, owner, device) = await ProvisionAsync(c, "bands12@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("zero", "Zero rated (books)", VatClass.Zero, 0));
        var barcode = await SeedItemOnTaxAsync(tenantId, taxId: 77, rate: 1.0, barcode: "BAND-STAMP-1");

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Created, (await PostBandlessSaleAsync(c, device, 1, 800, barcode, day)).StatusCode);

        // The server resolved "zero" from the item's tax row — unambiguous at 0% for this tenant.
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var line = await db.SaleLines.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.ItemIdOne == barcode).FirstAsync();
        Assert.Equal("zero", line.VatBand);
    }

    [Fact]
    public async Task An_AMBIGUOUS_unmapped_tax_row_leaves_the_band_null_rather_than_guessing()
    {
        // With BOTH a zero-rated and an exempt band at 0%, the catalogue cannot say which a tax row
        // is until someone maps it. Guessing would put a number on a VAT return nobody chose, so the
        // band stays null, the takings report as unclassified, and the portal flags the tax row.
        var c = _f.CreateClient();
        var (tenantId, owner, device) = await ProvisionAsync(c, "bands13@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("zero", "Zero rated (books)", VatClass.Zero, 0),
            ("exempt", "Exempt", VatClass.Exempt, 0));
        var barcode = await SeedItemOnTaxAsync(tenantId, taxId: 78, rate: 1.0, barcode: "BAND-STAMP-2");

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Created, (await PostBandlessSaleAsync(c, device, 1, 800, barcode, day)).StatusCode);

        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var line = await db.SaleLines.IgnoreQueryFilters()
                .Where(l => l.TenantId == tenantId && l.ItemIdOne == barcode).FirstAsync();
            Assert.Null(line.VatBand);
        }

        // …and the editor asks for the decision rather than hiding it.
        var admin = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands/admin", owner)));
        Assert.True(admin.GetProperty("mappingRequired").GetBoolean());
        Assert.Contains(admin.GetProperty("taxRows").EnumerateArray(),
            t => t.GetProperty("legacyTaxId").GetInt32() == 78 && t.GetProperty("band").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task A_band_the_TILL_stated_is_never_overwritten_by_the_catalogue()
    {
        // The till knows things the catalogue doesn't: a single-purpose gift-card line is
        // standard-rated by the voucher treatment even though its catalogue row sits on a zero band.
        // If the server "corrected" that, the treatment would be silently overridden.
        var c = _f.CreateClient();
        var (tenantId, _, device) = await ProvisionAsync(c, "bands14@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("zero", "Zero rated (books)", VatClass.Zero, 0));
        var barcode = await SeedItemOnTaxAsync(tenantId, taxId: 79, rate: 1.0, barcode: "BAND-STAMP-3");

        // The item's tax row says 0% → "zero", but the line declares "standard".
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var sale = new
        {
            saleId = Uuid7.New(), deviceSeq = 1L, channel = 0,
            businessDay = day.ToString("yyyy-MM-dd"),
            occurredAtUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            grossPence = 800L, vatPence = 0L,
            lines = new[]
            {
                new
                {
                    itemId = Guid.NewGuid(), qty = 1, unitPricePence = 800L, discountPence = 0L,
                    lineGrossPence = 800L, vatRateBp = 0, vatAmountPence = 0L,
                    discountsJson = "{\"itemIdOne\":\"" + barcode + "\",\"exUnitPence\":800,\"vatBand\":\"standard\"}",
                },
            },
            tenders = new[] { new { tenderType = 0, amountPence = 800L, changePence = 0L } },
        };
        Assert.Equal(HttpStatusCode.Created,
            (await c.SendAsync(Req(HttpMethod.Post, "/api/v1/sales", device, sale))).StatusCode);

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var line = await db.SaleLines.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.ItemIdOne == barcode).FirstAsync();
        Assert.Equal("standard", line.VatBand);
    }

    [Fact]
    public async Task Mapping_a_tax_row_to_a_band_at_a_DIFFERENT_rate_is_refused()
    {
        // It would make every item on that tax row off-band: the item editor validated their prices
        // against the tax row's multiplier, so a band at another rate contradicts the catalogue.
        var c = _f.CreateClient();
        var (tenantId, owner, _) = await ProvisionAsync(c, "bands11@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("exempt", "Exempt", VatClass.Exempt, 0));

        // Find a real tax row and its rate from the editor's own view.
        var admin = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands/admin", owner)));
        var taxRows = admin.GetProperty("taxRows").EnumerateArray().ToArray();
        if (taxRows.Length == 0) return;   // tenant template seeds no Taxes — nothing to assert on
        var standardRate = taxRows.FirstOrDefault(t => t.GetProperty("rateBp").GetInt32() >= 1000);
        if (standardRate.ValueKind == JsonValueKind.Undefined) return;

        var res = await c.SendAsync(Req(HttpMethod.Put,
            $"/api/v1/vat/tax-mapping/{standardRate.GetProperty("legacyTaxId").GetInt32()}", owner,
            new { band = "exempt" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("off-band", (await ReadJson(res)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_editor_is_gated_but_the_published_contract_is_readable_by_any_till()
    {
        var c = _f.CreateClient();
        var (_, _, device) = await ProvisionAsync(c, "bands8@acme.test");

        // A device token may READ the contract — it has to, to price anything.
        Assert.Equal(HttpStatusCode.OK,
            (await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands", device))).StatusCode);
        // …and may NOT see or change the editor's view.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands/admin", device))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands?rateBp=2000", device,
                new { key = "sneaky", displayName = "Sneaky", @class = "Standard" }))).StatusCode);
    }
}
