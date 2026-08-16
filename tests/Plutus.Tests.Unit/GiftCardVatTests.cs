using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// When a gift card's VAT falls due (WP13).
///
/// ⚠⚠ THE FAILURE MODE HERE IS SILENT AND PERMANENT. Every figure still adds up whichever way this
/// goes; what changes is which VAT PERIOD the money lands in, and whether output tax is declared
/// once or twice. Nothing downstream can detect it — a wrong answer here is only ever found by an
/// inspection.
///
/// ⚠ The rule was extracted from the web till's `api.ts` checkout BEFORE MAUI grew a second copy,
/// per CLAUDE.md's C2 discipline. These tests are the pin on the .NET side.
/// </summary>
public class GiftCardVatTests
{
    // ── multi-purpose: a liability, not a supply ──────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE ONE THAT MATTERS MOST. A multi-purpose card is stored value — nothing has been
    /// supplied yet, so nothing is VAT-able yet. ex == inc, which makes `VatLineMath.ForLine`
    /// derive 0bp with no separate "zero-rate" flag anybody could forget to set.
    ///
    /// ⚠ Declaring VAT here would declare it TWICE: once on the card, and again on the goods it is
    /// eventually spent on.
    /// </summary>
    [Theory]
    [InlineData(2000L)]
    [InlineData(1L)]
    [InlineData(50_000L)]
    public void A_multi_purpose_card_declares_no_vat_at_the_sale(long loaded)
    {
        var (inc, ex) = GiftCardVat.PairFor(loaded, VoucherTreatment.Multi, standardRateBp: 2000);

        Assert.Equal(loaded, inc);
        Assert.Equal(inc, ex);        // ex == inc ⇒ 0bp
    }

    /// <summary>⚠ And the standard rate is IGNORED under multi — passing one must not tempt the
    /// arithmetic into using it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2000)]
    [InlineData(500)]
    public void The_standard_rate_cannot_leak_into_a_multi_purpose_card(int rateBp)
    {
        var (inc, ex) = GiftCardVat.PairFor(2000, VoucherTreatment.Multi, rateBp);

        Assert.Equal(inc, ex);
    }

    /// <summary>
    /// ⚠ NULL BAND UNDER MULTI, deliberately. "The portal has not decided which band this is" is a
    /// legitimate answer the server understands. ⚠ A zero-VAT LIABILITY is not a zero-RATED supply,
    /// and claiming the zero band would file stored value alongside cold food and children's
    /// clothes on the VAT report.
    /// </summary>
    [Fact]
    public void A_multi_purpose_card_claims_no_band_rather_than_the_zero_band()
    {
        Assert.Null(GiftCardVat.BandKeyFor(VoucherTreatment.Multi));
        Assert.NotEqual(VatRateHistory.Zero, GiftCardVat.BandKeyFor(VoucherTreatment.Multi));
    }

    // ── single-purpose: VAT now, inside the face value ────────────────────────────────────────

    /// <summary>
    /// ⚠ THE CUSTOMER PAYS THE FACE VALUE EITHER WAY — a £20 card costs £20. The treatment decides
    /// how much of that £20 is VAT, not what they hand over.
    /// £20 at 20% ⇒ ex £16.67 (2000 × 10000 ÷ 12000 = 1666.67 → 1667).
    /// </summary>
    [Fact]
    public void A_single_purpose_card_carries_the_vat_inside_its_face_value()
    {
        var (inc, ex) = GiftCardVat.PairFor(2000, VoucherTreatment.Single, standardRateBp: 2000);

        Assert.Equal(2000, inc);
        Assert.Equal(1667, ex);
    }

