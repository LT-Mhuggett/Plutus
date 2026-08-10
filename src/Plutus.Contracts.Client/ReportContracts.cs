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
