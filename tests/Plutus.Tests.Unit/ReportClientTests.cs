using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The reporting client (WP11 / step 26).
///
/// ⚠⚠ THE TRAP THESE EXIST FOR: `/reports/summary-rich` answers in **POUNDS** and every other report
/// answers in **PENCE**. The JSON gives no clue — `total` and `grossPence` look equally plausible on
/// a takings figure — and the two appear on ONE screen. Binding default 17 puts the unit in the
/// name; these prove the mapping actually lands that way rather than merely being named well.
///
/// ⚠ MAUI's Statistics tab has reported ZERO for everything sold since cutover step 11, because it
/// reads local SQLite and sales stopped going there. These calls replace it.
/// </summary>
public class ReportClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Sent { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private static PlutusApiClient Api(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new PlutusApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://plutus.test") });
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 16);

    // ── the units ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ POUNDS. `summary-rich` sends `"totalSales": 97.94` meaning **£97.94**, because
    /// `ReportsController.cs:241` divides by 100 before serialising. Read as pence that is 97p, and
    /// a day's takings are reported at a hundredth of what was banked.
    /// </summary>
    [Fact]
    public async Task Summary_rich_is_read_as_POUNDS()
    {
        var api = Api(_ => Json(
            """{"totalSales":97.94,"totalSalesExTax":81.62,"totalOrders":3,"byDay":[{"date":"2026-08-16","total":97.94,"totalExTax":81.62,"orders":3}],"byTaxRate":[{"tax":"Standard","gross":97.94,"net":81.62,"vat":16.32}]}"""), out _);

        var summary = await api.GetSalesSummaryAsync(From, To);

        Assert.Equal(97.94m, summary!.TotalSalesPounds);
        Assert.Equal(81.62m, summary.TotalSalesExTaxPounds);
        Assert.Equal(97.94m, summary.ByDay[0].TotalPounds);
        Assert.Equal(16.32m, summary.ByTaxRate[0].VatPounds);
    }

    /// <summary>
    /// ⚠⚠ PENCE, on the very next report. `9794` here means **£97.94** — the same money as the test
    /// above, expressed a hundred times larger. Both figures can appear on one screen.
    /// </summary>
    [Fact]
    public async Task Every_other_report_is_read_as_PENCE()
    {
        var api = Api(_ => Json(
            """{"totals":{"grossPence":9794,"vatPence":1632,"txnCount":3,"avgBasketPence":3265}, "buckets":[{"period":"2026-08-16","grossPence":9794,"vatPence":1632,"txnCount":3,"avgBasketPence":3265}]}"""),
            out _);

        var summary = await api.GetReportSummaryAsync("store", "4", From, To);

        Assert.Equal(9794, summary!.Totals.GrossPence);
        Assert.Equal(3265, summary.Totals.AvgBasketPence);
        Assert.Equal(9794, summary.Buckets[0].GrossPence);
    }

    /// <summary>⚠ And the VAT report too — plus `vatRateBp` is BASIS POINTS: 2000 is 20%.</summary>
    [Fact]
    public async Task The_vat_report_is_pence_and_its_rate_is_basis_points()
    {
        var api = Api(_ => Json(
            """{"totals":{"grossPence":9794,"netPence":8162,"vatPence":1632}, "buckets":[{"period":"2026-08","vatRateBp":2000,"grossPence":9794,"netPence":8162,"vatPence":1632}]}"""),
            out _);

        var vat = await api.GetReportVatAsync(storeId: 4, From, To);

        Assert.Equal(1632, vat!.Totals.VatPence);
        Assert.Equal(2000, vat.Buckets[0].VatRateBp);
    }

    // ── the URLs ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ `yyyy-MM-dd` AND INVARIANT. A till in a culture that formats dates differently would query
    /// a different range and report someone else's week without erroring.
    /// </summary>
    [Fact]
    public async Task Dates_go_on_the_wire_as_invariant_yyyy_MM_dd()
    {
        var api = Api(_ => Json("{}"), out var h);

        await api.GetSalesSummaryAsync(new DateOnly(2026, 1, 5), new DateOnly(2026, 12, 31));

        Assert.Contains("from=2026-01-05", h.Sent[0].RequestUri!.AbsoluteUri);
        Assert.Contains("to=2026-12-31", h.Sent[0].RequestUri!.AbsoluteUri);
    }

