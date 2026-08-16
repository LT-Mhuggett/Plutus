using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Plutus.Contracts.Client;

/// <summary>
/// What a till has taken over a period — the platform's figures, not the till's.
///
/// ⚠ THE POINT OF READING THIS FROM THE SERVER is that a till cannot know its own takings. Sales
/// posted by the OTHER device on the same till, sales this till has forgotten after pruning, and
/// refunds taken at another counter against sales rung up here all belong in the number an operator
/// counts a drawer against. A till that summed its own local sales would be confidently wrong, and
/// the direction it is wrong in changes with every outage.
///
/// ⚠ Rollups are written PER TILL PER BUSINESS DAY (`RollupProjection`), so `level=till` is exactly
/// "this till, this day" — a real X-report rather than an approximation of one.
///
/// ⚠ TWIN of the anonymous object `ReportsController.Summary` returns. The contracts project has no
/// references because it ships onto tills, so the server cannot hand this type back; recorded in
/// till-design C2. ⚠ The server sends PENCE here — `summary-rich` is the one that sends pounds, and
/// mixing them up is a 100× error in a figure somebody banks against.
/// </summary>
public sealed class ReportSummary
{
    [JsonPropertyName("level")] public string? Level { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("to")] public string? To { get; set; }
    [JsonPropertyName("granularity")] public string? Granularity { get; set; }
    [JsonPropertyName("totals")] public ReportTotals Totals { get; set; } = new();

    /// <summary>One entry per period in the range, ZERO-FILLED by the server — a day with no sales
    /// is a row of zeroes rather than a gap, so a chart cannot silently skip it.</summary>
    [JsonPropertyName("buckets")] public List<ReportBucket> Buckets { get; set; } = new();
}

public sealed class ReportTotals
{
    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }
    [JsonPropertyName("vatPence")] public long VatPence { get; set; }
    [JsonPropertyName("txnCount")] public int TxnCount { get; set; }

    /// <summary>⚠ The SERVER's average, integer-divided. Recomputing it here from gross and count
    /// would disagree by a penny on most days, and the two figures would appear side by side.</summary>
    [JsonPropertyName("avgBasketPence")] public long AvgBasketPence { get; set; }
}

/// <summary>
/// One sale in the platform's list — enough to RECOGNISE it, from ANY till.
///
/// ⚠ THIS IS WHAT MAKES A CROSS-TILL REFUND FINDABLE. `TillStore.ListRecentSalesAsync` covers this
/// till's own sales and works offline, which is the common case; goods bought at another branch are
/// only in the platform, and until now the operator had to type a UUID off a receipt to reach one.
///
/// ⚠ NO LINES HERE, deliberately — same reasoning as `LocalSaleSummary`. It cannot say what is still
/// returnable, so the caller reads the real sale (`GET /api/v1/sales/{saleId}`) before capping
/// anything. A cap computed from a list entry is not a cap.
///
/// ⚠ TWIN of the anonymous object `ReportsController.SalesList` returns (till-design C2).
/// </summary>
public sealed class SaleListEntry
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("businessDay")] public string? BusinessDay { get; set; }
    [JsonPropertyName("occurredAtUtc")] public DateTime OccurredAtUtc { get; set; }

    /// <summary>⚠ Which till took it — the whole reason an operator is looking at this list.</summary>
    [JsonPropertyName("tillId")] public Guid TillId { get; set; }

    /// <summary>`Till`, `Webstore`, … — a webstore order is not refundable at a counter the same way.</summary>
    [JsonPropertyName("channel")] public string? Channel { get; set; }

    /// <summary>⚠ NEGATIVE for a refund. A refund is itself a sale, and one must never be offered as
    /// something to refund against — that cost £13.99 twice on 2026-08-10.</summary>
    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }

    [JsonPropertyName("vatPence")] public long VatPence { get; set; }
    [JsonPropertyName("legacyRef")] public string? LegacyRef { get; set; }
}

