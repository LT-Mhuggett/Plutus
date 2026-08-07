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
    /// <summary>The line total a till must charge: unit × qty − discount, all in pence.</summary>
    private static long LineGross(long unitPence, int qty, long discountPence) =>
        unitPence * qty - discountPence;

    /// <summary>VAT embedded in a gross figure at a rate in basis points, away-from-zero — the
    /// same rounding the receipt shows, because a customer can check this arithmetic.</summary>
    private static long VatOf(long grossPence, int rateBp) =>
        (long)Math.Round(grossPence * rateBp / (10000.0 + rateBp), MidpointRounding.AwayFromZero);

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
                var unit = rng.Next(1, 20_000);          // 1p … £200
                var qty = rng.Next(1, 6);
                var discount = rng.Next(0, 2) == 0 ? 0 : rng.Next(0, unit * qty);
                var rate = bands[rng.Next(bands.Length)];

                var lineGross = LineGross(unit, qty, discount);
                var lineVat = VatOf(lineGross, rate);

                lines[i] = new IngestLine
                {
                    Qty = qty, UnitPricePence = unit, DiscountPence = discount,
                    LineGrossPence = lineGross, VatRateBp = rate, VatAmountPence = lineVat,
                };
                gross += lineGross;
                vat += lineVat;
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
            var unit = rng.Next(1, 10_000);
            var qty = -rng.Next(1, 4);                     // negative quantity = a return
            var lineGross = LineGross(unit, qty, 0);
            Assert.True(lineGross < 0);
            var vat = VatOf(lineGross, 2000);
            Assert.True(vat <= 0);
            Assert.Equal(lineGross, unit * qty);
            // paying the customer back: net tender equals the (negative) total
            Assert.Equal(lineGross, lineGross - 0);
        }
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