    /// <summary>⚠ Summary buckets by DAY, VAT by MONTH — matching the web till, so the same range
    /// produces the same buckets on both surfaces rather than two different-looking reports.</summary>
    [Fact]
    public async Task The_default_granularities_match_the_web_till()
    {
        var api = Api(_ => Json("{}"), out var h);

        await api.GetReportSummaryAsync("store", "4", From, To);
        await api.GetReportVatAsync(4, From, To);

        Assert.Contains("granularity=day", h.Sent[0].RequestUri!.AbsoluteUri);
        Assert.Contains("granularity=month", h.Sent[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task The_summary_and_vat_reports_are_scoped_to_this_store()
    {
        var api = Api(_ => Json("{}"), out var h);

        await api.GetReportSummaryAsync("store", "4", From, To);

        Assert.Contains("level=store", h.Sent[0].RequestUri!.AbsoluteUri);
        Assert.Contains("id=4", h.Sent[0].RequestUri!.AbsoluteUri);
    }

    /// <summary>⚠ The row cap matches the web till's 2000, so the two truncate at the same point
    /// rather than disagreeing about a total.</summary>
    [Fact]
    public async Task Items_sold_asks_for_the_same_row_cap_as_the_web_till()
    {
        var api = Api(_ => Json("{}"), out var h);

        await api.GetItemsSoldAsync(storeId: 4, From, To);

        Assert.Equal(2000, PlutusApiClient.ItemsSoldRowCap);
        Assert.Contains("take=2000", h.Sent[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Items_sold_can_be_filtered_to_one_operator()
    {
        var api = Api(_ => Json("{}"), out var h);
        var op = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await api.GetItemsSoldAsync(4, From, To, op);

        Assert.Contains($"operatorUserId={op:D}", h.Sent[0].RequestUri!.AbsoluteUri);
    }

    // ── the caps that are otherwise silent ────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE CAP IS SILENT AND THE CALLER MUST SEE IT. `count` is the server's tally of MATCHING
    /// rows; `rows` is what fitted. A busy fortnight exceeds 2000 and the report then shows a total
    /// that is quietly short — which is why the two are exposed separately rather than being
    /// flattened into a list.
    /// </summary>
    [Fact]
    public async Task Items_sold_reports_the_matching_count_separately_from_the_rows_returned()
    {
        var api = Api(_ => Json(
            """{"count":5000,"totals":{"qty":9,"grossPence":9794,"discountPence":0}, "rows":[{"itemIdOne":"A","qty":1,"lineGrossPence":100}]}"""), out _);

        var sold = await api.GetItemsSoldAsync(4, From, To);

        Assert.Equal(5000, sold!.Count);
        Assert.Single(sold.Rows);          // ⚠ 5000 matched, one came back — the caller must say so
    }

    // ── the two new reports ───────────────────────────────────────────────────────────────────

    /// <summary>⚠ `sharePct` is a PERCENTAGE the server already computed — not a fraction. Dividing
    /// it by 100 again would report every category at a hundredth of its share.</summary>
    [Fact]
    public async Task Category_sales_carries_a_percentage_not_a_fraction()
    {
        var api = Api(_ => Json(
            """{"totals":{"grossPence":10000,"qty":10,"categories":2}, "rows":[{"category":"Comics","qty":8,"grossPence":8000,"discountPence":0,"sharePct":80.0}]}"""),
            out _);

        var cats = await api.GetCategorySalesAsync(From, To);

        Assert.Equal(80.0m, cats!.Rows[0].SharePct);
        Assert.Equal(8000, cats.Rows[0].GrossPence);
    }

    /// <summary>⚠ Best sellers rank by QTY or by GROSS and the two differ — a shop's best seller by
    /// units is rarely its best by money, and both are legitimate questions.</summary>
    [Theory]
    [InlineData("qty")]
    [InlineData("gross")]
    public async Task Best_sellers_can_be_ranked_either_way(string by)
    {
        var api = Api(_ => Json("""{"rows":[{"rank":1,"itemIdOne":"A","qty":9,"grossPence":9794,"sharePct":100}]}"""),
            out var h);

        var best = await api.GetBestSellersAsync(From, To, by);

        Assert.Contains($"by={by}", h.Sent[0].RequestUri!.AbsoluteUri);
        Assert.Equal(1, best!.Rows[0].Rank);
    }

    // ── failure ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ A 403 READS AS NOTHING, NOT AS ZERO. These endpoints are gated on
    /// `portal.reports.view` / `portal.financials.view`, so a cashier without the grant gets a 403 —
    /// and a report that rendered £0.00 takings would tell them the shop sold nothing today.
    /// </summary>
    [Fact]
    public async Task A_report_the_operator_may_not_see_is_null_rather_than_zero()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), out _);

        Assert.Null(await api.GetSalesSummaryAsync(From, To));
        Assert.Null(await api.GetReportSummaryAsync("store", "4", From, To));
        Assert.Null(await api.GetReportVatAsync(4, From, To));
    }
}
