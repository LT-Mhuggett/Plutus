using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Services.Reporting;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Reporting
{
    /// <summary>
    /// The report FRAMEWORK — Matt, 2026-08-16: *"Can I check that you are writing the framework
    /// that new reports can just be dropped into MAUI? Not hardcoding them all into MAUI please."*
    ///
    /// ⚠⚠ THESE TESTS ARE THE ANSWER, AND THEY ARE WRITTEN TO STAY TRUE. Nothing here names a
    /// specific screen: they walk `ReportCatalogue.All` and assert the CONTRACT every definition
    /// keeps. Add a report and it is tested by these the moment it appears in the list; add one that
    /// breaks the shape and they fail without anybody remembering to check.
    /// </summary>
    public class ReportCatalogueTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
                => Task.FromResult(_respond(r));
        }

        private static PlutusApiClient Api(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://plutus.test") });

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

        private static readonly ReportQuery Query =
            new(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 16), StoreId: 4);

        // ── the framework contract, held by EVERY report ──────────────────────────────────────

        [Fact]
        public void The_catalogue_is_not_empty_and_every_report_is_uniquely_identified()
        {
            Assert.NotEmpty(ReportCatalogue.All);

            var keys = ReportCatalogue.All.Select(r => r.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());

            Assert.All(ReportCatalogue.All, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Key));
                Assert.False(string.IsNullOrWhiteSpace(r.Title));
                Assert.NotNull(r.LoadAsync);
            });
        }

        /// <summary>
        /// ⚠⚠ HEADERS AND NUMERIC FLAGS ARE POSITIONAL, and a mismatch is silent: the wrong column
        /// right-aligns and sorts as text. Every report is checked, including ones written later.
        /// </summary>
        [Fact]
        public async Task Every_report_declares_one_numeric_flag_per_header()
        {
            var api = Api(_ => Json("{}"));

            foreach (var report in ReportCatalogue.All)
            {
                var table = await report.LoadAsync(api, Query, default);

                Assert.Equal(table.Headers.Count, table.NumericColumns.Count);
            }
        }

        /// <summary>⚠ And every ROW has one cell per header — a short row would shift every value
        /// after it into the wrong column, which on a money report is a wrong number under a right
        /// heading.</summary>
        [Fact]
        public async Task Every_report_returns_rows_that_match_its_own_headers()
        {
            var api = Api(r => Json(SampleFor(r.RequestUri!.AbsoluteUri)));

            foreach (var report in ReportCatalogue.All)
            {
                var table = await report.LoadAsync(api, Query, default);

                Assert.All(table.Rows, row =>
                    Assert.Equal(table.Headers.Count, row.Cells.Count));
            }
        }

        /// <summary>
        /// ⚠⚠ A REFUSAL IS A SENTENCE, NOT AN EMPTY TABLE. These endpoints are gated on
        /// `portal.reports.view`, so a cashier without the grant gets a 403 — and a report rendering
        /// £0.00 would tell them the shop sold nothing today. Every report must say so instead.
        /// </summary>
        [Fact]
        public async Task Every_report_explains_itself_rather_than_showing_zero_when_it_cannot_be_read()
        {
            var api = Api(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

            foreach (var report in ReportCatalogue.All)
            {
                var table = await report.LoadAsync(api, Query, default);

                Assert.Empty(table.Rows);
                Assert.False(string.IsNullOrWhiteSpace(table.Note));
                Assert.Contains("permission", table.Note, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>⚠ And a report must never THROW at the screen — a reporting tab that crashes the
        /// till is worse than one that says it has nothing.</summary>
        [Fact]
        public async Task No_report_throws_when_the_server_answers_with_nonsense()
        {
            var api = Api(_ => Json("{}"));

            foreach (var report in ReportCatalogue.All)
            {
                var table = await report.LoadAsync(api, Query, default);
                Assert.NotNull(table);
            }
        }

        // ── the money, per report ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE POUNDS REPORT. `summary-rich` sends 97.94 meaning £97.94. Read as pence it would
        /// render as £0.98 — a day's takings at a hundredth of what was banked.
        /// </summary>
        [Fact]
        public async Task The_takings_report_reads_pounds_and_renders_them_as_pounds()
        {
            var api = Api(_ => Json(
                """{"totalSales":97.94,"totalSalesExTax":81.62,"totalOrders":3,"byDay":[{"date":"2026-08-16","total":97.94,"totalExTax":81.62,"orders":3}]}"""));

            var table = await ReportCatalogue.All.Single(r => r.Key == "summary")
                .LoadAsync(api, Query, default);

            var row = Assert.Single(table.Rows);
            Assert.Contains("97.94", row.Cells.Last().Text);
            // ⚠ And the SORT key is scaled to pence so the column orders as a number.
            Assert.Equal(9794, row.Cells.Last().SortNumber);
        }

        /// <summary>⚠ THE PENCE REPORTS. 9794 pence is the same £97.94 — a hundred times larger on
        /// the wire, and it must render identically.</summary>
        [Fact]
        public async Task The_vat_report_reads_pence_and_renders_the_same_money()
        {
            var api = Api(_ => Json(
                """{"totals":{"grossPence":9794,"netPence":8162,"vatPence":1632},"buckets":[{"period":"2026-08","vatRateBp":2000,"grossPence":9794,"netPence":8162,"vatPence":1632}]}"""));

            var table = await ReportCatalogue.All.Single(r => r.Key == "vat")
                .LoadAsync(api, Query, default);

            var row = Assert.Single(table.Rows);
            Assert.Contains("97.94", row.Cells.Last().Text);
            Assert.Equal(9794, row.Cells.Last().SortNumber);

            // ⚠ Basis points shown as a percentage — 2000 is 20%, not 2000%.
            Assert.Equal("20%", row.Cells[1].Text);
        }

        /// <summary>
        /// ⚠⚠ THE SILENT CAP, MADE LOUD. The server matches 5,000 lines and returns 2,000; a total
        /// that looks complete but is not is worse than no report. Step 26 requires this be visible.
        /// </summary>
        [Fact]
        public async Task Items_sold_says_so_when_the_server_capped_the_rows()
        {
            var api = Api(_ => Json(
                """{"count":5000,"totals":{"qty":9,"grossPence":9794,"discountPence":0},"rows":[{"itemIdOne":"A","itemName":"Thing","qty":1,"lineGrossPence":100}]}"""));

            var table = await ReportCatalogue.All.Single(r => r.Key == "items-sold")
                .LoadAsync(api, Query, default);

            Assert.Contains("5,000", table.Note);
            Assert.Contains("1", table.Note);
        }

        [Fact]
        public async Task Items_sold_is_quiet_when_nothing_was_capped()
        {
            var api = Api(_ => Json(
                """{"count":1,"totals":{"qty":1,"grossPence":100,"discountPence":0},"rows":[{"itemIdOne":"A","itemName":"Thing","qty":1,"lineGrossPence":100}]}"""));

            var table = await ReportCatalogue.All.Single(r => r.Key == "items-sold")
                .LoadAsync(api, Query, default);

            Assert.True(string.IsNullOrEmpty(table.Note));
        }

        /// <summary>⚠ `sharePct` is ALREADY a percentage — rendering it as a fraction would report
        /// every category at a hundredth of its share.</summary>
        [Fact]
        public async Task Category_share_is_rendered_as_the_percentage_the_server_sent()
        {
            var api = Api(_ => Json(
                """{"totals":{"grossPence":10000,"qty":10,"categories":1},"rows":[{"category":"Comics","qty":8,"grossPence":8000,"discountPence":0,"sharePct":80.0}]}"""));

            var table = await ReportCatalogue.All.Single(r => r.Key == "category-sales")
                .LoadAsync(api, Query, default);

            Assert.Contains("80%", Assert.Single(table.Rows).Cells[2].Text);
        }

        // ── the shape helper ──────────────────────────────────────────────────────────────────

        /// <summary>⚠ A definition that declares mismatched headers and flags fails LOUDLY at build
        /// time rather than quietly right-aligning the wrong column.</summary>
        [Fact]
        public void Building_a_table_with_mismatched_headers_and_flags_is_refused()
        {
            Assert.Throws<ArgumentException>(() => ReportTable.From(
                new[] { 1 },
                new[] { "A", "B" },
                new[] { true },
                _ => new[] { new ReportCell("x") }));
        }

        /// <summary>⚠ The search box matches ANY cell — an operator finds a row by whatever they can
        /// see on it, rather than guessing which column is searchable.</summary>
        [Fact]
        public void A_row_is_searchable_by_every_cell_it_shows()
        {
            var row = new ReportRow(new[] { new ReportCell("Batman"), new ReportCell("Till 2") });

            Assert.Contains("Batman", row.SearchText);
            Assert.Contains("Till 2", row.SearchText);
        }


        /// <summary>
        /// ⚠⚠ THE NEGATIVE-STOCK REPORT'S WHOLE SUBJECT IS A NEGATIVE NUMBER, and two things could
        /// quietly destroy it: clamping the quantity at zero (the report becomes empty and the fault
        /// becomes a silence), or letting the column sort as TEXT — where "−28508" and "−3" order by
        /// their first character and the worst offenders hide in the middle of the list.
        ///
        /// ⚠ Kapow's real data has items at **−28,508**: sales decremented stock for seven years
        /// while goods-in was never recorded. That is the figure used here on purpose.
        /// </summary>
        [Fact]
        public async Task Negative_stock_keeps_the_sign_and_sorts_as_a_number()
        {
            var api = Api(_ => Json(
                """{"totalCatalogueItems":9,"inStock":0,"matched":1,"skip":0,"take":200,"rows":[{"itemIdOne":"BACKISSUE","name":"Back issues","category":"Comics","location":"Shop floor","quantity":-28508}]}"""));

            var table = await ReportCatalogue.All.Single(r => r.Key == "negative-stock")
                .LoadAsync(api, Query, default);

            var row = Assert.Single(table.Rows);

            // ⚠ The SORT key is the signed quantity itself — not its text, and not its absolute value.
            Assert.Equal(-28508, row.Cells.Last().SortNumber);
            Assert.Contains("28,508", row.Cells.Last().Text);
            Assert.Contains("-", row.Cells.Last().Text);

            // ⚠ And it is the only numeric column: Item, Category, Location are all text.
            Assert.Equal(new[] { false, false, false, true }, table.NumericColumns);
        }

        /// <summary>
        /// ⚠⚠ AN EMPTY NEGATIVE-STOCK REPORT IS GOOD NEWS, and it must not look like a failed read.
        /// "No rows" and "you may not have permission" are opposite answers, and on this report the
        /// good one is the one that will normally happen once a shop is straight.
        /// </summary>
        [Fact]
        public async Task An_empty_negative_stock_report_says_nothing_is_below_zero()
        {
            var api = Api(_ => Json("""{"totalCatalogueItems":9,"inStock":9,"matched":0,"skip":0,"take":200,"rows":[]}"""));

            var table = await ReportCatalogue.All.Single(r => r.Key == "negative-stock")
                .LoadAsync(api, Query, default);

            Assert.Empty(table.Rows);
            Assert.Contains("below zero", table.Note, StringComparison.OrdinalIgnoreCase);
            // ⚠ And it must NOT read as a refusal — that is the sentence the 403 path uses.
            Assert.DoesNotContain("permission", table.Note, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// ⚠⚠ STOCK IGNORES THE DATE RANGE, AND MUST SAY SO. On-hand stock is a fact about NOW — a
        /// materialised sum of the whole movement ledger — but the reports screen shows From/To
        /// pickers above every report. A report that silently ignores the dates above it invites
        /// somebody to believe they asked for last week's stock and got it.
        /// </summary>
        [Fact]
        public async Task Both_stock_reports_say_the_dates_do_not_apply()
        {
            var api = Api(_ => Json(
                """{"totalCatalogueItems":9,"inStock":1,"matched":1,"skip":0,"take":200,"rows":[{"itemIdOne":"A","name":"Thing","quantity":4}]}"""));

            foreach (var key in new[] { "stock", "negative-stock" })
            {
                var table = await ReportCatalogue.All.Single(r => r.Key == key)
                    .LoadAsync(api, Query, default);

                Assert.Contains("date range above does not apply", table.Note);
            }
        }

        /// <summary>
        /// ⚠⚠ THE SILENT CAP AGAIN, on a second report. The server clamps `take` to 200 and answers
        /// `matched` with the truth — so a shop with 4,000 stock rows sees 200 and, without this,
        /// would read the totals line as the whole picture. Same rule as `items-sold`.
        /// </summary>
        [Fact]
        public async Task Stock_says_so_when_the_server_capped_the_rows()
        {
            var api = Api(_ => Json(
                """{"totalCatalogueItems":9000,"inStock":3000,"matched":4000,"skip":0,"take":200,"rows":[{"itemIdOne":"A","name":"Thing","quantity":1}]}"""));

            var table = await ReportCatalogue.All.Single(r => r.Key == "stock")
                .LoadAsync(api, Query, default);

            Assert.Contains("4,000", table.Note);
            Assert.Contains("200 at a time", table.Note);
        }

        /// <summary>
        /// ⚠⚠ THE FILTER IS THE ONLY THING THAT MAKES THESE TWO REPORTS DIFFERENT, and if it
        /// were dropped the "Negative stock" report would return EVERY stock row — which, under that
        /// heading, reads as "every item in the shop is below zero". A worse answer than an error.
        ///
        /// ⚠ Found by mutation: clamping the sign or losing the sort key both died against the
        /// tests above, but sending the wrong query string did not, because a fake that answers every
        /// URL identically cannot tell the two reports apart. So this asserts the URL itself.
        /// </summary>
        [Fact]
        public async Task Only_the_negative_report_asks_the_server_to_filter()
        {
            foreach (var (key, shouldFilter) in new[] { ("stock", false), ("negative-stock", true) })
            {
                string? asked = null;
                var api = Api(r =>
                {
                    asked = r.RequestUri!.AbsoluteUri;
                    return Json("""{"matched":0,"rows":[]}""");
                });

                await ReportCatalogue.All.Single(r => r.Key == key).LoadAsync(api, Query, default);

                Assert.NotNull(asked);
                Assert.Contains("/api/v1/stock/levels", asked);
                Assert.Equal(shouldFilter, asked!.Contains("filter=negative", StringComparison.Ordinal));
            }
        }
        private static string SampleFor(string url) =>
            url.Contains("summary-rich") ? """{"totalSales":1,"totalOrders":1,"byDay":[{"date":"d","total":1,"totalExTax":1,"orders":1}]}"""
            : url.Contains("/vat") ? """{"totals":{},"buckets":[{"period":"p","vatRateBp":2000,"grossPence":1,"netPence":1,"vatPence":1}]}"""
            : url.Contains("items-sold") ? """{"count":1,"totals":{},"rows":[{"itemIdOne":"A","qty":1,"lineGrossPence":1}]}"""
            : url.Contains("category-sales") ? """{"totals":{},"rows":[{"category":"C","qty":1,"grossPence":1,"sharePct":1}]}"""
            : url.Contains("best-sellers") ? """{"rows":[{"rank":1,"itemIdOne":"A","qty":1,"grossPence":1,"sharePct":1}]}"""
            // ⚠⚠ STOCK IS NOT MONEY, AND THE QUANTITY IS NEGATIVE ON PURPOSE. Without a
            // sample here the two stock reports return an EMPTY table for every contract test
            // above, so their headers, their numeric flags and their row shape would all be
            // "verified" by a loop that never saw a row. Kapow really does have items at -28,508.
            : url.Contains("stock/levels") ? """{"totalCatalogueItems":9,"inStock":2,"matched":1,"skip":0,"take":200,"rows":[{"itemIdOne":"A","name":"Thing","category":"C","location":"Shop floor","quantity":-28508}]}"""
            : "{}";
    }
}
