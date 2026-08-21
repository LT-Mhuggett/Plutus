using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Enums;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// **WP-ZERO — a day with no sales is a zero, not a gap.**
///
/// ⚠⚠ MATT, 2026-08-21, with a screenshot of the dashboard chart: *"The reports still have to show
/// ALL days, even ones where no sales were made e.g. this graph jumps from the 15th to the 17th."*
///
/// `summary-rich`'s `byDay` was `sales.GroupBy(BusinessDay)`, so a day the shop was shut produced no
/// group and therefore no row. The chart then drew the next bar hard against the last one and **the
/// axis silently lied about the interval** — 15 Aug beside 17 Aug reads as two consecutive days.
///
/// ⚠⚠ THE REASON THIS IS A TEST AND NOT A TIDY-UP: the same gap hides **a till that stopped
/// syncing**. A quiet Sunday and a dead till look identical on a chart with no zero bars, and a
/// daily takings chart exists precisely to make the second one obvious.
///
/// ⚠ THREE CONSUMERS read this series — the portal's SummaryReport, the portal Dashboard, and MAUI's
/// **Takings** report. Fixing it in one client would have left the other two wrong, so it is fixed
/// in the endpoint and pinned here.
/// </summary>
public class DailySeriesGapE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public DailySeriesGapE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    // ⚠ AN ISOLATED WINDOW NOTHING ELSE SEEDS INTO, so the assertions can be exact. The middle day
    // is deliberately left empty — it is the whole point of the test.
    private static readonly DateOnly First = new(2018, 3, 12);
    private static readonly DateOnly Empty = new(2018, 3, 13);
    private static readonly DateOnly Last = new(2018, 3, 14);

    private async Task SeedAsync(DateOnly day, long grossPence, long vatPence)
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "daily-series-e2e-seed";

        db.SalesV2.Add(new SaleV2
        {
            Id = Uuid7.New(), TenantId = Kapow, TillId = Uuid7.New(), DeviceId = Uuid7.New(), DeviceSeq = 1,
            Channel = SaleChannel.Till, BusinessDay = day,
            OccurredAtUtc = day.ToDateTime(TimeOnly.MinValue), ReceivedAtUtc = day.ToDateTime(TimeOnly.MinValue),
            GrossPence = grossPence, VatPence = vatPence,
        });

        await db.SaveChangesAsync();
    }

    private HttpRequestMessage Ask(DateOnly from, DateOnly to)
    {
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/reports/summary-rich?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin, Kapow));
        return req;
    }

    private async Task<JsonElement> ReadAsync(DateOnly from, DateOnly to)
    {
        var resp = await _f.CreateClient().SendAsync(Ask(from, to));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task A_day_with_no_sales_is_a_zero_row_not_a_missing_one()
    {
        await SeedAsync(First, 1200, 200);
        await SeedAsync(Last, 2400, 400);

        var body = await ReadAsync(First, Last);
        var days = body.GetProperty("byDay").EnumerateArray()
            .Select(d => d.GetProperty("date").GetString()!)
            .ToList();

        // ⚠⚠ THE REGRESSION. Three days asked for, three days answered — the middle one had no sales.
        Assert.Equal(
            new[] { $"{First:yyyy-MM-dd}", $"{Empty:yyyy-MM-dd}", $"{Last:yyyy-MM-dd}" },
            days);
    }

    [Fact]
    public async Task The_empty_day_reads_as_zero_takings_and_zero_orders()
    {
        await SeedAsync(First, 1200, 200);
        await SeedAsync(Last, 2400, 400);

        var body = await ReadAsync(First, Last);
        var empty = body.GetProperty("byDay").EnumerateArray()
            .Single(d => d.GetProperty("date").GetString() == $"{Empty:yyyy-MM-dd}");

        // ⚠ A REAL 0.00, NOT A NULL. A client must be able to tell "the shop took nothing" from "we
        // have no data for this day", and only one of those is what an empty day means.
        Assert.Equal(0m, empty.GetProperty("total").GetDecimal());
        Assert.Equal(0m, empty.GetProperty("totalExTax").GetDecimal());
        Assert.Equal(0, empty.GetProperty("orders").GetInt32());
    }

    /// <summary>
    /// ⚠⚠ THE ARITHMETIC MUST NOT MOVE. Padding a series is exactly the kind of change that quietly
    /// double-counts or drops a day, and this endpoint answers a shop's takings. The series still has
    /// to sum to the header, and the header has to be what it always was.
    /// </summary>
    [Fact]
    public async Task Padding_the_series_does_not_change_the_totals()
    {
        await SeedAsync(First, 1200, 200);
        await SeedAsync(Last, 2400, 400);

        var body = await ReadAsync(First, Last);

        var seriesTotal = body.GetProperty("byDay").EnumerateArray()
            .Sum(d => d.GetProperty("total").GetDecimal());
        var seriesOrders = body.GetProperty("byDay").EnumerateArray()
            .Sum(d => d.GetProperty("orders").GetInt32());

        Assert.Equal(36.00m, seriesTotal);
        Assert.Equal(36.00m, body.GetProperty("totalSales").GetDecimal());
        Assert.Equal(2, seriesOrders);
        Assert.Equal(2, body.GetProperty("totalOrders").GetInt32());
    }

    /// <summary>⚠ A range with NO sales at all is a row per day of zeros, not an empty array. An empty
    /// array renders as "no data" and a week of zeros renders as a shut week; the shop asking this
    /// question is trying to tell those two apart.</summary>
    [Fact]
    public async Task A_range_with_no_sales_at_all_is_all_zeros_not_empty()
    {
        var body = await ReadAsync(new DateOnly(2017, 9, 4), new DateOnly(2017, 9, 6));
        var days = body.GetProperty("byDay").EnumerateArray().ToList();

        Assert.Equal(3, days.Count);
        Assert.All(days, d => Assert.Equal(0, d.GetProperty("orders").GetInt32()));
    }

    /// <summary>⚠ A single-day range is ONE row, not zero and not two — the off-by-one this kind of
    /// enumeration invites, and the range the till's own Takings report asks for most.</summary>
    [Fact]
    public async Task A_single_day_range_is_exactly_one_row()
    {
        var day = new DateOnly(2017, 9, 20);

        var body = await ReadAsync(day, day);
        var days = body.GetProperty("byDay").EnumerateArray().ToList();

        Assert.Single(days);
        Assert.Equal($"{day:yyyy-MM-dd}", days[0].GetProperty("date").GetString());
    }
}
