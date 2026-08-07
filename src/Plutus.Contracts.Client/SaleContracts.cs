using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plutus.Contracts.Client;

// ─────────────────────────────────────────────────────────────────────────────
// WP1: the /api/v1/sales wire contract, mirroring src/Plutus.Sales/SalesIngestService.cs
// FIELD FOR FIELD. These types exist so the till and the server cannot drift: if the server's
// DTO changes, this file must change with it and every caller stops compiling.
//
// tenantId and deviceId are derived FROM THE TOKEN by SalesV2Controller — DeviceId here is
// optional and, if present, must match the token's `did` or the request is 403 (not a merge).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One sale line. ⚠ There is NO first-class ItemIdOne field on the wire — the barcode
/// travels inside <see cref="DiscountsJson"/>; see <see cref="LineMeta"/>.</summary>
public sealed class IngestLine
{
    public Guid ItemId { get; set; }
    public int Qty { get; set; }
    public long UnitPricePence { get; set; }
    public long DiscountPence { get; set; }
    public long LineGrossPence { get; set; }
    public int VatRateBp { get; set; }
    public long VatAmountPence { get; set; }
    public long? OverriddenFromPence { get; set; }
    /// <summary>The <see cref="LineMeta"/> envelope, serialised. Build it with
    /// <see cref="LineMeta.ToJson"/> — never hand-roll it.</summary>
    public string? DiscountsJson { get; set; }
}

public sealed class IngestTender
{
    public byte TenderType { get; set; }
    public long AmountPence { get; set; }
    public long ChangePence { get; set; }
    public string? ProviderRef { get; set; }
}

public sealed class IngestSaleRequest
{
    public Guid SaleId { get; set; }
    /// <summary>Optional. If set it MUST equal the token's device id — a mismatch is 403.</summary>
    public Guid DeviceId { get; set; }
    public long DeviceSeq { get; set; }
    public byte Channel { get; set; }
    public DateOnly BusinessDay { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long GrossPence { get; set; }
    public long VatPence { get; set; }
    public string? Note { get; set; }
    public Guid? OperatorUserId { get; set; }
    public List<IngestLine> Lines { get; set; } = new();
    public List<IngestTender> Tenders { get; set; } = new();
}

/// <summary>
/// The projection metadata the server reads out of <see cref="IngestLine.DiscountsJson"/>.
///
/// ⚠ <b>THIS IS LOAD-BEARING AND FAILS SILENTLY.</b> `StockProjectionConsumer` only attributes a
/// stock movement when it can read <c>itemIdOne</c> from here; a line without it is skipped with
/// no error at all, so stock quietly stops moving while every sale still reports success. The
/// web till builds the identical shape in <c>api.ts</c> — keep the two in step.
/// </summary>
public sealed class LineMeta
{
    [JsonPropertyName("itemIdOne")] public string ItemIdOne { get; set; } = "";
    [JsonPropertyName("exUnitPence")] public long ExUnitPence { get; set; }
    /// <summary>Real catalogue discounts only. The members' auto-discount uses sentinel id 0 and
    /// is deliberately OMITTED — the legacy bridge maps these by real DiscountId and a synthetic
    /// one would FK-fail. Its money still flows through <see cref="IngestLine.DiscountPence"/>.</summary>
    [JsonPropertyName("discounts")] public List<LineDiscount>? Discounts { get; set; }
    [JsonPropertyName("return")] public ReturnRef? Return { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static LineMeta? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<LineMeta>(json, Options); }
        catch (JsonException) { return null; }
    }
}

public sealed class LineDiscount
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("rate")] public decimal Rate { get; set; }
}

public sealed class ReturnRef
{
    [JsonPropertyName("originSaleId")] public string OriginSaleId { get; set; } = "";
}

/// <summary>What the ingest endpoint answers with. 201 recorded · 200 duplicate (same outcome
/// re-read) · 202 quarantined (do NOT retry) · 400 rejected (skip and continue — a poison sale
/// must never block the queue).</summary>
public sealed class IngestResponse
{
    public string? Status { get; set; }
    public Guid SaleId { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public string? Detail { get; set; }
}
