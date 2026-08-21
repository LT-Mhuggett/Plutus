using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;

namespace Plutus.Client.Core;

/// <summary>
/// One line in a basket, in the shape the wire wants.
///
/// ⚠ MONEY IS <see langword="long"/> PENCE, ids are GUIDs, and there is no reference to any
/// UI or database entity. The legacy MAUI basket held a `Database.Models.ItemModel` and decimals,
/// which is why it could only ever produce a legacy sale — the architecture test
/// `No_module_declares_decimal_or_double_money_members` exists for exactly this.
///
/// ⚠ A RETURN IS A FLAG, NOT A SUBCLASS. The legacy till had a separate `BasketReturnItem` and
/// nine `is BasketReturnItem` type-tests scattered through its viewmodel;
/// <see cref="VatLineMath.ForLine"/> already handles every sign from <c>isReturn</c> — and
/// deliberately REFUSES a negative quantity, because passing one alongside the flag would
/// double-negate and turn a refund into a payment.
/// </summary>
/// <param name="ItemId">The platform item id. ⚠ It must equal
/// <c>DeterministicGuid.ForItem(businessId, IdOne)</c> — `TillStore`'s catalogue rows already are
/// that, and <see cref="SaleAssembler"/> re-derives and checks it rather than trusting the caller,
/// because a wrong id here attaches a sale to the wrong item silently.</param>
/// <param name="IdOne">The legacy code / default barcode. ⚠ LOAD-BEARING — see
/// <see cref="LineMeta.ItemIdOne"/>: without it the server's stock projection skips the line and
/// stock stops moving while the sale still reports success.</param>
/// <param name="UnitIncPence">Unit price INCLUDING VAT — what the customer sees.</param>
/// <param name="UnitExPence">The SAME price point's ex-VAT half. ⚠ Not derived from a rate here:
/// the line's declared rate is derived from this pair (C1 rule 2).</param>
/// <param name="DiscountPence">Positive, inc-VAT, for the WHOLE line. Compute it with
/// <see cref="LineDiscounts"/> — never by multiplying by an operator's raw input.</param>
/// <param name="VatBandKey">Which published band this was rung up under. ⚠ NULL is a legitimate
/// answer meaning "the portal has not decided which band this tax row is" — never guess one.</param>
/// <param name="OverriddenFromPence">The price before an operator overrode it, if they did.</param>
/// <param name="OriginSaleId">For a return: the sale being refunded.</param>
/// <param name="DiscountAuthorities">Who authorised each discount on this line, and why — binding
/// default 22(c). ⚠ ONE PER DISCOUNT, not one per line: <paramref name="DiscountPence"/> is their
/// sum, so it cannot on its own say which half a supervisor approved. ⚠ Each entry's `AmountPence`
/// is THIS LINE's share, which is why a basket-wide discount appears on every line it landed on.
/// ⚠ Empty is legitimate on a line with no discount, and on any line from a till built before
/// 2026-08-14 — absent means "not recorded", never "nobody authorised it".</param>
public sealed record BasketLine(
    Guid ItemId,
    string IdOne,
    string Name,
    long UnitIncPence,
    long UnitExPence,
    int Quantity,
    long DiscountPence = 0,
    string? VatBandKey = null,
    long? OverriddenFromPence = null,
    bool IsReturn = false,
    Guid? OriginSaleId = null,
    IReadOnlyList<DiscountAuthority>? DiscountAuthorities = null,
    /// <summary>The barcode the operator actually scanned, when an item has more than one
    /// (multi-barcode, 2026-08-20). ⚠ A SNAPSHOT — <see cref="IdOne"/> stays canonical and is what
    /// everything downstream keys on. Null, or equal to <see cref="IdOne"/>, on almost every line;
    /// the assembler omits it in both cases.</summary>
    string? ScannedBarcode = null);

/// <summary>What a basket is worth, as the header must state it.</summary>
public sealed record BasketTotals(long GrossPence, long ExPence, long VatPence);

