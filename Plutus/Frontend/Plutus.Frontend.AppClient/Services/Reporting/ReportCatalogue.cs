using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;

namespace Plutus.Frontend.AppClient.Services.Reporting
{
    /// <summary>What the operator has asked for.</summary>
    /// <param name="StoreId">⚠ Some endpoints are store-scoped and some are tenant-wide. The
    /// definition decides; the screen just supplies what it knows.</param>
    public sealed record ReportQuery(DateOnly From, DateOnly To, int StoreId);

    /// <summary>
    /// ONE REPORT, DECLARED — a title and how to turn a query into a table. Nothing else.
    ///
    /// ⚠⚠ THIS IS THE POINT OF THE WHOLE FILE. Matt, 2026-08-16: *"Can I check that you are writing
    /// the framework that new reports can just be dropped into MAUI? Not hardcoding them all into
    /// MAUI please."*
    ///
    /// **Adding a report is ONE ENTRY in <see cref="ReportCatalogue.All"/>** — no screen, no
    /// viewmodel, no XAML, no navigation, no tab. The generic reports screen renders whatever the
    /// catalogue contains, and `TillTable` gives every one of them sorting, search and paging for
    /// free.
    /// </summary>
    /// <param name="Key">Stable id — used for the picker and for remembering a choice. Never shown.</param>
    /// <param name="Title">What the operator picks from the list.</param>
    /// <param name="LoadAsync">Query in, table out. ⚠ Return <see cref="ReportTable.Empty"/> with a
    /// NOTE rather than throwing: a report that cannot be read must say why, on the screen, in words
    /// an operator can act on.</param>
    public sealed record ReportDefinition(
        string Key,
        string Title,
        Func<PlutusApiClient, ReportQuery, CancellationToken, Task<ReportTable>> LoadAsync);

    /// <summary>
    /// Every report this till can show.
    ///
    /// ⚠⚠ TO ADD A REPORT, ADD AN ENTRY HERE. That is the entire procedure. If a new report ever
    /// needs a screen of its own, something has gone wrong with this design rather than with the
    /// report — say so rather than working around it.
    ///
    /// ⚠ THE UNITS ARE THE TRAP, EVERY TIME. `summary-rich` answers in POUNDS and every other report
    /// in PENCE (binding default 17). The contract type names carry it; these definitions are where
    /// a mistake would actually reach a screen, so each one says which it is handling.
    ///
    /// ⚠ NOTHING HERE READS LOCAL SQLITE. MAUI's old Statistics tab did, which is why it has
    /// reported ZERO for everything sold since cutover step 11 — sales stopped being written there.
    /// </summary>
    public static class ReportCatalogue
    {
        public static IReadOnlyList<ReportDefinition> All { get; } = new[]
        {
            Summary(),
            Vat(),
            ItemsSold(),
            CategorySales(),
            BestSellers(),
            Stock(),
            NegativeStock(),
        };

        private static string Gbp(long pence) =>
            (pence / 100m).ToString("C2", CultureInfo.CurrentCulture);

        private static string Gbp(decimal pounds) =>
            pounds.ToString("C2", CultureInfo.CurrentCulture);

        /// <summary>Takings by day. ⚠ POUNDS — this is the one.</summary>
        private static ReportDefinition Summary() => new(
            "summary", "Takings",
            async (api, q, ct) =>
            {
                var data = await api.GetSalesSummaryAsync(q.From, q.To, ct).ConfigureAwait(false);
                if (data is null) return ReportTable.Empty(Unreadable);

                return ReportTable.From(
                    data.ByDay,
                    new[] { "Date", "Orders", "Ex VAT", "Total" },
                    new[] { false, true, true, true },
                    d => new[]
                    {
                        new ReportCell(d.Date ?? ""),
                        new ReportCell(d.Orders.ToString(CultureInfo.CurrentCulture), true, d.Orders),
                        // ⚠ POUNDS in, so the sort key is scaled to pence to stay a whole number.
                        new ReportCell(Gbp(d.TotalExTaxPounds), true, (long)(d.TotalExTaxPounds * 100m)),
                        new ReportCell(Gbp(d.TotalPounds), true, (long)(d.TotalPounds * 100m)),
                    },
                    totals: $"{data.TotalOrders} orders · {Gbp(data.TotalSalesPounds)} " +
                            $"({Gbp(data.TotalSalesExTaxPounds)} ex VAT)");
            });

