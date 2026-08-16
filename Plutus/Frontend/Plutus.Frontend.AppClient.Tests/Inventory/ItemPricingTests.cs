using System;
using Plutus.Frontend.AppClient.Services.Inventory;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Inventory
{
    /// <summary>
    /// The item price pair and the catalogue's band guard (WP10).
    ///
    /// ⚠⚠ WHY THE GUARD EXISTS AT ALL: free-typed ex prices corrupted **47 live items** — a £7.99
    /// item carrying a £799.00 ex price — and with them every downstream VAT figure. The server
    /// refuses `|price − exPrice × rate| > 2p`; this is the client half, so the operator is told
    /// while the form is still open rather than after a round trip.
    /// </summary>
    public class ItemPricingTests
    {
        // ── deriving the ex price ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE MULTIPLIER DIVIDES. `Rate` from `/api/Tax/Index` is 1.2 for 20%, so a £10 item is
        /// £8.33 ex — MULTIPLYING would make it £12.00, and the server rejects the write with a
        /// message that never mentions units.
        /// </summary>
        [Theory]
        [InlineData(10.00, 1.2, 8.33)]
        [InlineData(7.99, 1.2, 6.66)]
        [InlineData(10.00, 1.05, 9.52)]
        [InlineData(10.00, 1.0, 10.00)]   // zero-rated: ex == inc
        public void The_ex_price_divides_by_the_band_multiplier(double inc, double rate, double expectedEx)
        {
            var ex = ItemPricing.ExPriceFor((decimal)inc, (decimal)rate, currentInc: 0m, currentEx: 0m);

            Assert.Equal((decimal)expectedEx, ex);
        }

        /// <summary>
        /// ⚠ AN UNKNOWN BAND KEEPS THE ITEM EDITABLE. An item whose tax row `/api/Tax/Index` does not
        /// know must not become unsaveable — the operator is usually editing it precisely BECAUSE
        /// something about it is wrong. Its existing ratio is carried instead.
        /// </summary>
        [Fact]
        public void An_unknown_band_preserves_the_items_existing_ratio()
        {
            // The item was £12.00 inc / £10.00 ex — a 1.2 ratio. Repricing to £24 keeps it.
            var ex = ItemPricing.ExPriceFor(24.00m, bandMultiplier: null, currentInc: 12.00m, currentEx: 10.00m);

            Assert.Equal(20.00m, ex);
        }

        /// <summary>⚠ And an item with no usable current pair falls back to ex == inc rather than
        /// dividing by zero. A wrong-but-saveable item beats an unsaveable one.</summary>
        [Fact]
        public void An_unknown_band_and_no_prior_pair_gives_ex_equal_to_inc()
        {
            Assert.Equal(24.00m, ItemPricing.ExPriceFor(24.00m, null, currentInc: 0m, currentEx: 0m));
        }

        /// <summary>
        /// ⚠⚠ THE CORRUPTION MUST NOT SPREAD. This is the 47-item bug trying to reproduce itself: the
        /// £7.99/£799.00 item has a ratio of 100, so carrying that ratio through a reprice to £9.99
        /// would write a £999.00 ex price and corrupt the item all over again at a new price point.
        /// An implausible ratio is dropped for ex == inc — wrong in the safe, visible direction.
        /// </summary>
        [Fact]
        public void An_unknown_band_refuses_to_carry_a_corrupt_ratio()
        {
            var ex = ItemPricing.ExPriceFor(9.99m, null, currentInc: 7.99m, currentEx: 799.00m);

            Assert.Equal(9.99m, ex);
            Assert.NotEqual(999.00m, ex);   // what carrying the ratio blindly would have produced
        }

        /// <summary>⚠ EX ABOVE INC IS NEVER VALID — VAT is never negative. That one test is the whole
        /// plausibility rule, so it is worth pinning at the boundary: equal is fine (zero-rated),
        /// a penny over is not.</summary>
        [Theory]
        [InlineData(10.00, 8.33, true)]    // ordinary 20% pair
        [InlineData(10.00, 10.00, true)]   // zero-rated — ex == inc is legitimate
        [InlineData(10.00, 10.01, false)]  // a penny of negative VAT is still corrupt
        [InlineData(10.00, 0.00, false)]   // no ex price stored — nothing to carry
        [InlineData(0.00, 8.33, false)]    // no inc price — would divide by zero
        public void A_carried_pair_is_usable_only_when_ex_is_between_zero_and_inc(
            double inc, double ex, bool usable)
        {
            Assert.Equal(usable, ItemPricing.CarriedRatioIsUsable((decimal)inc, (decimal)ex));
        }

        // ── the guard ─────────────────────────────────────────────────────────────────────────

        /// <summary>⚠ A derived pair must ALWAYS pass the guard — if it did not, the till would
        /// refuse its own arithmetic.</summary>
        [Theory]
        [InlineData(10.00, 1.2)]
        [InlineData(7.99, 1.2)]
        [InlineData(0.99, 1.2)]
        [InlineData(1234.56, 1.05)]
        public void A_derived_pair_always_satisfies_the_guard(double inc, double rate)
        {
            var price = (decimal)inc;
            var ex = ItemPricing.ExPriceFor(price, (decimal)rate, 0m, 0m);

            Assert.True(ItemPricing.IsBandConsistent(price, ex, (decimal)rate));
        }

        /// <summary>
        /// ⚠⚠ THE 47-ITEM BUG, CAUGHT. A £7.99 item with a £799.00 ex price is the shape that
        /// actually happened, and the guard must refuse it.
        /// </summary>
        [Fact]
        public void The_price_pair_that_corrupted_47_live_items_is_refused()
        {
            Assert.False(ItemPricing.IsBandConsistent(7.99m, 799.00m, 1.2m));
        }

        /// <summary>⚠ And the classic units mistake — MULTIPLYING by the band instead of dividing.
        /// £10 inc with a £12 ex price is what that produces.</summary>
        [Fact]
        public void Multiplying_by_the_band_instead_of_dividing_is_refused()
        {
            Assert.False(ItemPricing.IsBandConsistent(10.00m, 12.00m, 1.2m));
        }

        /// <summary>
        /// ⚠ THE TOLERANCE IS THE SERVER'S — 2p, from the shared constant. A pair a penny out is
        /// ACCEPTED, because rounding a real price pair can legitimately land there and refusing it
        /// would block ordinary edits.
        /// </summary>
        [Fact]
        public void A_pair_within_the_servers_tolerance_is_accepted()
        {
            // £10.00 at 1.2 derives £8.33; £8.32 is a penny out and must still pass.
            Assert.True(ItemPricing.IsBandConsistent(10.00m, 8.32m, 1.2m));
            Assert.True(ItemPricing.IsBandConsistent(10.00m, 8.34m, 1.2m));
        }

        [Fact]
        public void A_pair_well_outside_the_tolerance_is_refused()
        {
            Assert.False(ItemPricing.IsBandConsistent(10.00m, 8.00m, 1.2m));
        }

        /// <summary>
        /// ⚠ AN UNKNOWN BAND IS NOT A FAILURE. There is no rate to be inconsistent WITH, and
        /// refusing would make an unclassified item permanently unsaveable. The server has the last
        /// word either way.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData(0.0)]
        public void An_unknown_band_never_blocks_a_save(double? rate)
        {
            Assert.True(ItemPricing.IsBandConsistent(10.00m, 999.00m, (decimal?)rate));
        }

        // ── what the operator is told ─────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠ THE MESSAGE NAMES THE NUMBER THE SERVER EXPECTED. "That price is inconsistent" sends
        /// somebody back to a form with no idea which of two fields to change.
        /// </summary>
        [Fact]
        public void The_refusal_says_what_the_ex_price_should_have_been()
        {
            var message = ItemPricing.InconsistencyMessage(10.00m, 1.2m);

            Assert.Contains("8.33", message);
            Assert.Contains("band", message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
