using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// WP2c-exempt: WHICH VAT BAND this line was rung up under — the band's key, from
    /// <c>GET /api/v1/vat/bands</c>.
    ///
    /// ⚠ NOT REDUNDANT WITH <see cref="IngestLine.VatRateBp"/>. Zero-rated and exempt supplies both
    /// declare 0bp and are different in law: exempt blocks recovery of attributable input tax, zero
    /// rated doesn't (HMRC Notice 706). Without this, a shop selling both can never derive its
    /// partial-exemption position from its own takings — the information is gone the moment the sale
    /// is written.
    ///
    /// Resolve it from the item's legacy <c>TaxId</c> against the published bands' <c>legacyTaxIds</c>.
    /// Leave it NULL when the portal hasn't decided which band a tax row means: the server then falls
    /// back to snapping the rate, and the portal shows the tax row as needing a decision. Do not
    /// guess — a guess here puts a number on a VAT return that nobody chose.
    /// </summary>
    [JsonPropertyName("vatBand")] public string? VatBand { get; set; }
    /// <summary>Real catalogue discounts only. The members' auto-discount uses sentinel id 0 and
    /// is deliberately OMITTED — the legacy bridge maps these by real DiscountId and a synthetic
    /// one would FK-fail. Its money still flows through <see cref="IngestLine.DiscountPence"/>.</summary>
    [JsonPropertyName("discounts")] public List<LineDiscount>? Discounts { get; set; }
    [JsonPropertyName("return")] public ReturnRef? Return { get; set; }

    /// <summary>
    /// WHICH BARCODE WAS ACTUALLY SCANNED, when an item has more than one and it was not the item's
    /// own (multi-barcode, 2026-08-20).
    ///
    /// ⚠⚠ A SNAPSHOT, NEVER AN IDENTITY. <see cref="ItemIdOne"/> stays canonical and is what every
    /// reader keys on — stock, VAT band, item reports, the legacy bridge. This field is here for the
    /// day a supplier's barcode migration goes wrong and somebody has to ask *which code did the
    /// tills actually read?* — the question `Build/archive/plutus-catalogue-sync-design.md` §7 calls
    /// "gold for debugging" and that nothing could otherwise answer.
    ///
    /// ⚠ OMITTED when the scanned code WAS the item's own, which is almost every line — so an
    /// ordinary sale's metadata stays byte-identical to what it was before this field existed. The
    /// same discipline as <see cref="DiscountAuthority"/> below.
    ///
    /// ⚠ The server needs no change to keep it: `SalesIngestService` stores `DiscountsJson` verbatim
    /// and only ever plucks named fields out of it, so an unknown field survives the round trip.
    /// </summary>
    [JsonPropertyName("barcodeScanned")] public string? BarcodeScanned { get; set; }

    /// <summary>
    /// WHO authorised each discount on this line, and WHY — binding default 22(c).
    ///
    /// ⚠⚠ IT IS NOT ON <see cref="LineDiscount"/>, AND THAT IS THE WHOLE DESIGN DECISION.
    /// <c>discounts[]</c> is projected straight into legacy `Transaction_Discount` rows keyed on a
    /// real `DiscountId` (`LegacySaleBridgeConsumer.cs:195`) — which is why the members'
    /// auto-discount is already deliberately omitted from it. A manual discount has no catalogue id
    /// either, so putting the audit fields there would have meant emitting a synthetic-id entry, and
    /// the bridge would have FK-failed the projection of every discounted sale. This list is its own
    /// field precisely so the bridge never sees it.
    ///
    /// ⚠ ONE ENTRY PER DISCOUNT, NOT PER LINE. A line can attract two (a member's tier rate and a
    /// manual one) and <see cref="IngestLine.DiscountPence"/> is their SUM — so the sum alone cannot
    /// say which half a supervisor approved.
    ///
    /// ⚠ A BASKET-WIDE DISCOUNT REPEATS ON EVERY LINE IT LANDS ON, with each line's own share in
    /// <c>amountPence</c>. That is duplication and it is the right kind: the money is apportioned per
    /// line, so the attribution has to be too, or a line-level report of "discount given, and why"
    /// has to re-derive the apportionment to answer.
    ///
    /// ⚠ NULL ON EVERY SALE WRITTEN BEFORE 2026-08-14, and null-safe forever. Absent means "this
    /// till did not record it", NOT "nobody authorised it" — do not render the two the same way.
    ///
    /// ⚠ The server needs NO change to keep this: `SalesIngestService` stores `DiscountsJson`
    /// VERBATIM into a `longtext` column and only ever plucks named fields back out with
    /// `JsonDocument`, so an unknown field survives the round trip and comes back on
    /// `SaleLineDto.DiscountsJson`.
    /// </summary>
    [JsonPropertyName("discountAuthority")] public List<DiscountAuthorityRef>? DiscountAuthority { get; set; }

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

