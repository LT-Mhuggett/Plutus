using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// **WP-FY — the company year and the VAT periods, set in the portal.**
///
/// ⚠⚠ MATT, 2026-08-21: *"I need to be able to set the company year in the portal. And the VAT
/// periods. This then needs to be reflected in the reports, specifically the VAT reports needs to
/// match the months it reports on."*
///
/// ⚠⚠ WHAT WAS ACTUALLY BROKEN was not a missing setting — it was **two screens disagreeing**.
/// `vat-corrections` took `basis` and `staggerEndMonth` off the QUERY STRING with defaults, so the
/// shop's real stagger was whatever the caller last typed; and `VatReturn`, the screen an accountant
/// files from, took neither — its quarter picker was hard-wired to CALENDAR quarters. A business on
/// stagger 2 files November to January and could not produce that range there at all.
///
/// ⚠ These tests pin the WIRE, not the arithmetic — `FinancialCalendarTests` owns the period maths
/// with 37 vectors including a three-year no-gap/no-overlap sweep.
/// </summary>
public class VatPeriodSettingsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public VatPeriodSettingsE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    /// <summary>⚠ The controller reads `Business.FirstOrDefaultAsync()`, so a tenant with no company
    /// row must still answer. Seeding one only where a test needs the stored values back.</summary>
    private async Task EnsureBusinessAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "vat-periods-e2e-seed";

        if (await Task.FromResult(db.Business.Any())) return;

        db.Business.Add(new Business
        {
            Id = Guid.NewGuid(), Name = "VAT Periods E2E", NameAbbr = "VPE2E", VatIN = "GB0",
        });
        await db.SaveChangesAsync();
    }

    private HttpRequestMessage Req(HttpMethod method, string url, string scope = PlutusPolicies.PlatformAdmin)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(scope, Kapow));
        return r;
    }

    private async Task<JsonElement> GetAsync(string url = "/api/v1/companies/vat-periods")
    {
        var resp = await _f.CreateClient().SendAsync(Req(HttpMethod.Get, url));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
    }

    private async Task<HttpResponseMessage> PutAsync(object body)
    {
        var req = Req(HttpMethod.Put, "/api/v1/companies/vat-periods");
        req.Content = JsonContent.Create(body);
        return await _f.CreateClient().SendAsync(req);
    }

    /// <summary>
    /// ⚠⚠ EVERY TEST HERE SETS THE STATE IT ASSERTS ON, because they share one database.
    ///
    /// The fixture is class-scoped and these tests WRITE — so "unset" is only unset until another
    /// test in this class has run. Two of them failed exactly that way on first execution: one saw a
    /// stagger another had stored, and one saw FIVE quarters in a financial year rather than four.
    ///
    /// ⚠ THE FIVE WAS CORRECT AND THE TEST WAS WRONG, which is worth recording: a stagger-2 business
    /// whose year starts in April has a year that BEGINS MID-PERIOD (its first return runs Feb–Apr),
    /// so the year spans five VAT quarters. `FinancialCalendarTests` pins that behaviour on purpose.
    /// </summary>
    private async Task ClearSettingsAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "vat-periods-e2e-seed";

        foreach (var b in db.Business)
        {
            b.VatBasis = null;
            b.VatStaggerEndMonth = null;
            b.FinancialYearStartMonth = null;
            b.FinancialYearStartDay = null;
            b.VatSettingsChangedAtUtc = null;
            b.VatSettingsChangedBy = null;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>⚠⚠ AN UNSET BUSINESS GETS THE DEFAULT **AND IS TOLD IT IS A DEFAULT**. A guess
    /// presented as a choice is a lie to whoever is reconciling a return.</summary>
    [Fact]
    public async Task Unset_answers_the_default_and_says_it_is_not_configured()
    {
        await EnsureBusinessAsync();
        await ClearSettingsAsync();

        var body = await GetAsync();

        Assert.Equal("quarter", body.GetProperty("basis").GetString());
        Assert.Equal(3, body.GetProperty("staggerEndMonth").GetInt32());
        Assert.False(body.GetProperty("configured").GetBoolean());
        Assert.Contains("not yet set", body.GetProperty("describe").GetString());
    }

    /// <summary>
    /// ⚠ The picker's options come from the SERVER — the client never computes a boundary, because
    /// two implementations of "when does this quarter start" is a C2 twin over money.
    ///
    /// ⚠ A JANUARY-START, STAGGER-1 BUSINESS is chosen deliberately: it is the one configuration
    /// whose financial year lines up exactly with four calendar quarters, so "four" is a fact about
    /// the calendar rather than about the implementation.
    /// </summary>
    [Fact]
    public async Task It_answers_the_periods_of_a_financial_year()
    {
        await EnsureBusinessAsync();
        Assert.Equal(HttpStatusCode.NoContent,
            (await PutAsync(new { basis = "quarter", staggerEndMonth = 3, yearStartMonth = 1, yearStartDay = 1 })).StatusCode);

        var body = await GetAsync("/api/v1/companies/vat-periods?year=2026");

        var periods = body.GetProperty("periods").EnumerateArray().ToList();

        Assert.Equal(4, periods.Count);
        Assert.Equal("2026-01-01", periods[0].GetProperty("from").GetString());
        Assert.Equal("2026-12-31", periods[3].GetProperty("to").GetString());

        Assert.All(periods, p => Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("label").GetString())));

        // ⚠ CONTIGUOUS. A picker offering a list with a hole in it is how a quarter's takings go
        // unfiled, and nothing downstream would notice.
        for (var i = 1; i < periods.Count; i++)
        {
            var prevEnd = DateOnly.Parse(periods[i - 1].GetProperty("to").GetString()!);
            var start = DateOnly.Parse(periods[i].GetProperty("from").GetString()!);
            Assert.Equal(prevEnd.AddDays(1), start);
        }
    }

    /// <summary>
    /// ⚠⚠ AND THE MID-PERIOD YEAR IS PINNED HERE TOO, because it looks like a bug the first time
    /// anybody sees it. A stagger-2 business whose financial year starts in April files Feb–Apr
    /// first, so its year touches FIVE VAT quarters — the picker must offer all five or a return
    /// falls outside every option it shows.
    /// </summary>
    [Fact]
    public async Task A_year_that_starts_mid_period_offers_five_quarters()
    {
        await EnsureBusinessAsync();
        Assert.Equal(HttpStatusCode.NoContent,
            (await PutAsync(new { basis = "quarter", staggerEndMonth = 1, yearStartMonth = 4, yearStartDay = 1 })).StatusCode);

        var body = await GetAsync("/api/v1/companies/vat-periods?year=2026");
        var periods = body.GetProperty("periods").EnumerateArray().ToList();

        Assert.Equal(5, periods.Count);
        Assert.Equal("2026-02-01", periods[0].GetProperty("from").GetString());
    }

    /// <summary>⚠⚠ THE ROUND TRIP — stagger 2, the case the old picker could not express at all.</summary>
    [Fact]
    public async Task Saving_stagger_two_is_read_back_and_marked_configured()
    {
        await EnsureBusinessAsync();

        var put = await PutAsync(new { basis = "quarter", staggerEndMonth = 1, yearStartMonth = 4, yearStartDay = 1 });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var body = await GetAsync();

        Assert.Equal(1, body.GetProperty("staggerEndMonth").GetInt32());
        Assert.True(body.GetProperty("configured").GetBoolean());
        Assert.Contains("January", body.GetProperty("describe").GetString());
        Assert.False(body.GetProperty("changedAtUtc").ValueKind == JsonValueKind.Null);
    }

    /// <summary>
    /// ⚠⚠ SWITCHING TO MONTHLY **CLEARS** THE STAGGER. A stale stagger under a monthly basis is a
    /// value that means nothing and reads as if it means something — and it would reappear the
    /// moment somebody switched back to quarterly, silently restoring a setting nobody chose.
    /// </summary>
    [Fact]
    public async Task Switching_to_monthly_clears_the_stagger()
    {
        await EnsureBusinessAsync();

        Assert.Equal(HttpStatusCode.NoContent,
            (await PutAsync(new { basis = "quarter", staggerEndMonth = 2, yearStartMonth = 4, yearStartDay = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await PutAsync(new { basis = "month", staggerEndMonth = (int?)null, yearStartMonth = 4, yearStartDay = 1 })).StatusCode);

        var body = await GetAsync();

        Assert.Equal("month", body.GetProperty("basis").GetString());
        Assert.True(body.GetProperty("configured").GetBoolean());
        // Resolved back to the default because nothing is stored — not to the 2 that was there.
        Assert.Equal(3, body.GetProperty("staggerEndMonth").GetInt32());
        Assert.Equal(12, body.GetProperty("periods").GetArrayLength());
    }

    /// <summary>
    /// ⚠⚠ A TYPED-IN BAD VALUE IS REFUSED, NOT QUIETLY DEFAULTED. `FinancialCalendar.Resolve`
    /// tolerates a junk stored row so a report cannot crash on one; the WRITE path must not, or the
    /// junk row gets created in the first place and reads back as something else.
    /// </summary>
    [Theory]
    [InlineData("quarter", 0, 4, 1)]
    [InlineData("quarter", 13, 4, 1)]
    [InlineData("fortnightly", 3, 4, 1)]
    [InlineData("quarter", 3, 0, 1)]
    [InlineData("quarter", 3, 13, 1)]
    [InlineData("quarter", 3, 4, 0)]
    [InlineData("quarter", 3, 4, 32)]
    public async Task Junk_is_refused(string basis, int stagger, int yearMonth, int yearDay)
    {
        var resp = await PutAsync(new { basis, staggerEndMonth = stagger, yearStartMonth = yearMonth, yearStartDay = yearDay });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    /// <summary>⚠ WRITING IS `portal.company.manage`; a cashier's token must not move a VAT period.</summary>
    [Fact]
    public async Task A_till_operator_cannot_change_the_vat_periods()
    {
        var req = Req(HttpMethod.Put, "/api/v1/companies/vat-periods", "pos.sell");
        req.Content = JsonContent.Create(new { basis = "month", staggerEndMonth = (int?)null, yearStartMonth = 4, yearStartDay = 1 });

        var resp = await _f.CreateClient().SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── the report reads it ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE POINT OF THE WHOLE PACKAGE: **the report buckets on what the business filed on**, with
    /// nobody passing a parameter. This used to default to stagger 1 whatever the shop actually did.
    /// </summary>
    [Fact]
    public async Task Vat_corrections_defaults_to_the_business_setting()
    {
        await EnsureBusinessAsync();
        Assert.Equal(HttpStatusCode.NoContent,
            (await PutAsync(new { basis = "quarter", staggerEndMonth = 1, yearStartMonth = 4, yearStartDay = 1 })).StatusCode);

        var resp = await _f.CreateClient().SendAsync(Req(HttpMethod.Get, "/api/v1/reports/vat-corrections"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(1, body.GetProperty("staggerEndMonth").GetInt32());
        Assert.True(body.GetProperty("configured").GetBoolean());
    }

    /// <summary>
    /// ⚠⚠ REQUIREMENT 4 — **THE REPORT NAMES ITS BASIS, IN WORDS, ON THE WIRE.** A VAT report that
    /// does not say which periods it used cannot be checked against a filed return, which is the
    /// only thing it is for.
    /// </summary>
    [Fact]
    public async Task Vat_corrections_says_which_basis_it_used()
    {
        await EnsureBusinessAsync();
        Assert.Equal(HttpStatusCode.NoContent,
            (await PutAsync(new { basis = "quarter", staggerEndMonth = 2, yearStartMonth = 4, yearStartDay = 1 })).StatusCode);

        var resp = await _f.CreateClient().SendAsync(Req(HttpMethod.Get, "/api/v1/reports/vat-corrections"));
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        Assert.Contains("February", body.GetProperty("periodBasis").GetString());
    }

    /// <summary>
    /// ⚠ THE QUERY PARAMETERS STILL OVERRIDE, and they must: this endpoint is also how somebody
    /// tests what a different basis would produce — the exact question asked before changing the
    /// setting. What changed is that OMITTING them no longer means "stagger 1".
    /// </summary>
    [Fact]
    public async Task An_explicit_query_parameter_still_overrides_the_setting()
    {
        await EnsureBusinessAsync();
        Assert.Equal(HttpStatusCode.NoContent,
            (await PutAsync(new { basis = "quarter", staggerEndMonth = 1, yearStartMonth = 4, yearStartDay = 1 })).StatusCode);

        var resp = await _f.CreateClient().SendAsync(
            Req(HttpMethod.Get, "/api/v1/reports/vat-corrections?staggerEndMonth=3"));
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(3, body.GetProperty("staggerEndMonth").GetInt32());
    }

    /// <summary>⚠ A typed-in bad override is still a 400 — `Resolve`'s tolerance is for stored rows,
    /// not for callers.</summary>
    [Fact]
    public async Task A_bad_query_override_is_still_refused()
    {
        var resp = await _f.CreateClient().SendAsync(
            Req(HttpMethod.Get, "/api/v1/reports/vat-corrections?basis=fortnightly"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