        /// <summary>VAT by band. ⚠ PENCE, and `vatRateBp` is basis points.</summary>
        private static ReportDefinition Vat() => new(
            "vat", "VAT",
            async (api, q, ct) =>
            {
                var data = await api.GetReportVatAsync(q.StoreId, q.From, q.To, ct: ct).ConfigureAwait(false);
                if (data is null) return ReportTable.Empty(Unreadable);

                return ReportTable.From(
                    data.Buckets,
                    new[] { "Period", "Rate", "Net", "VAT", "Gross" },
                    new[] { false, true, true, true, true },
                    b => new[]
                    {
                        new ReportCell(b.Period ?? ""),
                        // ⚠ BASIS POINTS → a percentage for display only. 2000 is 20%.
                        new ReportCell($"{b.VatRateBp / 100m:0.##}%", true, b.VatRateBp),
                        new ReportCell(Gbp(b.NetPence), true, b.NetPence),
                        new ReportCell(Gbp(b.VatPence), true, b.VatPence),
                        new ReportCell(Gbp(b.GrossPence), true, b.GrossPence),
                    },
                    totals: $"Net {Gbp(data.Totals.NetPence)} · VAT {Gbp(data.Totals.VatPence)} · " +
                            $"Gross {Gbp(data.Totals.GrossPence)}");
            });

        /// <summary>Every line sold. ⚠ PENCE, and the server cap must be SAID.</summary>
        private static ReportDefinition ItemsSold() => new(
            "items-sold", "Items sold",
            async (api, q, ct) =>
            {
                var data = await api.GetItemsSoldAsync(q.StoreId, q.From, q.To, ct: ct).ConfigureAwait(false);
                if (data is null) return ReportTable.Empty(Unreadable);

                // ⚠⚠ THE CAP IS SILENT ON THE WIRE AND MUST NOT BE ON THE SCREEN. `count` is what
                // matched; `rows` is what fitted. A total that looks complete but is not is worse
                // than no report at all — step 26 requires this be visible.
                var note = data.Count > data.Rows.Count
                    ? $"⚠ Showing {data.Rows.Count:N0} of {data.Count:N0} lines — narrow the dates to see the rest."
                    : string.Empty;

                return ReportTable.From(
                    data.Rows,
                    new[] { "Date", "Item", "Till", "Qty", "Gross" },
                    new[] { false, false, false, true, true },
                    r => new[]
                    {
                        new ReportCell(r.DateSold ?? ""),
                        new ReportCell(r.ItemName ?? r.ItemIdOne ?? ""),
                        new ReportCell(r.TillName ?? ""),
                        new ReportCell(r.Qty.ToString(CultureInfo.CurrentCulture), true, r.Qty),
                        new ReportCell(Gbp(r.LineGrossPence), true, r.LineGrossPence),
                    },
                    note,
                    $"{data.Totals.Qty:N0} items · {Gbp(data.Totals.GrossPence)}");
            });

        /// <summary>Sales by category — new to MAUI. ⚠ PENCE, and `sharePct` is already a percentage.</summary>
        private static ReportDefinition CategorySales() => new(
            "category-sales", "By category",
            async (api, q, ct) =>
            {
                var data = await api.GetCategorySalesAsync(q.From, q.To, ct).ConfigureAwait(false);
                if (data is null) return ReportTable.Empty(Unreadable);

                return ReportTable.From(
                    data.Rows,
                    new[] { "Category", "Qty", "Share", "Gross" },
                    new[] { false, true, true, true },
                    r => new[]
                    {
                        new ReportCell(r.Category ?? "(none)"),
                        new ReportCell(r.Qty.ToString(CultureInfo.CurrentCulture), true, r.Qty),
                        // ⚠ ALREADY A PERCENTAGE — not a fraction. Dividing again reports every
                        // category at a hundredth of its share.
                        new ReportCell($"{r.SharePct:0.#}%", true, (long)(r.SharePct * 100m)),
                        new ReportCell(Gbp(r.GrossPence), true, r.GrossPence),
                    },
                    totals: $"{data.Totals.Categories} categories · {Gbp(data.Totals.GrossPence)}");
            });

        /// <summary>Best sellers — also new to MAUI. ⚠ PENCE.</summary>
        private static ReportDefinition BestSellers() => new(
            "best-sellers", "Best sellers",
            async (api, q, ct) =>
            {
                var data = await api.GetBestSellersAsync(q.From, q.To, ct: ct).ConfigureAwait(false);
                if (data is null) return ReportTable.Empty(Unreadable);

                return ReportTable.From(
                    data.Rows,
                    new[] { "#", "Item", "Qty", "Share", "Gross" },
                    new[] { true, false, true, true, true },
                    r => new[]
                    {
                        new ReportCell(r.Rank.ToString(CultureInfo.CurrentCulture), true, r.Rank),
                        new ReportCell(r.ItemName ?? r.ItemIdOne ?? ""),
                        new ReportCell(r.Qty.ToString(CultureInfo.CurrentCulture), true, r.Qty),
                        new ReportCell($"{r.SharePct:0.#}%", true, (long)(r.SharePct * 100m)),
                        new ReportCell(Gbp(r.GrossPence), true, r.GrossPence),
                    });
            });


        /// <summary>
        /// On-hand stock. ⚠⚠ **QUANTITIES, NOT PENCE — the only report here that is not money**, and
        /// the units note at the top of this class is about the other five.
        ///
        /// ⚠ Closes one of the three gaps §5c item 5 found against the web till, which has had
        /// **Stock** and **Negative stock** since it was written.
        /// </summary>
        private static ReportDefinition Stock() => new(
            "stock", "Stock",
            (api, q, ct) => StockPage(api, negativeOnly: false, ct));

