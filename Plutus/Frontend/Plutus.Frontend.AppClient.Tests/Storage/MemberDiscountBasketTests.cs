using System;
using System.Collections.Generic;
using System.Linq;
using Database.Models;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.Storage;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Storage
{
    /// <summary>
    /// The members' automatic discount on a MAUI basket — retrofit step 27.
    ///
    /// ⚠⚠ THIS CLOSES A LIVE MONEY DIFFERENCE: a Gold member was charged 10% more on this till than
    /// on the web till for the same basket, because `autoDiscountRate` appeared nowhere in the app.
    ///
    /// ⚠ THE TRAP THESE TESTS EXIST FOR. MAUI carries a discount as ONE `BasketAlteration` that
    /// `CheckoutCommit.ApplyAlterations` apportions at commit, and `TargetsOf` excludes returns but
    /// NOT already-discounted or gift-card lines — both of which the shared rule excludes. So a
    /// whole-basket member alteration would put money on lines the rule forbids, on one till only.
    /// The alteration must NAME its eligible items.
    /// </summary>
    public class MemberDiscountBasketTests
    {
        private static readonly Guid Cashier = Guid.Parse("11111111-1111-1111-1111-111111111111");

        private static BasketItem Item(string idOne, decimal price, decimal exPrice, int qty = 1) =>
            new(new ItemModel
            {
                Id = idOne,
                Name = "Item " + idOne,
                Price = price,
                ExPrice = exPrice,
                Vat = new TaxModel { Name = "Standard" },
            }, qty);

        private static BasketReturnItem Returned(string idOne, decimal price) =>
            new(new ItemModel
            {
                Id = idOne, Name = "Returned " + idOne, Price = price, ExPrice = price,
                Vat = new TaxModel { Name = "Standard" },
            }, 1);

        private static BasketAlteration ManualDiscount(decimal price, BasketItem on) =>
            new(new NoteModel { Note = "Manual" }, new DiscountModel { Id = 7 }, on, price, price);

        private static BasketAlteration Build(IEnumerable<IBasketRecord> basket, decimal rate = 0.10m) =>
            MemberDiscountBasket.Build(basket, "Gold", rate, hasMembership: true, expired: false, Cashier);

        // ── the money ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public void A_gold_member_gets_ten_percent_off_and_the_alteration_carries_it_as_negative_money()
        {
            var basket = new List<IBasketRecord> { Item("A", 10m, 10m) };

            var alteration = Build(basket);

            Assert.NotNull(alteration);
            Assert.Equal(-1m, alteration.Price);          // £1.00 off £10.00
            Assert.Equal("Gold 10%", alteration.Note.Note);
        }

        /// <summary>
        /// ⚠ THE PARITY ASSERTION. The web till computes this per line through
        /// `lineDiscountPence`, which rounds on the WHOLE line. Applying the rate once to a basket
        /// total instead would disagree by a penny on baskets that do not divide evenly — and it
        /// would disagree only sometimes, which is the worst way for two tills to differ.
        /// £3.33 + £3.33 + £3.33 at 10% is 33p + 33p + 33p = 99p, NOT round(999 × 0.1) = 100p.
        /// </summary>
        [Fact]
        public void The_discount_is_computed_per_line_and_summed_never_as_a_rate_on_the_basket_total()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 3.33m, 3.33m), Item("B", 3.33m, 3.33m), Item("C", 3.33m, 3.33m),
            };

            var alteration = Build(basket);

            Assert.Equal(-0.99m, alteration.Price);
        }

        [Fact]
        public void Quantity_is_taken_into_account()
        {
            var basket = new List<IBasketRecord> { Item("A", 10m, 10m, qty: 3) };

            Assert.Equal(-3m, Build(basket).Price);
        }

        // ── the four exclusions ───────────────────────────────────────────────────────────────

        /// <summary>⚠ A refund gives back what the customer actually paid. Discounting it would
        /// refund them less than they handed over.</summary>
        [Fact]
        public void A_return_is_never_discounted()
        {
            var basket = new List<IBasketRecord> { Returned("R", 10m) };

            Assert.Null(Build(basket));
        }

        /// <summary>
        /// ⚠⚠ NO STACKING, AND THIS IS THE ONE THE ASSOCIATION EXISTS FOR. `TargetsOf` would happily
        /// apportion a whole-basket member alteration onto an already-discounted line; naming the
        /// eligible items is what stops it. A member's rate on top of a staff discount is how a
        /// basket leaves for less than cost without anyone choosing that.
        /// </summary>
        [Fact]
        public void A_line_that_already_has_a_manual_discount_is_excluded_from_the_association()
        {
            var discounted = Item("A", 10m, 10m);
            var plain = Item("B", 20m, 20m);
            var basket = new List<IBasketRecord> { discounted, plain, ManualDiscount(-5m, discounted) };

            var alteration = Build(basket);

            // Only B is eligible: 10% of £20.
            Assert.Equal(-2m, alteration.Price);
            var associated = Assert.Single(alteration.ItemsAssocitated);
            Assert.Same(plain, associated);
        }

        /// <summary>⚠ Stored value is a liability, not a supply. Selling £50 of spendable money for
        /// £45 hands over £50 of purchasing power, which is then spent again on discounted goods.</summary>
        [Fact]
        public void A_gift_card_activation_line_is_never_discounted()
        {
            var basket = new List<IBasketRecord> { Item(GiftCards.ItemIdOne, 50m, 50m) };

            Assert.Null(Build(basket));
        }

        /// <summary>⚠ MAUI's alone: the web till builds its card fee inside `checkout()` where no
        /// discount can reach it, while MAUI adds a real basket line mid-tender. Without this a
        /// member would get 10% off the card fee on one till and not the other.</summary>
        [Fact]
        public void The_card_surcharge_line_is_never_discounted()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 10m, 10m),
                Item(CardSurchargeVat.ItemIdOne, 1m, 1m),
            };

            var alteration = Build(basket);

            Assert.Equal(-1m, alteration.Price);   // 10% of the £10 item only
            Assert.Same(basket[0], Assert.Single(alteration.ItemsAssocitated));
        }

        /// <summary>
        /// ⚠ A WHOLE-BASKET manual discount (no association) discounts everything, so nothing is
        /// eligible. Answering "all of them" would stack the member's rate on top of the operator's
        /// "£5 off the basket".
        /// </summary>
        [Fact]
        public void A_whole_basket_manual_discount_leaves_nothing_eligible()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 10m, 10m),
                new BasketAlteration(new NoteModel { Note = "5 off" }, new DiscountModel { Id = 7 },
                    Array.Empty<BasketItem>(), -5m, -5m),
            };

            Assert.Null(Build(basket));
        }

        // ── membership state ──────────────────────────────────────────────────────────────────

        /// <summary>⚠ A lapsed Gold member is a Gold member who is not currently entitled — the row
        /// keeps its rate so it can be renewed, and the rate must be ignored rather than trusted.</summary>
        [Fact]
        public void An_expired_membership_grants_nothing()
        {
            var basket = new List<IBasketRecord> { Item("A", 10m, 10m) };

            Assert.Null(MemberDiscountBasket.Build(
                basket, "Gold", 0.10m, hasMembership: true, expired: true, Cashier));
        }

        [Theory]
        [InlineData(false, 0.10)]   // attached, but no membership
        [InlineData(true, 0.0)]     // membership with no discount rate
        public void No_membership_and_no_rate_both_grant_nothing(bool hasMembership, double rate)
        {
            var basket = new List<IBasketRecord> { Item("A", 10m, 10m) };

            Assert.Null(MemberDiscountBasket.Build(
                basket, "Gold", (decimal)rate, hasMembership, expired: false, Cashier));
        }

        // ── recomputing, which is where this silently breaks ──────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE RE-APPLY TRAP. The basket changes on every scan, so the member alteration is
        /// rebuilt constantly. If the rebuild counted the member's OWN previous alteration as "this
        /// line is already discounted", every line would be ineligible from the second scan onwards
        /// and the discount would silently collapse to nothing — a member charged full price with a
        /// discount line still on screen.
        /// </summary>
        [Fact]
        public void Recomputing_ignores_the_members_own_previous_alteration()
        {
            var item = Item("A", 10m, 10m);
            var existing = Build(new List<IBasketRecord> { item });
            var basket = new List<IBasketRecord> { item, existing };

            var rebuilt = MemberDiscountBasket.Build(
                basket, "Gold", 0.10m, hasMembership: true, expired: false, Cashier, replacing: existing);

            Assert.NotNull(rebuilt);
            Assert.Equal(-1m, rebuilt.Price);
        }

        /// <summary>⚠ Found by SENTINEL id, not by label or position — detaching a customer must
        /// remove the member's discount and leave the operator's manual ones alone, exactly as the
        /// web till's `clearMemberDiscount` filters on `discountId === 0`.</summary>
        [Fact]
        public void The_members_alteration_is_identified_by_its_sentinel_id_not_by_its_words()
        {
            var item = Item("A", 10m, 10m);
            var manual = ManualDiscount(-2m, item);
            var member = Build(new List<IBasketRecord> { item });
            var basket = new List<IBasketRecord> { item, manual, member };

            var found = MemberDiscountBasket.ExistingOn(basket);

            Assert.Same(member, found);
            Assert.Equal(MemberDiscount.SentinelDiscountId, found.Discount.Id);
            Assert.NotSame(manual, found);
        }

        // ── the audit trail (default 22c) ─────────────────────────────────────────────────────

        /// <summary>
        /// ⚠ "All discounts need to be tracked" includes the automatic ones — this is the discount
        /// most likely to be queried later ("why is this basket 10% under?"). ⚠ With NO authoriser:
        /// the tier authorised it and the tier is portal-configured. Naming the operator there would
        /// manufacture a self-approval that never happened.
        /// </summary>
        [Fact]
        public void The_members_discount_carries_its_own_audit_record_with_no_authoriser()
        {
            var alteration = Build(new List<IBasketRecord> { Item("A", 10m, 10m) });

            Assert.Equal("Gold 10%", alteration.DiscountReason);
            Assert.Equal(Cashier, alteration.RequestedByUserId);
            Assert.Null(alteration.AuthorisedByUserId);
            Assert.Null(alteration.AuthorisedByName);
        }

        // ── end to end through the commit path ────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE POINT OF THE ASSOCIATION, PROVEN THROUGH THE REAL COMMIT PATH rather than asserted
        /// on the alteration. `CheckoutCommit.ApplyAlterations` is what actually decides which lines
        /// the money lands on, and its `TargetsOf` does NOT know about gift cards or already-
        /// discounted lines. This test fails if anyone "simplifies" the association away.
        /// </summary>
        [Fact]
        public void At_commit_the_members_money_lands_only_on_the_lines_it_named()
        {
            var discounted = Item("A", 10m, 10m);
            var plain = Item("B", 20m, 20m);

            var basket = new List<IBasketRecord> { discounted, plain, ManualDiscount(-5m, discounted) };
            basket.Add(Build(basket));

            var lines = CheckoutCommit.LinesFrom(basket);

            var a = lines.Single(l => l.IdOne == "A");
            var b = lines.Single(l => l.IdOne == "B");

            Assert.Equal(500, a.DiscountPence);   // the manual £5 only — no member money
            Assert.Equal(200, b.DiscountPence);   // 10% of £20, the member's alone
        }

        /// <summary>⚠ And the audit trail survives the apportionment: the member's reason reaches
        /// the line it landed on, so "why is this basket 10% under?" is answerable from the sale.</summary>
        [Fact]
        public void The_members_reason_reaches_the_sale_line()
        {
            var item = Item("A", 10m, 10m);
            var basket = new List<IBasketRecord> { item };
            basket.Add(Build(basket));

            var line = Assert.Single(CheckoutCommit.LinesFrom(basket));
            var authority = Assert.Single(line.DiscountAuthorities);

            Assert.Equal("Gold 10%", authority.Reason);
            Assert.Equal(100, authority.AmountPence);
            Assert.Null(authority.AuthorisedByUserId);
        }
    }
}