public sealed class ReportBucket
{
    [JsonPropertyName("period")] public string? Period { get; set; }
    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }
    [JsonPropertyName("vatPence")] public long VatPence { get; set; }
    [JsonPropertyName("txnCount")] public int TxnCount { get; set; }
    [JsonPropertyName("avgBasketPence")] public long AvgBasketPence { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Step 26 additions — the reports MAUI's Statistics tab is being repointed at.
//
// ⚠⚠⚠ READ THIS BEFORE ADDING ANOTHER. `/reports/summary-rich` answers in **POUNDS**
// (`ReportsController.cs:241` divides by 100 first); **every other report answers in PENCE**. The
// JSON gives no clue — `total` and `grossPence` look equally plausible on a takings figure — so the
// unit lives in the NAME. `ReportSummary` above already carries `…Pence` on every property; the
// pounds type carries it on the TYPE as well, because every one of its members is pounds and one
// missed suffix is a 100× error on a figure somebody banks against. Binding default 17.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// `/api/v1/reports/summary-rich` — the till's Summary screen: takings, orders, top items, tenders
/// and VAT bands.
///
/// ⚠⚠ **POUNDS, UNIQUELY.** Every other report on this till is pence. The type name says so because
/// this is the one place the two meet on a single screen.
/// </summary>
public sealed class SalesSummaryPounds
{
    [JsonPropertyName("totalSales")] public decimal TotalSalesPounds { get; set; }
    [JsonPropertyName("totalSalesExTax")] public decimal TotalSalesExTaxPounds { get; set; }
    [JsonPropertyName("totalOrders")] public int TotalOrders { get; set; }

    [JsonPropertyName("byDay")] public List<SummaryDayPounds> ByDay { get; set; } = new();
    [JsonPropertyName("topItems")] public List<SummaryTopItemPounds> TopItems { get; set; } = new();
    [JsonPropertyName("byPayMethod")] public List<SummaryPayMethodPounds> ByPayMethod { get; set; } = new();
    [JsonPropertyName("byTaxRate")] public List<SummaryTaxRatePounds> ByTaxRate { get; set; } = new();
}

public sealed class SummaryDayPounds
{
    [JsonPropertyName("date")] public string? Date { get; set; }
    [JsonPropertyName("total")] public decimal TotalPounds { get; set; }
    [JsonPropertyName("totalExTax")] public decimal TotalExTaxPounds { get; set; }
    [JsonPropertyName("orders")] public int Orders { get; set; }
}

public sealed class SummaryTopItemPounds
{
    [JsonPropertyName("itemId")] public string? ItemId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
    [JsonPropertyName("gross")] public decimal GrossPounds { get; set; }
    [JsonPropertyName("grossExTax")] public decimal GrossExTaxPounds { get; set; }
}

public sealed class SummaryPayMethodPounds
{
    [JsonPropertyName("method")] public string? Method { get; set; }
    [JsonPropertyName("total")] public decimal TotalPounds { get; set; }
}

public sealed class SummaryTaxRatePounds
{
    [JsonPropertyName("tax")] public string? Tax { get; set; }
    [JsonPropertyName("gross")] public decimal GrossPounds { get; set; }
    [JsonPropertyName("net")] public decimal NetPounds { get; set; }
    [JsonPropertyName("vat")] public decimal VatPounds { get; set; }
}

/// <summary>
/// `/api/v1/reports/vat` — the VAT table. **PENCE.**
///
/// ⚠ Its `vatPence` is VAT on the takings in the period. It is NOT interchangeable with any other
/// report's VAT figure, and the two must never be added or compared — default 17 names this trap.
/// </summary>
public sealed class ReportVat
{
    [JsonPropertyName("totals")] public ReportVatTotals Totals { get; set; } = new();
    [JsonPropertyName("buckets")] public List<ReportVatBucket> Buckets { get; set; } = new();
}

public sealed class ReportVatTotals
{
    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }
    [JsonPropertyName("netPence")] public long NetPence { get; set; }
    [JsonPropertyName("vatPence")] public long VatPence { get; set; }
}

public sealed class ReportVatBucket
{
    [JsonPropertyName("period")] public string? Period { get; set; }

    /// <summary>⚠ BASIS POINTS, not a percentage — 2000 is 20%. A report that printed "2000%" would
    /// at least be obvious; one that divided by the wrong power of ten would not.</summary>
    [JsonPropertyName("vatRateBp")] public int VatRateBp { get; set; }

    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }
    [JsonPropertyName("netPence")] public long NetPence { get; set; }
    [JsonPropertyName("vatPence")] public long VatPence { get; set; }
}

/// <summary>`/api/v1/reports/items-sold` — every line sold in a range. **PENCE.**</summary>
public sealed class ItemsSold
{
    /// <summary>⚠ The server's count of MATCHING rows, which can exceed `Rows.Count` when the take
    /// cap bites. Compare the two before believing a total — see `PlutusApiClient.ItemsSoldRowCap`.</summary>
    [JsonPropertyName("count")] public int Count { get; set; }

    [JsonPropertyName("totals")] public ItemsSoldTotals Totals { get; set; } = new();
    [JsonPropertyName("rows")] public List<ItemSoldRow> Rows { get; set; } = new();
}

public sealed class ItemsSoldTotals
{
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }
    [JsonPropertyName("discountPence")] public long DiscountPence { get; set; }
}

public sealed class ItemSoldRow
{
    [JsonPropertyName("dateSold")] public string? DateSold { get; set; }
    [JsonPropertyName("itemIdOne")] public string? ItemIdOne { get; set; }
    [JsonPropertyName("itemName")] public string? ItemName { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }

    /// <summary>⚠ WHICH TILL — the cross-till half. A report that cannot say where a line was rung
    /// up is one store's figures pretending to be one till's.</summary>
    [JsonPropertyName("tillName")] public string? TillName { get; set; }

    [JsonPropertyName("staffName")] public string? StaffName { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("unitPricePence")] public long UnitPricePence { get; set; }
    [JsonPropertyName("discountPence")] public long DiscountPence { get; set; }
    [JsonPropertyName("lineGrossPence")] public long LineGrossPence { get; set; }
}

/// <summary>`/api/v1/reports/category-sales` — a report MAUI has never had. **PENCE.**</summary>
public sealed class CategorySales
{
    [JsonPropertyName("totals")] public CategorySalesTotals Totals { get; set; } = new();
    [JsonPropertyName("rows")] public List<CategorySalesRow> Rows { get; set; } = new();
}

public sealed class CategorySalesTotals
{
    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("categories")] public int Categories { get; set; }
}

public sealed class CategorySalesRow
{
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }
    [JsonPropertyName("discountPence")] public long DiscountPence { get; set; }

    /// <summary>⚠ A PERCENTAGE the server already worked out — not a fraction, and not pence. The
    /// one number on these reports that is neither money nor a count.</summary>
    [JsonPropertyName("sharePct")] public decimal SharePct { get; set; }
}

/// <summary>`/api/v1/reports/best-sellers` — also new to MAUI. **PENCE.**</summary>
public sealed class BestSellers
{
    [JsonPropertyName("rows")] public List<BestSellerRow> Rows { get; set; } = new();
}

public sealed class BestSellerRow
{
    [JsonPropertyName("rank")] public int Rank { get; set; }
    [JsonPropertyName("itemIdOne")] public string? ItemIdOne { get; set; }
    [JsonPropertyName("itemName")] public string? ItemName { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }
    [JsonPropertyName("sharePct")] public decimal SharePct { get; set; }
}
