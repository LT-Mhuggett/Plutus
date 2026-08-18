using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Overriding a price at the till keeps the item's VAT proportion.
///
/// ⚠⚠ THE FAULT THIS PINS. MAUI asked the operator for the ex-VAT price AND the inc-VAT price in one
/// dialog and wrote both onto the line. The sale line's declared rate is derived from that pair, so a
/// mistyped (or deliberately odd) pair became the VAT figure on the sale — 5% on a 20% item, with
/// nothing to notice. One number in, the other derived, is the fix.
///
/// ⚠ C2 TWIN of the web till's `adjust` reducer (`till/basket.ts`) and `priceAdjust.test.ts` — the
/// vectors below are shared.
/// </summary>
public class PriceAdjustTests
{
    /// <summary>A standard-rated item: £12.00 inc / £10.00 ex. Override to £6.00 → £5.00 ex.</summary>
    [Fact]
    public void A_standard_rated_override_keeps_20_percent()
    {
        Assert.Equal(500, PriceAdjust.ExFromInc(600, 1200, 1000));
    }

    /// <summary>⚠ THE ONE A SEPARATE EX FIELD GETS WRONG. Zero-rated goods have ex == inc, so an
    /// override must stay zero-rated — not acquire VAT because somebody typed into two boxes.</summary>
    [Fact]
    public void A_zero_rated_override_stays_zero_rated()
    {
        Assert.Equal(750, PriceAdjust.ExFromInc(750, 1000, 1000));
    }

    /// <summary>
    /// ⚠ AWAY FROM ZERO, not banker's. 5 × 50 ÷ 100 is exactly 2.5: away-from-zero says 3, .NET's
    /// DEFAULT `Math.Round` says 2 — and the web till's `Math.round` says 3. Mutation-checked
    /// 2026-08-18: switching to `ToEven` kills two of these tests.
    ///
    /// ⚠⚠ IT DOES **NOT** PIN PRODUCTS-BEFORE-DIVISION, AND SAYING SO MATTERS. The ratio-first mutant
    /// (`newInc × (ex ÷ inc)`) SURVIVES every vector here, because `decimal` division carries 28–29
    /// digits and the product still rounds to the same penny. Products-first stays because it is the
    /// house convention and because the **TypeScript** twin is where it bites — a double rounds
    /// `70/100` inexactly, so ratio-first there returns 31 for `45 × 70 ÷ 100` instead of 32. Same
    /// lesson as the card surcharge (§5b W-P7): a mutant that dies in one language can live in the
    /// other, so the vector belongs in BOTH suites and the mutation must be run on both.
    /// </summary>
    [Fact]
    public void The_derived_ex_rounds_away_from_zero_after_one_multiplication()
    {
        Assert.Equal(32, PriceAdjust.ExFromInc(45, 100, 70));
        // ⚠ An exact .5 that banker's rounding would send to 2 instead of 3.
        Assert.Equal(3, PriceAdjust.ExFromInc(5, 100, 50));
    }

    /// <summary>Overriding twice derives from the CATALOGUE both times, so it cannot drift.</summary>
    [Fact]
    public void Repeated_overrides_do_not_drift()
    {
        var once = PriceAdjust.ExFromInc(999, 1200, 1000);
        var twice = PriceAdjust.ExFromInc(999, 1200, 1000);
        Assert.Equal(once, twice);
        Assert.Equal(833, once);   // 999 × 1000 ÷ 1200 = 832.5 → 833
    }

    /// <summary>A free line, or a catalogue row with no price: no proportion to keep, so ex == inc and
    /// the line claims no VAT. ⚠ Conservative on purpose — the same answer the web till's `: 1`
    /// fallback gives.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void A_catalogue_with_no_usable_inc_price_yields_ex_equals_inc(long catInc)
    {
        Assert.Equal(500, PriceAdjust.ExFromInc(500, catInc, 400));
    }

    /// <summary>⚠ An ex ABOVE the inc would declare negative VAT; an ex of zero or less is not a
    /// price. Both mean the catalogue row is wrong, and neither may reach a VAT return through here.
    /// </summary>
    [Fact]
    public void An_impossible_catalogue_pair_is_clamped_rather_than_propagated()
    {
        Assert.Equal(500, PriceAdjust.ExFromInc(500, 1000, 1200));   // ex > inc → treat as no VAT
        Assert.Equal(0, PriceAdjust.ExFromInc(500, 1000, 0));        // no ex → all VAT
    }

    /// <summary>Zero is a legitimate override (a giveaway), and it must not become a negative ex.</summary>
    [Fact]
    public void A_zero_override_is_zero_on_both_halves()
    {
        Assert.Equal(0, PriceAdjust.ExFromInc(0, 1200, 1000));
    }
}