    /// <summary>⚠ The rate is the PUBLISHED one, never a literal 2000 — a tenant on 5% must get 5%.</summary>
    [Theory]
    [InlineData(2000, 2000, 1667)]   // 20%
    [InlineData(2000, 500, 1905)]    // 5%
    [InlineData(2000, 0, 2000)]      // 0% published ⇒ ex == inc, and that is arithmetic not a special case
    [InlineData(1000, 2000, 833)]
    public void The_published_rate_decides_the_split(long loaded, int rateBp, long expectedEx)
    {
        var (_, ex) = GiftCardVat.PairFor(loaded, VoucherTreatment.Single, rateBp);

        Assert.Equal(expectedEx, ex);
    }

    /// <summary>
    /// ⚠⚠ THE BAND IS STATED, NOT DERIVED. Round-tripping pence through the generic ratio wobbles
    /// the implied rate to 1998–2002bp, which scatters one tenant's card sales across several bands
    /// on the VAT report. The treatment says it IS the standard rate, so the line says so — this is
    /// the web till's own comment, carried across rather than rediscovered.
    /// </summary>
    [Fact]
    public void A_single_purpose_card_states_the_standard_band()
    {
        Assert.Equal(VatRateHistory.Standard, GiftCardVat.BandKeyFor(VoucherTreatment.Single));
    }

    /// <summary>⚠ And that statement is what protects the report: the pair alone implies a rate that
    /// is only APPROXIMATELY the standard one, because pence are integers.</summary>
    [Fact]
    public void The_stated_band_is_needed_because_the_pair_alone_only_approximates_the_rate()
    {
        var (inc, ex) = GiftCardVat.PairFor(2000, VoucherTreatment.Single, 2000);

        // 2000/1667 implies 1997.6bp — close to 2000, and NOT equal to it.
        var impliedBp = (int)Math.Round((inc / (decimal)ex - 1m) * 10000m);

        Assert.NotEqual(2000, impliedBp);
        Assert.InRange(impliedBp, 1990, 2010);
    }

    // ── no treatment chosen: refuse, never guess ──────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE SERVER 409s EVERY GIFT-CARD OPERATION WHILE THE TENANT HAS NOT CHOSEN, and this is the
    /// till's half of that rule. Throwing is correct: there is no sensible pair to return, and
    /// returning one would be exactly the guess this class exists to prevent.
    /// </summary>
    [Fact]
    public void With_no_treatment_chosen_a_card_cannot_be_priced_at_all()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GiftCardVat.PairFor(2000, VoucherTreatment.NotChosen, 2000));

        // ⚠ The message has to say WHY and WHERE to fix it — a cashier reading "invalid operation"
        // learns nothing, and this refusal happens with a customer at the counter.
        Assert.Contains("portal", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(GiftCardVat.CanSell(VoucherTreatment.NotChosen));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MULTI-PURPOSE")]
    [InlineData("something new")]
    public void An_unrecognised_treatment_reads_as_NOT_CHOSEN_rather_than_a_default(string? wire)
    {
        // ⚠ An unknown value means THIS BUILD does not understand what the tenant chose — which is
        // precisely when it must not sell a card. Defaulting to either answer would be a guess made
        // by an out-of-date till.
        Assert.Equal(VoucherTreatment.NotChosen, GiftCardVat.FromWireName(wire));
    }

    [Theory]
    [InlineData("multi", VoucherTreatment.Multi)]
    [InlineData("MULTI", VoucherTreatment.Multi)]
    [InlineData(" Single ", VoucherTreatment.Single)]
    public void The_wire_names_the_web_till_sends_are_understood(string wire, VoucherTreatment expected)
    {
        Assert.Equal(expected, GiftCardVat.FromWireName(wire));
    }

    // ── guards ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0L)]
    [InlineData(-500L)]
    public void A_card_cannot_be_loaded_with_nothing(long loaded)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GiftCardVat.PairFor(loaded, VoucherTreatment.Multi, 2000));
    }

    [Fact]
    public void Both_chosen_treatments_can_sell()
    {
        Assert.True(GiftCardVat.CanSell(VoucherTreatment.Multi));
        Assert.True(GiftCardVat.CanSell(VoucherTreatment.Single));
    }
}