/// <summary>
/// Turns a basket into the exact payload <c>POST /api/v1/sales</c> expects.
///
/// ⚠ THIS IS THE KEYSTONE OF THE MONEY PATH. Nothing in the repo built an
/// <see cref="IngestSaleRequest"/> before it: the MAUI till wrote a legacy EF object graph and
/// called <c>db.Save()</c>, so however complete its screens looked it could not sell into Plutus
/// at all. Every penny of every sale and every VAT return flows through this class.
///
/// ⚠ IT DOES NOT INVENT ARITHMETIC. Each line goes through <see cref="VatLineMath.ForLine"/> —
/// the same shared rule the server and the web till use — and the header totals are the SUM of
/// what those lines produced. Recomputing a header from prices and rates is the classic way to
/// disagree with your own lines by a penny, and the server's reconcile invariants reject the sale
/// when it happens.
///
/// The reference implementation is the web till's `api.ts` (~line 1062). Where this and that
/// disagree, that one is right (cutover binding default 10).
/// </summary>
public static class SaleAssembler
{
    /// <summary>
    /// Assemble the payload. ⚠ Throws rather than posting something the server will silently
    /// mis-handle — see the individual guards.
    /// </summary>
    /// <param name="businessId">The LEGACY business id, for re-deriving item ids. ⚠ NOT the tenant
    /// id: <c>DeterministicGuid.ForItem</c> seeds from this, and the wrong one makes every id on
    /// this till differ from the web till's for the same barcode.</param>
    public static IngestSaleRequest Assemble(
        Guid saleId,
        Guid deviceId,
        long deviceSeq,
        Guid businessId,
        IReadOnlyList<BasketLine> lines,
        IReadOnlyList<IngestTender> tenders,
        DateOnly businessDay,
        DateTime occurredAtUtc,
        Guid? operatorUserId = null,
        string? note = null,
        byte channel = SaleChannels.Till)
    {
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("A sale must have at least one line.", nameof(lines));
        if (tenders is null || tenders.Count == 0)
            throw new ArgumentException("A sale must have at least one tender.", nameof(tenders));

        var ingestLines = new List<IngestLine>(lines.Count);
        long gross = 0, ex = 0;

        foreach (var line in lines)
        {
            // ⚠ THE SILENT ONE. LineMeta.itemIdOne is what StockProjectionConsumer attributes a
            // stock movement from; a line without it is SKIPPED with no error, so stock quietly
            // stops moving while every sale reports success. Refusing here is the only point at
            // which that is still visible.
            if (string.IsNullOrWhiteSpace(line.IdOne))
                throw new InvalidOperationException(
                    $"Basket line '{line.Name}' has no IdOne. The server would accept the sale and " +
                    "silently skip its stock movement — refusing instead.");

            // ⚠ Re-derived, not trusted. A caller that built the id any other way would attach this
            // sale to a different item than the web till would for the same barcode, permanently
            // and with no symptom until someone compared two reports.
            var expectedId = DeterministicGuid.ForItem(businessId, line.IdOne);
            if (line.ItemId != Guid.Empty && line.ItemId != expectedId)
                throw new InvalidOperationException(
                    $"Basket line '{line.Name}' ({line.IdOne}) carries item id {line.ItemId}, but " +
                    $"DeterministicGuid.ForItem derives {expectedId}. One of them is wrong, and " +
                    "guessing which would corrupt this item's sales history silently.");

            var vat = VatLineMath.ForLine(
                line.UnitIncPence, line.UnitExPence, line.Quantity, line.DiscountPence, line.IsReturn);

            var meta = new LineMeta
            {
                ItemIdOne = line.IdOne,
                ExUnitPence = line.UnitExPence,
                // ⚠ Omitted, never guessed, when the portal has not resolved the tax row. The
                // server then falls back to snapping the rate and shows the row as needing a
                // decision — a guess here puts a number on a VAT return nobody chose.
                VatBand = string.IsNullOrWhiteSpace(line.VatBandKey) ? null : line.VatBandKey,
                Return = line.IsReturn && line.OriginSaleId is Guid origin
                    ? new ReturnRef { OriginSaleId = origin.ToString("D") }
                    : null,

                // ⚠ Binding default 22(c) — "all discounts need to be tracked". Emitted here rather
                // than by the caller so that EVERY payload this assembler builds carries the
                // attribution, on every till, without each checkout screen remembering to.
                //
                // ⚠ NULL, NOT AN EMPTY LIST, when there is nothing to say: `LineMeta` serialises
                // with `WhenWritingNull`, so an undiscounted line's JSON stays byte-identical to what
                // it was before this field existed. A sale is compared against its own stored meta in
                // more than one place, and a new empty array on every line would be a diff on every
                // line.
                DiscountAuthority = DiscountAuthorityWire.ToWire(line.DiscountAuthorities),

                // ⚠ Multi-barcode: recorded ONLY when the operator scanned something other than the
                // item's own code, so an ordinary line's metadata is byte-identical to what it was
                // before this field existed. ⚠ It is a snapshot for debugging a supplier's barcode
                // migration, never an identity — `ItemIdOne` above is the canonical one and the only
                // id any reader keys on.
                BarcodeScanned =
                    !string.IsNullOrWhiteSpace(line.ScannedBarcode) &&
                    !string.Equals(line.ScannedBarcode, line.IdOne, StringComparison.Ordinal)
                        ? line.ScannedBarcode
                        : null,
            };

            ingestLines.Add(new IngestLine
            {
                ItemId = expectedId,
                Qty = vat.Qty,
                UnitPricePence = line.UnitIncPence,
                DiscountPence = vat.DiscountPence,
                LineGrossPence = vat.LineGrossPence,
                VatRateBp = vat.VatRateBp,
                VatAmountPence = vat.VatAmountPence,
                OverriddenFromPence = line.OverriddenFromPence,
                DiscountsJson = meta.ToJson(),
            });

            // ⚠ SUMMED FROM THE LINES, never recomputed from prices and rates. The server's
            // reconcile invariants compare the header against the lines and reject a sale whose
            // header was derived independently — which is exactly what happens when someone
            // "optimises" this into a single calculation over the basket.
            gross += vat.LineGrossPence;
            ex += vat.LineExPence;
        }

        return new IngestSaleRequest
        {
            SaleId = saleId,
            DeviceId = deviceId,
            DeviceSeq = deviceSeq,
            Channel = channel,
            BusinessDay = businessDay,
            OccurredAtUtc = occurredAtUtc,
            GrossPence = gross,
            VatPence = gross - ex,
            Note = note,
            OperatorUserId = operatorUserId,
            Lines = ingestLines,
            Tenders = tenders.ToList(),
        };
    }

    /// <summary>
    /// What the basket is worth right now, for the on-screen total.
    ///
    /// ⚠ THE SAME ARITHMETIC AS <see cref="Assemble"/>, deliberately sharing
    /// <see cref="VatLineMath.ForLine"/>. The web till has a second implementation of this for its
    /// display total (`basketTotals`) and `till-design.md` C2 records it as an unpinned third copy:
    /// if the screen and the payload disagree, the customer is shown one number and charged
    /// another. Here there is one calculation and the display is a projection of it.
    /// </summary>
    public static BasketTotals Total(IReadOnlyList<BasketLine> lines)
    {
        if (lines is null || lines.Count == 0) return new BasketTotals(0, 0, 0);

        long gross = 0, ex = 0;
        foreach (var line in lines)
        {
            var vat = VatLineMath.ForLine(
                line.UnitIncPence, line.UnitExPence, line.Quantity, line.DiscountPence, line.IsReturn);
            gross += vat.LineGrossPence;
            ex += vat.LineExPence;
        }

        return new BasketTotals(gross, ex, gross - ex);
    }
}
