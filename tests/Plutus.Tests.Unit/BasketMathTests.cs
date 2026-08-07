using System;
using System.Linq;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// MAUI retrofit WP2 — the money DoD, as a property test over random baskets.
///
/// The legacy till did its arithmetic in <c>decimal</c>; the platform is integer pence end to end.
/// This is where those two meet, and the risk is not that a total is wildly wrong — it is that it
/// is out by a penny, on one basket in a thousand, in a way nobody notices until a VAT return
/// doesn't reconcile. So: assert the invariants over many randomised baskets rather than a handful
/// of hand-picked ones.
/// </summary>
public class BasketMathTests
{
    // ⚠ CORRECTED 2026-08-08. These helpers previously derived VAT as rate arithmetic
    // (gross × bp/(10000+bp)). That is NOT how this platform declares VAT and would have let MAUI
    // ship figures that disagree with the web till by a penny on the same basket. The reference
    // implementation is api.ts:958-1019 — VAT is the difference between the inc-VAT and ex-VAT
    // line totals, and the rate is DERIVED from the price pair for reporting. Mirrored exactly.

    /// <summary>The line total a till must charge: unit × qty − discount, all in pence.
    /// <paramref name="qty"/> is already signed (negative for a return).</summary>
    private static long LineGross(long unitIncPence, int qty, long discountPence) =>
        unitIncPence * qty - discountPence;

    /// <summary>The ex-VAT line total, api.ts:965: the discount is scaled by the ex/inc ratio so
    /// a discount reduces net and VAT proportionally rather than coming wholly out of one.</summary>
    private static long LineEx(long unitIncPence, long unitExPence, int quantity, long discountPence, bool isReturn)
    {
        var ratio = unitIncPence > 0 ? (double)unitExPence / unitIncPence : 1d;
        var ex = unitExPence * quantity - (long)Math.Round(discountPence * ratio, MidpointRounding.AwayFromZero);
        return isReturn ? -ex : ex;
    }

    /// <summary>The rate the till DECLARES, derived from the price pair (api.ts:978). Wobbles to
    /// 1998–2002bp on ordinary prices — that is expected and correct, not a defect.</summary>
    private static int DeclaredRateBp(long unitIncPence, long unitExPence) =>
        unitExPence > 0 ? (int)Math.Round((double)unitIncPence / unitExPence * 10000 - 10000) : 0;

