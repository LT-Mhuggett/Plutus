using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Cutover step 9 — the basket → <see cref="IngestSaleRequest"/> assembler.
///
/// ⚠ THE KEYSTONE OF THE MONEY PATH. Nothing in the repo built this payload before: the MAUI till
/// wrote a legacy EF object graph and called `db.Save()`, so however complete its screens looked it
/// could not sell into Plutus at all. Every penny of every sale and every VAT return flows through
/// the class these tests cover.
///
/// They are written to state the RULE rather than the number, because the numbers are the easy part
/// to reproduce from the implementation and the rules are what stop the two tills diverging.
/// </summary>
public class SaleAssemblerTests
{
    private static readonly Guid Business = Guid.Parse("d5a31aac-159e-9a30-706b-02f9eb935600");

    private static BasketLine Line(
        string idOne, long inc, long ex, int qty = 1, long discount = 0,
        bool isReturn = false, string? band = null, Guid? origin = null) =>
        new(DeterministicGuid.ForItem(Business, idOne), idOne, $"Item {idOne}",
            inc, ex, qty, discount, band, null, isReturn, origin);

    private static IngestSaleRequest Assemble(params BasketLine[] lines) =>
        SaleAssembler.Assemble(
            Uuid7.New(), Guid.NewGuid(), 1, Business, lines,
            new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = 0 } },
            new DateOnly(2026, 8, 9), DateTime.UtcNow);

    // ── the header must equal its own lines ──

    /// <summary>
    /// ⚠ THE INVARIANT THE SERVER ENFORCES. The header is the SUM of the lines, never recomputed
    /// from prices and rates — a sale whose header was derived independently is rejected by the
    /// ingest reconcile check, and the mixed-rate basket is where an independent calculation
    /// diverges first.
    /// </summary>
    [Fact]
    public void The_header_totals_equal_the_sum_of_the_lines_to_the_penny()
    {
        var sale = Assemble(
            Line("A", 1499, 1249),            // standard-rated
            Line("B", 250, 250),              // zero-rated
            Line("C", 999, 832, qty: 3));     // standard, quantity 3

        Assert.Equal(sale.Lines.Sum(l => l.LineGrossPence), sale.GrossPence);
        Assert.Equal(sale.Lines.Sum(l => l.VatAmountPence), sale.VatPence);

        // and the on-screen total is the SAME calculation, not a second one
        var display = SaleAssembler.Total(new[]
        {
            Line("A", 1499, 1249), Line("B", 250, 250), Line("C", 999, 832, qty: 3),
        });
        Assert.Equal(sale.GrossPence, display.GrossPence);
        Assert.Equal(sale.VatPence, display.VatPence);
    }

    /// <summary>
    /// ⚠ VAT IS GROSS − EX, never rate arithmetic (C1 rule 3). Rate arithmetic disagrees with the
    /// receipt by a penny on about a third of standard-rated lines, and the receipt is the thing
    /// the customer is holding.
    /// </summary>
    [Fact]
    public void Line_vat_is_gross_minus_ex_not_rate_arithmetic()
    {
        var sale = Assemble(Line("A", 1499, 1249));
        var line = Assert.Single(sale.Lines);

        Assert.Equal(1499, line.LineGrossPence);
        Assert.Equal(1499 - 1249, line.VatAmountPence);

        // ⚠ and the declared rate is DERIVED FROM THE PAIR, so it legitimately "wobbles" off 2000
        Assert.Equal(VatLineMath.RateBpFromPair(1499, 1249), line.VatRateBp);
        Assert.NotEqual(2000, line.VatRateBp);
    }

    // ── returns ──

    /// <summary>
    /// ⚠ A return negates every money figure AND DROPS THE DISCOUNT. Refunding a discounted sale
    /// returns what the customer actually paid; re-applying the discount would refund less than
    /// they handed over.
    /// </summary>
    [Fact]
    public void A_return_negates_the_money_and_carries_its_origin_sale()
    {
        var origin = Uuid7.New();
        var sale = Assemble(Line("A", 1499, 1249, discount: 100, isReturn: true, origin: origin));
        var line = Assert.Single(sale.Lines);

        Assert.True(line.LineGrossPence < 0);
        Assert.True(line.VatAmountPence < 0);
        Assert.True(line.Qty < 0);
        Assert.Equal(0, line.DiscountPence);
        Assert.Equal(-1499, line.LineGrossPence);

        var meta = LineMeta.FromJson(line.DiscountsJson);
        Assert.Equal(origin.ToString("D"), meta!.Return!.OriginSaleId);
    }

    /// <summary>⚠ A negative quantity alongside the return flag would DOUBLE-NEGATE and turn a
    /// refund into a payment. VatLineMath refuses it; the assembler must not swallow that.</summary>
    [Fact]
    public void A_negative_quantity_is_refused_rather_than_double_negated()
    {
        var bad = new BasketLine(
            DeterministicGuid.ForItem(Business, "A"), "A", "Item A", 1499, 1249, -1, IsReturn: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => Assemble(bad));
    }

    // ── the metadata the server reads ──

    /// <summary>
    /// ⚠ THE SILENT ONE, and the reason this throws rather than warns. `StockProjectionConsumer`
    /// only attributes a stock movement when it can read `itemIdOne` out of the line meta; a line
    /// without it is skipped with NO error, so stock quietly stops moving while every sale reports
    /// success. Refusing at assembly is the last point where that is still visible.
    /// </summary>
    [Fact]
    public void A_line_with_no_IdOne_is_refused_rather_than_silently_losing_its_stock_movement()
    {
        var bad = new BasketLine(Guid.Empty, "", "Mystery", 100, 100, 1);
        var ex = Assert.Throws<InvalidOperationException>(() => Assemble(bad));
        Assert.Contains("stock", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_line_carries_its_barcode_and_ex_price_in_the_meta_envelope()
    {
        var sale = Assemble(Line("BAT-001", 1499, 1249));
        var meta = LineMeta.FromJson(Assert.Single(sale.Lines).DiscountsJson);

        Assert.Equal("BAT-001", meta!.ItemIdOne);
        Assert.Equal(1249, meta.ExUnitPence);
    }

    // ── multi-barcode: which code was actually scanned (MB5) ──────────────────

    /// <summary>
    /// ⚠⚠ THE CANONICAL CODE IS `itemIdOne`; THE SCANNED ONE IS A SNAPSHOT BESIDE IT. Every reader
    /// keys on `itemIdOne` — stock, the VAT band stamp, item reports, the legacy bridge — so an alias
    /// reaching it would create a phantom `StockLevel` and drop the line's VAT band, both silently.
    /// This proves the two travel separately and correctly.
    /// </summary>
    [Fact]
    public void A_line_scanned_under_an_ADDITIONAL_barcode_records_both_codes()
    {
        var line = Line("BAT-001", 1499, 1249) with { ScannedBarcode = "OLD-SUPPLIER-CODE" };
        var meta = LineMeta.FromJson(Assert.Single(Assemble(line).Lines).DiscountsJson);

        Assert.Equal("BAT-001", meta!.ItemIdOne);              // canonical — what everything keys on
        Assert.Equal("OLD-SUPPLIER-CODE", meta.BarcodeScanned); // the snapshot
    }

    /// <summary>
    /// ⚠ OMITTED when the scanned code WAS the item's own, which is almost every line — so an
    /// ordinary sale's metadata stays byte-identical to what it was before this field existed. The
    /// same discipline `DiscountAuthority` follows, and for the same reason: a sale is compared
    /// against its own stored meta in more than one place.
    /// </summary>
    [Fact]
    public void The_scanned_barcode_is_omitted_when_it_is_the_items_own()
    {
        foreach (var scanned in new[] { (string?)null, "", "   ", "BAT-001" })
        {
            var line = Line("BAT-001", 1499, 1249) with { ScannedBarcode = scanned };
            var meta = LineMeta.FromJson(Assert.Single(Assemble(line).Lines).DiscountsJson);

            Assert.Null(meta!.BarcodeScanned);
        }
    }

    /// <summary>⚠ And the JSON itself carries no key at all in that case — `LineMeta` serialises with
    /// `WhenWritingNull`, which is what keeps an old payload byte-identical rather than merely
    /// null-valued.</summary>
    [Fact]
    public void An_ordinary_lines_json_gains_no_barcode_key()
    {
        var json = Assert.Single(Assemble(Line("BAT-001", 1499, 1249)).Lines).DiscountsJson;

        Assert.DoesNotContain("barcodeScanned", json);
    }

    /// <summary>
    /// ⚠ An unresolved VAT band is OMITTED, never guessed. Zero-rated and exempt are both 0% and
    /// different in law; the server falls back to snapping the rate and the portal flags the tax
    /// row as needing a decision. A guess here puts a number on a VAT return nobody chose.
    /// </summary>
    [Fact]
    public void An_unresolved_vat_band_is_omitted_rather_than_guessed()
    {
        var withBand = LineMeta.FromJson(Assert.Single(Assemble(Line("A", 100, 100, band: "zero")).Lines).DiscountsJson);
        Assert.Equal("zero", withBand!.VatBand);

        foreach (var unresolved in new[] { (string?)null, "", "   " })
        {
            var meta = LineMeta.FromJson(Assert.Single(Assemble(Line("B", 100, 100, band: unresolved)).Lines).DiscountsJson);
            Assert.Null(meta!.VatBand);
        }
    }

    // ── item identity ──

    /// <summary>
    /// ⚠ The item id is RE-DERIVED from the business id and barcode, not trusted from the caller.
    /// It is the same derivation the web till uses, so both tills attach the same barcode to the
    /// same item; a caller that built it another way would corrupt an item's sales history with no
    /// symptom until someone compared two reports.
    /// </summary>
    [Fact]
    public void The_item_id_is_derived_and_a_mismatched_one_is_refused()
    {
        var sale = Assemble(Line("BAT-001", 100, 100));
        Assert.Equal(DeterministicGuid.ForItem(Business, "BAT-001"), Assert.Single(sale.Lines).ItemId);

        var wrong = new BasketLine(Guid.NewGuid(), "BAT-001", "Item", 100, 100, 1);
        var ex = Assert.Throws<InvalidOperationException>(() => Assemble(wrong));
        Assert.Contains("DeterministicGuid", ex.Message);
    }

    /// <summary>A caller that has not derived an id yet (Guid.Empty) gets it derived for them —
    /// the assembler is the one place that needs to know the rule.</summary>
    [Fact]
    public void A_line_with_no_item_id_yet_has_one_derived_for_it()
    {
        var sale = Assemble(new BasketLine(Guid.Empty, "BAT-001", "Item", 100, 100, 1));
        Assert.Equal(DeterministicGuid.ForItem(Business, "BAT-001"), Assert.Single(sale.Lines).ItemId);
    }

    // ── refusals that protect the server's invariants ──

    [Fact]
    public void A_sale_with_no_lines_or_no_tenders_is_refused()
    {
        var line = Line("A", 100, 100);
        var tender = new[] { new IngestTender { TenderType = Tenders.Cash, AmountPence = 100 } };

        Assert.Throws<ArgumentException>(() => SaleAssembler.Assemble(
            Uuid7.New(), Guid.NewGuid(), 1, Business, Array.Empty<BasketLine>(), tender,
            new DateOnly(2026, 8, 9), DateTime.UtcNow));

        Assert.Throws<ArgumentException>(() => SaleAssembler.Assemble(
            Uuid7.New(), Guid.NewGuid(), 1, Business, new[] { line }, Array.Empty<IngestTender>(),
            new DateOnly(2026, 8, 9), DateTime.UtcNow));
    }

    [Fact]
    public void An_empty_basket_totals_zero_rather_than_throwing()
    {
        var totals = SaleAssembler.Total(Array.Empty<BasketLine>());
        Assert.Equal(0, totals.GrossPence);
        Assert.Equal(0, totals.VatPence);
    }
}

/// <summary>
/// Cutover step 9's other half — how an operator's discount becomes pence.
///
/// ⚠ This is a C2 twin with the web till's `lineDiscountPence` (`till/basket.ts`), and the reason
/// it is a shared rule at all is the bug it replaces: the legacy MAUI till computed
/// `item.Price * decimal.Parse(operatorInput)`, so entering `10` for "10%" multiplied the price BY
/// TEN and the basket charged it.
/// </summary>
public class LineDiscountsTests
{
    [Fact]
    public void A_fixed_discount_applies_to_every_unit()
    {
        // "£1 off" on three of something is £3, matching the web till.
        Assert.Equal(300, LineDiscounts.FixedPerUnit(100, 3, isReturn: false));
        Assert.Equal(100, LineDiscounts.FixedPerUnit(100, 1, isReturn: false));
    }

    [Fact]
    public void A_percentage_is_a_fraction_of_the_whole_line()
    {
        // 10% of 3 × £14.99 = £4.497 → £4.50 away from zero, rounded on the LINE not per unit.
        Assert.Equal(450, LineDiscounts.Percentage(1499, 3, 0.10m, isReturn: false));
        Assert.Equal(150, LineDiscounts.Percentage(1499, 1, 0.10m, isReturn: false));
    }

    /// <summary>
    /// ⚠ THE ×10 BUG, as its own test. A percent NUMBER where a fraction belongs is refused rather
    /// than obeyed — the legacy till multiplied the price by it and took the customer's money.
    /// </summary>
    [Theory]
    [InlineData(10)]     // "10" meaning 10%
    [InlineData(100)]    // "100" meaning 100%
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public void A_percent_number_where_a_fraction_belongs_is_refused(decimal fraction) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LineDiscounts.Percentage(1499, 1, fraction, isReturn: false));

    /// <summary>⚠ Never more than the line is worth — rounding at the 100% boundary would
    /// otherwise produce a discount a penny larger than the line, i.e. a negative-gross sale.</summary>
    [Fact]
    public void A_discount_never_exceeds_the_line()
    {
        Assert.Equal(1499, LineDiscounts.Percentage(1499, 1, 1.0m, isReturn: false));
        Assert.Equal(999, LineDiscounts.Percentage(333, 3, 1.0m, isReturn: false));
    }

    /// <summary>⚠ Returns take NO discount, on either till: a refund gives back what was actually
    /// paid, and re-applying the discount would refund less than the customer handed over.</summary>
    [Fact]
    public void A_return_takes_no_discount()
    {
        Assert.Equal(0, LineDiscounts.Percentage(1499, 1, 0.10m, isReturn: true));
        Assert.Equal(0, LineDiscounts.FixedPerUnit(100, 3, isReturn: true));
    }
}
