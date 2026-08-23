using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.Storage;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Storage
{
    /// <summary>
    /// Step 11b — the checkout's ORCHESTRATION, 2026-08-21.
    ///
    /// ⚠⚠ WHAT THIS COVERS AND WHY IT DID NOT EXIST. §7 called
    /// `ExecuteCheckoutTransaction` *"the last money-adjacent cluster in this app with no test
    /// coverage at all"*. That was stale by two rewrites: the arithmetic is `TenderSettlement`
    /// (mutation-checked, C2-twinned), the settle mapping is `CheckoutHelper.Settle`
    /// (`CheckoutScreenTests`), and the payload is `CheckoutCommit` (36 cases). **What genuinely had
    /// nothing was the orchestration** — the handful of decisions the viewmodel makes before and
    /// between those calls, sitting inside an `async void` no test can reach.
    ///
    /// Two of them decide real money behaviour, and both are now here rather than there:
    ///
    ///  • **the basket total**, which the checkout screen shows, the operator tenders against, and
    ///    `CheckoutCommit` then reconciles the payload against — and which the viewmodel derived
    ///    **four separate times in `decimal` pounds**, one of them by rounding the sum instead of
    ///    the lines;
    ///  • **`IsRefundOnly`**, which decides whether the customer may be paid back in cash.
    ///
    /// ⚠ These are NOT tests of VAT or of apportionment. `SaleAssembler.Total` owns the VAT-correct
    /// figure that goes on the wire; this is the *display and reconciliation* sum, and the whole
    /// point of the reconciliation guard is that the two are computed differently and compared.
    /// </summary>
    public class BasketMoneyTests
    {
        private static BasketItem Item(decimal price, decimal exPrice, int qty = 1) =>
            new(new Plutus.Frontend.AppClient.Models.TillItem
            {
                Id = "ITEM",
                Name = "Item",
                Price = price,
                ExPrice = exPrice,
                VatName = "Standard",
            }, qty);

        private static BasketItem Return(decimal price, decimal exPrice, int qty = 1)
        {
            var line = new BasketItem(new Plutus.Frontend.AppClient.Models.TillItem
            {
                Id = "ITEM",
                Name = "Item",
                Price = price,
                ExPrice = exPrice,
                VatName = "Standard",
            }, qty);

            // ⚠⚠ MARKED, NOT SUBCLASSED (step 11b, 2026-08-22). This helper returned a
            // `BasketReturnItem`, where the TYPE carried the meaning. Widening the return type
            // WITHOUT this call hands every test a SALE line named `Return` — the arithmetic
            // flips sign silently and the tests still pass, on the wrong numbers.
            line.MarkAsReturn();
            return line;
        }

        private static BasketNote Note(string text) =>
            new(text);

        private static BasketAlteration Discount(decimal negativePrice, params BasketItem[] appliesTo) =>
            new("Discount", new Plutus.Frontend.AppClient.Models.TillDiscount(),
                appliesTo, negativePrice, negativePrice);

        // ── the total ─────────────────────────────────────────────────────────

        [Fact]
        public void The_total_is_price_times_quantity_over_every_record()
        {
            var basket = new List<IBasketRecord> { Item(3.30m, 2.75m, qty: 3), Item(1.20m, 1.00m) };

            Assert.Equal(1110, CheckoutCommit.BasketMoneyPence(basket));
            Assert.Equal(925, CheckoutCommit.BasketMoneyExPence(basket));
        }

        /// <summary>⚠ A return is NEGATED, not skipped — the till nets them off, and a mixed basket
        /// is a sale for whatever is left over.</summary>
        [Fact]
        public void A_return_comes_off_the_total()
        {
            var basket = new List<IBasketRecord> { Item(10.00m, 8.33m), Return(4.00m, 3.33m) };

            Assert.Equal(600, CheckoutCommit.BasketMoneyPence(basket));
            Assert.Equal(500, CheckoutCommit.BasketMoneyExPence(basket));
        }

        /// <summary>⚠ Quantity applies to returns too. Five £30 returns is £150 going back, not £30 —
        /// the shape of a real defect: the pre-step-12 refund gate summed unit prices and ignored
        /// quantity, so five £30 returns tested as £30 and walked through the £100 band.</summary>
        [Fact]
        public void A_returns_quantity_is_not_ignored()
        {
            var basket = new List<IBasketRecord> { Return(30.00m, 25.00m, qty: 5) };

            Assert.Equal(-15000, CheckoutCommit.BasketMoneyPence(basket));
        }

        /// <summary>⚠ A discount is a RECORD with a negative price, not an adjustment to the item's
        /// price — the belief that it was is the step-11 defect that would have quarantined every
        /// discounted sale.</summary>
        [Fact]
        public void A_discount_record_comes_off_the_total()
        {
            var item = Item(10.00m, 8.33m);

            Assert.Equal(500, CheckoutCommit.BasketMoneyPence(
                new List<IBasketRecord> { item, Discount(-5.00m, item) }));
        }

        /// <summary>⚠ A plain note is not money and must not move the total.</summary>
        [Fact]
        public void A_note_carries_nothing()
        {
            var basket = new List<IBasketRecord> { Item(10.00m, 8.33m), Note("Gift wrapped") };

            Assert.Equal(1000, CheckoutCommit.BasketMoneyPence(basket));
        }

        [Fact]
        public void An_empty_or_null_basket_is_worth_nothing_rather_than_throwing()
        {
            Assert.Equal(0, CheckoutCommit.BasketMoneyPence(Array.Empty<IBasketRecord>()));
            Assert.Equal(0, CheckoutCommit.BasketMoneyPence(null));
            Assert.Equal(0, CheckoutCommit.BasketMoneyExPence(null));
        }

        /// <summary>
        /// ⚠⚠ THE ONE THIS FILE EXISTS FOR. `BasketMoneyPence`'s own header forbids
        /// `Pence.FromDecimal(Σ prices)` — *"summing decimals first and rounding once gives a
        /// different answer from rounding each line"* — and the checkout screen did exactly that
        /// until 2026-08-21, feeding the number the operator tenders against while this method
        /// guarded the commit.
        ///
        /// ⚠⚠ **AND THIS TEST CANNOT KILL THE MUTANT IT IS NAMED AFTER — SAID OUT LOUD RATHER THAN
        /// IMPLIED.** Replace `BasketMoneyPence` with `Pence.FromDecimal(Σ Price × qty)` and this
        /// stays green, because `Price` is an exact projection of `PricePence` and every reachable
        /// basket therefore agrees to the penny. That is precisely why the divergence removed on
        /// 2026-08-21 was **latent rather than live**: it was one un-pinned invariant away from
        /// mattering, and nothing failed while it held. What this test does pin is the invariant
        /// itself — *the pounds a customer reads is exactly the pence the commit reconciles* — so
        /// the day somebody gives a record a price that is not a projection, this is the test that
        /// goes red instead of a shop's sale being refused at the counter.
        ///
        /// ⚠ Mutation-checked 2026-08-21, three killed: dropping the return negation (**3** tests),
        /// `BasketMoneyExPence` reading the inc-VAT price (**2**), and dropping `IsRefundOnly`'s
        /// negation (**5**).
        /// </summary>
        [Fact]
        public void The_screen_total_and_the_reconciliation_total_are_the_same_number()
        {
            var basket = new List<IBasketRecord>
            {
                Item(0.01m, 0.01m, qty: 7),
                Item(3.33m, 2.78m, qty: 3),
                Return(1.11m, 0.93m, qty: 2),
                Note("nothing"),
            };

            var reconciliation = CheckoutCommit.BasketMoneyPence(basket);

            // The pounds-shaped view the till screen binds — `SaleIncTax` is exactly this expression.
            var onScreen = reconciliation / 100m;

            Assert.Equal(reconciliation, Plutus.SharedKernel.Pence.FromDecimal(onScreen));
            Assert.Equal(784, reconciliation);   // 7x1p + 3x333p - 2x111p
        }

        // ── refund-only ───────────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE MONEY CONSEQUENCE. Matt, 2026-08-11: *"Refunds need to ONLY offer the method that
        /// was used to pay."* Refunding a card sale in cash is the oldest till fraud there is.
        /// </summary>
        [Fact]
        public void A_basket_of_only_returns_is_refund_only()
        {
            Assert.True(CheckoutCommit.IsRefundOnly(
                new List<IBasketRecord> { Return(4.00m, 3.33m), Return(2.00m, 1.67m) }));
        }

        /// <summary>⚠ ONE sale line is enough to make it a sale. Restricting how a customer may pay
        /// the balance because one line is a return would be nonsense.</summary>
        [Fact]
        public void One_sale_line_beside_a_return_is_NOT_refund_only()
        {
            Assert.False(CheckoutCommit.IsRefundOnly(
                new List<IBasketRecord> { Return(40.00m, 33.33m), Item(0.10m, 0.08m) }));
        }

        /// <summary>⚠ Notes and discounts are not goods — a returns basket carrying a discount is
        /// still a refund.</summary>
        [Fact]
        public void Notes_and_discounts_do_not_make_a_returns_basket_into_a_sale()
        {
            var basket = new List<IBasketRecord> { Return(10.00m, 8.33m), Note("damaged"), Discount(-1.00m) };

            Assert.True(CheckoutCommit.IsRefundOnly(basket));
        }

        /// <summary>
        /// ⚠⚠ DOCUMENTING WHAT SHIPPED, NOT WHAT IS IDEAL. The predicate is *"no sale line"*, so an
        /// empty basket answers **true**. It was lifted out of `ExecuteCheckoutTransaction` verbatim
        /// and left that way on purpose: tightening it during an extraction would be a silent
        /// money-path change behind a refactor. The case is unreachable — an empty basket tenders
        /// nothing and `SaleAssembler` refuses a sale with no tender — and if it should read false,
        /// that is a decision with its own commit.
        /// </summary>
        [Fact]
        public void An_empty_basket_answers_true_because_that_is_what_shipped()
        {
            Assert.True(CheckoutCommit.IsRefundOnly(Array.Empty<IBasketRecord>()));
            Assert.True(CheckoutCommit.IsRefundOnly(null));
        }

        /// <summary>⚠ The card-surcharge line is a `BasketItem`, so it WOULD make a refund basket
        /// look like a sale. It cannot arise — the fee is only ever added to a non-refund basket
        /// (`SurchargeItem` returns null when there are no sale lines) — and this pins the ordering
        /// that keeps it that way.</summary>
        [Fact]
        public void A_refund_basket_is_never_given_a_surcharge_line_to_confuse_it()
        {
            var refundBasket = new List<IBasketRecord> { Return(10.00m, 8.33m) };

            Assert.Null(CheckoutCommit.SurchargeItem(refundBasket, surchargeBp: 200, surchargeFlatPence: 25));
            Assert.True(CheckoutCommit.IsRefundOnly(refundBasket));
        }
    }
}