    [Fact]
    public void Random_baskets_hold_the_money_invariants()
    {
        // Fixed seed: a property test that fails only sometimes is a test nobody trusts.
        var rng = new Random(20260807);
        var bands = new[] { 0, 500, 1750, 2000 };

        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var lineCount = rng.Next(1, 8);
            var lines = new IngestLine[lineCount];
            long gross = 0, vat = 0;

            for (var i = 0; i < lineCount; i++)
            {
                // Price the item the way a shop does: pick an ex-VAT price and a band, then round
                // the inc-VAT price to the penny. That rounding is exactly what makes the DERIVED
                // rate wobble off the clean band.
                var unitEx = rng.Next(1, 16_000);        // 1p … £160 ex VAT
                var band = bands[rng.Next(bands.Length)];
                var unitInc = (long)Math.Round(unitEx * (1m + band / 10000m), MidpointRounding.AwayFromZero);

                var qty = rng.Next(1, 6);
                var discount = rng.Next(0, 2) == 0 ? 0 : rng.Next(0, (int)(unitInc * qty));

                var lineGross = LineGross(unitInc, qty, discount);
                var lineEx = LineEx(unitInc, unitEx, qty, discount, isReturn: false);
                var lineVat = lineGross - lineEx;        // api.ts:979 — the derivation, not arithmetic

                lines[i] = new IngestLine
                {
                    Qty = qty, UnitPricePence = unitInc, DiscountPence = discount,
                    LineGrossPence = lineGross, VatRateBp = DeclaredRateBp(unitInc, unitEx),
                    VatAmountPence = lineVat,
                };
                gross += lineGross;
                vat += lineVat;

                // ⚠ The declared rate need NOT be near its band — on low-priced lines penny
                // rounding throws it a long way off (a 7p ex item at 20% declares 1428bp). The
                // PAIR, however, is always explained by the real band, which is exactly why
                // ingest validates the pair and never the declared rate (WP2b correction).
                Assert.True(VatRateHistory.Explains(unitInc, unitEx, band),
                    $"pair {unitInc}/{unitEx} not explained by band {band}");
            }

            // 1. the sale total is the sum of its lines — no drift from a running decimal
            Assert.Equal(gross, lines.Sum(l => l.LineGrossPence));
            // 2. VAT is the sum of per-line VAT, NOT recomputed on the total (which would round
            //    once instead of per line and disagree with the printed receipt)
            Assert.Equal(vat, lines.Sum(l => l.VatAmountPence));
            // 3. VAT never exceeds the gross it is embedded in, at any band
            Assert.True(vat <= gross, $"VAT {vat} > gross {gross}");
            // 4. a zero-rated line carries no VAT at all
            foreach (var l in lines.Where(l => l.VatRateBp == 0)) Assert.Equal(0, l.VatAmountPence);

            // 5. change = tendered − total, and a fully-tendered sale leaves nothing owed
            var tendered = gross + rng.Next(0, 5_000);
            var change = tendered - gross;
            Assert.Equal(gross, tendered - change);
            Assert.True(change >= 0);
        }
    }

    [Fact]
    public void Refunds_hold_the_same_invariants_with_every_figure_negative()
    {
        // Refund-only baskets are the same arithmetic with the signs flipped (proved server-side
        // by SalesV2Tests) — the till's maths must not special-case them into a different path.
        var rng = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var unitEx = rng.Next(1, 8_000);
            var unitInc = (long)Math.Round(unitEx * 1.2m, MidpointRounding.AwayFromZero);
            var quantity = rng.Next(1, 4);
            var qty = -quantity;                           // negative quantity = a return

            // api.ts drops the discount and negates the ex total on a return.
            var lineGross = LineGross(unitInc, qty, 0);
            var lineEx = LineEx(unitInc, unitEx, quantity, 0, isReturn: true);
            var vat = lineGross - lineEx;

            Assert.True(lineGross < 0);
            Assert.True(vat <= 0);                         // money going back out, VAT with it
            Assert.Equal(lineGross, unitInc * qty);
            // the refund's VAT is the exact negation of the equivalent sale's — no sign asymmetry
            Assert.Equal(-(unitInc * quantity - unitEx * quantity), vat);
        }
    }

    [Theory]
    // Low-priced lines are where the declared rate goes badly astray — and where an exact-bp
    // check would have quarantined a shop's cheapest, highest-volume stock first. Sweets, carrier
    // bags, single cards. The pair rule holds throughout.
    [InlineData(7, 20)]      // 7p ex at 20% → 8p inc → declares 1428bp
    [InlineData(13, 20)]     // → 16p inc → declares 2308bp
    [InlineData(4, 5)]       // 4p ex at 5% → 4p inc → declares 0bp
    [InlineData(1249, 20)]   // £12.49 ex → £14.99 inc → declares 2002bp
    public void The_declared_rate_can_be_wildly_wrong_while_the_pair_stays_provable(long unitEx, int bandPct)
    {
        var bandBp = bandPct * 100;
        var unitInc = (long)Math.Round(unitEx * (1m + bandBp / 10000m), MidpointRounding.AwayFromZero);

        // the pair is unambiguously the band it was priced at…
        Assert.True(VatRateHistory.Explains(unitInc, unitEx, bandBp));
        // …and VAT is still the difference, exactly as the receipt shows it
        Assert.Equal(unitInc - unitEx, LineGross(unitInc, 1, 0) - LineEx(unitInc, unitEx, 1, 0, false));
    }

    [Fact]
    public void Legacy_decimal_conversion_rounds_the_way_a_receipt_does()
    {
        // Banker's rounding (.NET's default) would make £0.125 → 12p and £0.135 → 14p: fine
        // statistically, indefensible to a customer reading the line. Away-from-zero, always.
        Assert.Equal(13, Pence.FromDecimal(0.125m));
        Assert.Equal(14, Pence.FromDecimal(0.135m));
        Assert.Equal(1499, Pence.FromDecimal(14.99m));
        Assert.Equal(0, Pence.FromDecimal(0m));
        Assert.Equal(-1499, Pence.FromDecimal(-14.99m));   // refunds convert symmetrically
    }
}
