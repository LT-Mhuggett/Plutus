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
/// WP2c — restating past VAT returns end to end (Matt, 2026-08-08: "correct past return").
///
/// The defect being corrected was in the ARITHMETIC, never the data: the return summed each line's
/// penny-rounded VAT instead of applying the VAT fraction to takings (Notice 727 §3.4.1). So this
/// endpoint repairs nothing — it re-runs both methods over the same rollups and reports the gap.
/// These tests pin that the gap is real, lands in the right VAT period, and is routed by the
/// Notice 700/45 threshold rather than by a guess.
/// </summary>
public class VatCorrectionsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public VatCorrectionsE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private static HttpRequestMessage Req(string url, string token)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, url);
        r.Headers.Authorization = new("Bearer", token);
        return r;
    }

    private async Task<string> FinancialsUserAsync()
    {
        // perm:portal.financials.view resolves from RBAC by userId (runbook pitfall #5).
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "vat-corrections-e2e";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var owner = await db.RbacRoles.FirstAsync(r => r.Name == "Owner");
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Kapow, UserId = userId, RoleId = owner.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return PlutusAppFactory.OperatorTokenFor(userId, "pos.sell");
    }

    /// <summary>Takings on one day, as the tills would have recorded them: gross at a derived rate,
    /// with the VAT the till actually charged (Σ per-line, penny-rounded). Seeds the tenant's bands
    /// too — WITHOUT them every penny reports as unclassified and there is nothing to restate,
    /// which is correct behaviour and would make this test pass for the wrong reason.</summary>
    private async Task SeedRollupAsync(DateOnly day, int rateBp, long grossPence, long chargedVatPence)
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "vat-corrections-e2e";
        if (!await db.VatRatePoints.AnyAsync())
            foreach (var (band, name, cls, bp) in new[]
            {
                ("standard", "20%", VatClass.Standard, 2000),
                ("zero", "Zero rated (books)", VatClass.Zero, 0),
            })
                db.VatRatePoints.Add(new VatRatePoint
                {
                    Id = Uuid7.New(), TenantId = Kapow, Band = band, DisplayName = name,
                    Class = (int)cls, RateBp = bp, EffectiveFromUtc = DateTime.UnixEpoch,
                });
        db.VatRollups.Add(new VatRollup
        {
            TenantId = Kapow, CompanyId = Guid.Empty, StoreId = 1,
            BusinessDay = day, VatRateBp = rateBp,
            GrossPence = grossPence, NetPence = grossPence - chargedVatPence, VatPence = chargedVatPence,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_restatement_finds_the_gap_lands_it_in_the_right_quarter_and_routes_it()
    {
        var c = _f.CreateClient();
        var token = await FinancialsUserAsync();

        // A completed quarter, well in the past so it is never "in progress". Stagger 1 → the
        // quarter ending 31 Mar 2025 runs Jan–Mar.
        //
        // £100.00 of standard-rated takings where the tills charged £17.00 (ten £10 lines, each
        // rounding 1000 − 833 = 167… deliberately overstated here to make the direction visible).
        // The law says the VAT due is the fraction on the takings: 10000 × 20/120 = 1666.67 → 1667.
        await SeedRollupAsync(new DateOnly(2025, 2, 10), 2002, 10_000, 1_700);

        var res = await c.SendAsync(Req("/api/v1/reports/vat-corrections?basis=quarter&staggerEndMonth=3", token));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        var q1 = body.GetProperty("periods").EnumerateArray()
            .Single(p => p.GetProperty("startDay").GetString() == "2025-01-01");
        Assert.Equal("2025-03-31", q1.GetProperty("endDay").GetString());
        Assert.True(q1.GetProperty("complete").GetBoolean());
        // It ended before the method was fixed (2026-08-08), so it WAS filed on the wrong basis.
        Assert.True(q1.GetProperty("affected").GetBoolean());

        // ⚠ The line arrived declaring 2002bp — a till derives the rate from the price pair. The
        // restatement must snap it to the 20% BAND, not take a fraction of 20.02%.
        Assert.Equal(1_700, q1.GetProperty("asFiledVatPence").GetInt64());
        Assert.Equal(1_667, q1.GetProperty("restatedVatPence").GetInt64());
        Assert.Equal(-33, q1.GetProperty("netErrorPence").GetInt64());   // tills over-charged
        Assert.Equal(10_000 - 1_667, q1.GetProperty("boxSixPence").GetInt64());

        // £0.33 against a £10,000 floor threshold → an adjustment on the next return, not a VAT652.
        var summary = body.GetProperty("summary");
        Assert.Equal("adjust-next-return", summary.GetProperty("route").GetString());
        Assert.Equal(10_000_00, summary.GetProperty("thresholdPence").GetInt64());

        // …and the endpoint states its basis and its correction guidance rather than leaving the
        // reader to guess which rule produced the number.
        Assert.Contains("727", body.GetProperty("returnBasis").GetString());
        Assert.Contains("700/45", body.GetProperty("guidance").GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_period_that_is_still_running_is_never_reported_as_correctable()
    {
        // You cannot correct a return you have not filed.
        var c = _f.CreateClient();
        var token = await FinancialsUserAsync();
        await SeedRollupAsync(DateOnly.FromDateTime(DateTime.UtcNow), 2000, 5_000, 900);

        var body = JsonDocument.Parse(await (await c.SendAsync(
            Req("/api/v1/reports/vat-corrections?basis=month", token))).Content.ReadAsStringAsync()).RootElement;

        var current = body.GetProperty("periods").EnumerateArray()
            .Single(p => p.GetProperty("endDay").GetString() ==
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1 - DateTime.UtcNow.Day).AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"));
        Assert.False(current.GetProperty("complete").GetBoolean());
        Assert.False(current.GetProperty("affected").GetBoolean());
    }

    [Fact]
    public async Task Takings_no_band_explains_are_reported_not_quietly_restated_to_zero_error()
    {
        // ⚠ THE TRAP THIS PINS. Unclassified takings have no rate to take a fraction of, so their
        // restated figure IS what was charged and they contribute nothing to the net error. Without
        // surfacing them, a tenant whose bands are unconfigured would read a confident "nothing to
        // correct" that actually means "I could not classify a penny of it".
        var c = _f.CreateClient();
        var token = await FinancialsUserAsync();
        // 1000bp — no band this tenant has, and further than the 25bp snap tolerance from any.
        await SeedRollupAsync(new DateOnly(2025, 5, 12), 1000, 11_000, 1_000);

        var body = JsonDocument.Parse(await (await c.SendAsync(
            Req("/api/v1/reports/vat-corrections?basis=quarter&staggerEndMonth=3", token))).Content.ReadAsStringAsync()).RootElement;

        var q2 = body.GetProperty("periods").EnumerateArray()
            .Single(p => p.GetProperty("startDay").GetString() == "2025-04-01");
        Assert.Equal(11_000, q2.GetProperty("unclassifiedGrossPence").GetInt64());
        // No invented rate, and no invented correction.
        Assert.Equal(1_000, q2.GetProperty("restatedVatPence").GetInt64());
        Assert.Equal(0, q2.GetProperty("netErrorPence").GetInt64());
        // …but it IS visible at the summary level, so the screen can warn.
        Assert.True(body.GetProperty("summary").GetProperty("unclassifiedGrossPence").GetInt64() >= 11_000);
    }

    [Fact]
    public async Task Financials_permission_is_required()
    {
        var c = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.SendAsync(Req("/api/v1/reports/vat-corrections", PlutusAppFactory.OperatorToken("pos.sell")))).StatusCode);
    }

    [Fact]
    public async Task A_bad_stagger_or_basis_is_refused_rather_than_silently_defaulted()
    {
        // Silently defaulting would attribute every correction to the wrong return, and look fine.
        var c = _f.CreateClient();
        var token = await FinancialsUserAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.SendAsync(Req("/api/v1/reports/vat-corrections?basis=fortnight", token))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.SendAsync(Req("/api/v1/reports/vat-corrections?staggerEndMonth=13", token))).StatusCode);
    }
}