/// <summary>
/// One line of a sale as the platform holds it — `GET /api/v1/sales/{saleId}` (cutover step 15).
/// ⚠ A projection of the SERVER's record, not of what this till sent: for a sale rung on another
/// till, or migrated from the legacy system, they are not the same thing.
/// </summary>
public sealed class SaleLineDto
{
    [JsonPropertyName("lineNo")] public int LineNo { get; set; }
    [JsonPropertyName("itemIdOne")] public string? ItemIdOne { get; set; }
    [JsonPropertyName("itemName")] public string? ItemName { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("unitPricePence")] public long UnitPricePence { get; set; }
    [JsonPropertyName("discountPence")] public long DiscountPence { get; set; }
    [JsonPropertyName("lineGrossPence")] public long LineGrossPence { get; set; }
    [JsonPropertyName("vatRateBp")] public int VatRateBp { get; set; }
    [JsonPropertyName("vatAmountPence")] public long VatAmountPence { get; set; }

    /// <summary>⚠ The line's <see cref="LineMeta"/>, which is where the EX-VAT unit price lives.
    /// There is no ex-unit field on the wire, and deriving one from <see cref="VatRateBp"/> would
    /// re-run VAT arithmetic the sale already settled — disagreeing by a penny on some lines, on a
    /// refund, against a receipt the customer is holding.</summary>
    [JsonPropertyName("discountsJson")] public string? DiscountsJson { get; set; }

    /// <summary>What one unit cost before VAT, from the line meta; falls back to the inc price,
    /// which is right for a zero-rated line and the least-wrong answer for any other.</summary>
    public long UnitExPence => LineMeta.FromJson(DiscountsJson) is { ExUnitPence: > 0 } meta
        ? meta.ExUnitPence
        : UnitPricePence;
}