        /// <summary>
        /// Everything that has gone below zero.
        ///
        /// ⚠⚠ **NOT A CURIOSITY — IT IS THE ONE STOCK REPORT THIS SHOP NEEDS.** Kapow's legacy data
        /// has items at **−28,508**: sales decremented stock for seven years while goods-in was never
        /// recorded. A negative on-hand figure is a shelf nobody can trust, and this is the list of
        /// them. ⚠ The quantities are NEVER clamped to zero anywhere on the way here — a clamp would
        /// turn this report into an empty one and the fault into a silence.
        /// </summary>
        private static ReportDefinition NegativeStock() => new(
            "negative-stock", "Negative stock",
            (api, q, ct) => StockPage(api, negativeOnly: true, ct));

        /// <summary>
        /// Both stock reports, because they are one endpoint and one table with one filter — the same
        /// shape the web till uses (`StockView negativeOnly`). ⚠ Two copies of "what a stock row is"
        /// would be two things to keep agreeing.
        ///
        /// ⚠⚠ **NO DATE RANGE, AND THAT IS NOT AN OVERSIGHT.** On-hand stock is a fact about NOW, not
        /// about a period: it is a materialised sum of the whole movement ledger. So this ignores
        /// `q.From`/`q.To` deliberately — and the note says so on screen, because a report that
        /// silently ignores the dates above it invites somebody to believe they asked for last week's
        /// stock and got it.
        /// </summary>
        private static async Task<ReportTable> StockPage(
            PlutusApiClient api, bool negativeOnly, CancellationToken ct)
        {
            var data = await api.GetStockLevelsPageAsync(
                filter: negativeOnly ? "negative" : null, ct: ct).ConfigureAwait(false);

            if (data is null) return ReportTable.Empty(Unreadable);

            // ⚠ AN EMPTY NEGATIVE-STOCK REPORT IS GOOD NEWS AND MUST READ AS SUCH. "No rows" on this
            // report looks identical to a failed read otherwise, and the two mean opposite things.
            if (data.Rows.Count == 0)
                return ReportTable.Empty(negativeOnly
                    ? "Nothing is below zero — every counted item is at or above zero."
                    : "No stock records yet. Stock arrives when goods-in is recorded against an item.");

            // ⚠⚠ THE CAP MUST BE SAID, same rule as `items-sold`. The server clamps to 200 and
            // answers `matched` with the truth; a stock report that looks complete and is not sends
            // somebody to a shelf for something that was never there.
            var note = data.Matched > data.Rows.Count
                ? $"⚠ Showing {data.Rows.Count:N0} of {data.Matched:N0} rows — the server sends 200 at a time. Search to narrow it."
                : string.Empty;

            // ⚠ Said on EVERY stock report, capped or not: the dates above are inert here.
            const string noDates = "On-hand stock is as of now — the date range above does not apply.";
            note = string.IsNullOrEmpty(note) ? noDates : note + " · " + noDates;

            return ReportTable.From(
                data.Rows,
                new[] { "Item", "Category", "Location", "Qty" },
                new[] { false, false, false, true },
                r => new[]
                {
                    // ⚠ THE ID WHEN THERE IS NO NAME, never a blank. A stock row can outlive the
                    // catalogue item it points at, and a blank row is one nobody can investigate.
                    new ReportCell(string.IsNullOrWhiteSpace(r.Name) ? (r.ItemIdOne ?? "(unknown item)") : r.Name),
                    new ReportCell(r.Category ?? "(none)"),
                    new ReportCell(r.Location ?? "?"),
                    // ⚠ SORTS AS A NUMBER, which is the whole point on this report: ordered as text,
                    // "−28508" and "−3" sort by their first character and the worst offenders hide in
                    // the middle of the list.
                    // ⚠ GROUPED (`N0`), unlike the small qty columns on the other reports: stock counts
                    // reach five digits here, and the totals line right beside this already groups.
                    // A test caught the two disagreeing.
                    new ReportCell(r.Quantity.ToString("N0", CultureInfo.CurrentCulture), true, r.Quantity),
                },
                note,
                negativeOnly
                    ? $"{data.Matched:N0} below zero"
                    : $"{data.InStock:N0} in stock · {data.Matched:N0} with a stock record · {data.TotalCatalogueItems:N0} products in the catalogue");
        }
        /// <summary>
        /// ⚠ WHAT A REFUSAL SAYS. These endpoints are gated on `portal.reports.view` /
        /// `portal.financials.view`, so a cashier without the grant gets a 403 — and a report that
        /// rendered £0.00 would tell them the shop sold nothing today. Null is not zero.
        /// </summary>
        private const string Unreadable =
            "This report couldn't be read. You may not have permission to see it, or the till is offline.";
    }
}