/// <summary>
/// A refund or void already recorded against a sale.
///
/// ⚠ THIS IS THE SERVER'S HALF OF THE REFUND CAP. `AmountPence` here plus what the local store
/// knows is how much of a sale has been given back; a refund decided without it is a refund
/// decided from one till's memory, and the customer only has to walk to a different counter.
/// </summary>
public sealed class SaleAdjustmentDto
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("itemId")] public Guid? ItemId { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("amountPence")] public long AmountPence { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

/// <summary>
/// A sale as the platform holds it — the authority a receipt-led refund is decided against when
/// this till never sold the goods (`GET /api/v1/sales/{saleId}`).
/// </summary>
public sealed class SaleDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("businessDay")] public string? BusinessDay { get; set; }
    [JsonPropertyName("occurredAtUtc")] public DateTime OccurredAtUtc { get; set; }
    [JsonPropertyName("grossPence")] public long GrossPence { get; set; }
    [JsonPropertyName("vatPence")] public long VatPence { get; set; }
    [JsonPropertyName("operatorName")] public string? OperatorName { get; set; }
    [JsonPropertyName("lines")] public List<SaleLineDto> Lines { get; set; } = new();
    [JsonPropertyName("adjustments")] public List<SaleAdjustmentDto> Adjustments { get; set; } = new();

    /// <summary>
    /// What has already been refunded against this sale, in pence, as a positive number.
    ///
    /// ⚠ Voids are counted too. A voided line's money left the drawer just as surely as a refunded
    /// one's, and treating a void as "not a refund" leaves exactly that much refundable twice.
    /// </summary>
    public long AlreadyRefundedPence =>
        Adjustments?.Sum(a => Math.Abs(a.AmountPence)) ?? 0;

    /// <summary>
    /// How the sale was PAID — finding Y, 2026-08-13.
    ///
    /// ⚠⚠ THE SERVER HAS ALWAYS SENT THESE AND THIS CONTRACT IGNORED THEM. `GET /api/v1/sales/{saleId}`
    /// has projected `tenders` since the endpoint was written; nothing here read them, so a till
    /// refunding a sale rung up on ANOTHER till could not tell a £2.00-cash-plus-£2.40-card payment
    /// from £4.40 on a card — and put the whole refund wherever the operator tapped.
    /// </summary>
    /// ⚠ A SHAPE, NOT A RULE. Turning these names into wire bytes needs `SharedKernel`, and this
    /// project deliberately references NOTHING — see `SaleDtoTenders` in `Plutus.Client.Core`.
    [JsonPropertyName("tenders")] public List<SaleTenderDto> Tenders { get; set; } = new();
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

/// <summary>
/// One discount's audit record as it travels — see <see cref="LineMeta.DiscountAuthority"/>.
///
/// ⚠ A SHAPE, NOT A RULE. What makes an authority valid — the reason being mandatory, the
/// normalisation, the refusal of self-approval — lives in `SharedKernel.DiscountAudit`, which this
/// project deliberately cannot reference. Same split as `SaleTenderDto` ↔ `SaleDtoTenders`. Build
/// these from a `DiscountAuthority`; never hand-roll one, or the rule is enforced on one till only.
/// </summary>
public sealed class DiscountAuthorityRef
{
    /// <summary>Why, in the operator's words — or the tier's name for an automatic one.</summary>
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";

    /// <summary>THIS LINE's share of the discount, positive. ⚠ Not the whole basket-wide amount:
    /// the shares across the lines sum to what was taken off, so a report can add them up.</summary>
    [JsonPropertyName("amountPence")] public long AmountPence { get; set; }

    /// <summary>The signed-in operator who applied it.</summary>
    [JsonPropertyName("requestedBy")] public string? RequestedBy { get; set; }

    /// <summary>The supervisor who authorised it, or ABSENT when the discount was within the
    /// operator's own ceiling. ⚠ Absent is a real answer — "no step-up was required".</summary>
    [JsonPropertyName("authorisedBy")] public string? AuthorisedBy { get; set; }

    /// <summary>The authoriser's name as the roster had it at the time — stored, never resolved
    /// later, because staff leave and "(deleted user)" answers nothing.</summary>
    [JsonPropertyName("authorisedByName")] public string? AuthorisedByName { get; set; }
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

/// <summary>One tender on a sale the platform holds, as `GET /api/v1/sales/{saleId}` serialises it.</summary>
public sealed class SaleTenderDto
{
    /// <summary>⚠ The `TenderType` ENUM NAME — "Cash", "Card", "GiftCard" — not the wire byte. Parse
    /// it with <see cref="Plutus.SharedKernel.Tenders.TryFromWireName"/>, which refuses what it does
    /// not recognise instead of defaulting to Card.</summary>
    [JsonPropertyName("tenderType")] public string? TenderType { get; set; }

    [JsonPropertyName("amountPence")] public long AmountPence { get; set; }
    [JsonPropertyName("changePence")] public long ChangePence { get; set; }
}
