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
    /// Every AUTOMATIC discount on a MAUI basket — the member's tier and any live scheduled rule.
    ///
    /// ⚠⚠ THIS FILE REPLACES `MemberDiscountBasketTests`, WHOSE CLASS IS DELETED. `MemberDiscountBasket`
    /// handled one automatic discount; `AutoDiscountBasket` handles the set, and leaving both would be
    /// two divergent paths for one job — the drift till-design C2 exists to prevent, and the same
    /// "moved, not copied" rule the add-member screen followed. Every vector below that came from the
    /// old file is kept, because each one records a trap.
    ///
    /// ⚠ THE TRAP THIS CLASS EXISTS FOR, unchanged: MAUI carries a discount as a `BasketAlteration`
    /// that `CheckoutCommit.ApplyAlterations` apportions at commit, and `TargetsOf` excludes returns
    /// but NOT already-discounted or gift-card lines — both of which the shared rule excludes. So an
    /// unassociated alteration would put money on lines the rule forbids, on one till only. The
    /// alteration must NAME its eligible items.
    /// </summary>
    public class AutoDiscountBasketTests
    {
        private static readonly Guid Cashier = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid Warhammer = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid Paint = Guid.Parse("33333333-3333-3333-3333-333333333333");

        /// <summary>Wednesday 2026-08-19, 14:00 LOCAL — every schedule case is judged against this.</summary>
        private static readonly DateTime Wed = new(2026, 8, 19, 14, 0, 0, DateTimeKind.Local);
        private static readonly DateTime Thu = new(2026, 8, 20, 14, 0, 0, DateTimeKind.Local);
        private const byte Wednesdays = 1 << 3;

        private static readonly MemberStanding Gold = new(true, false, 0.10m, "Gold");

        private static BasketItem Item(string idOne, decimal price, decimal exPrice, int qty = 1, Guid? cat = null) =>
            new(new ItemModel
            {
                Id = idOne,
                Name = "Item " + idOne,
                Price = price,
                ExPrice = exPrice,
                Vat = new TaxModel { Name = "Standard" },
            }, qty)
            { CategoryId = cat };

        private static BasketItem Returned(string idOne, decimal price)
        {
            var line = new BasketItem(new ItemModel
            {
                Id = idOne, Name = "Returned " + idOne, Price = price, ExPrice = price,
                Vat = new TaxModel { Name = "Standard" },
            }, 1);

            // ⚠⚠ MARKED, NOT SUBCLASSED (step 11b, 2026-08-22). This helper returned a
            // `BasketReturnItem`, where the TYPE carried the meaning. Widening the return type
            // WITHOUT this call hands every test a SALE line named `Return` — the arithmetic
            // flips sign silently and the tests still pass, on the wrong numbers.
            line.MarkAsReturn();
            return line;
        }

        private static BasketAlteration ManualDiscount(decimal price, BasketItem on) =>
            new(new NoteModel { Note = "Manual" }, new DiscountModel { Id = 7 }, on, price, price);

        private static ScheduledDiscount Rule(
            decimal fraction = 0.10m, int id = 41, Guid? cat = null, byte? days = Wednesdays) =>
            new(id, "Wednesday Warhammer", DiscountKinds.Percentage, PercentFraction: fraction,
                AutoApply: true, DaysOfWeekMask: days,
                CategoryIds: new[] { cat ?? Warhammer });

        /// <summary>The member's discount alone, on a Thursday so no rule can interfere.</summary>
        private static IReadOnlyList<BasketAlteration> Member(IEnumerable<IBasketRecord> basket) =>
            AutoDiscountBasket.Build(basket, Gold, Array.Empty<ScheduledDiscount>(), Thu, Cashier);

        private static BasketAlteration OneMember(IEnumerable<IBasketRecord> basket) =>
            Member(basket).SingleOrDefault();

        // ── the money (ported: the member's discount) ────────────────────────────────────────

        [Fact]
        public void A_gold_member_gets_ten_percent_off_and_the_alteration_carries_it_as_negative_money()
        {
            var alteration = OneMember(new List<IBasketRecord> { Item("A", 10m, 10m) });

            Assert.NotNull(alteration);
            Assert.Equal(-1m, alteration.Price);          // £1.00 off £10.00
            Assert.Equal("Gold 10%", alteration.Note.Note);
        }

        /// <summary>
        /// ⚠ THE PARITY ASSERTION. The web till computes this per line through `lineDiscountPence`,
        /// which rounds on the WHOLE line. Applying the rate once to a basket total instead would
        /// disagree by a penny on baskets that do not divide evenly — and only sometimes, which is the
        /// worst way for two tills to differ. £3.33 × 3 at 10% is 33p+33p+33p = 99p, NOT
        /// round(999 × 0.1) = 100p.
        /// </summary>
        [Fact]
        public void The_discount_is_computed_per_line_and_summed_never_as_a_rate_on_the_basket_total()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 3.33m, 3.33m), Item("B", 3.33m, 3.33m), Item("C", 3.33m, 3.33m),
            };

            Assert.Equal(-0.99m, OneMember(basket).Price);
        }

        [Fact]
        public void Quantity_is_taken_into_account()
        {
            Assert.Equal(-3m, OneMember(new List<IBasketRecord> { Item("A", 10m, 10m, qty: 3) }).Price);
        }

        // ── the four exclusions (ported) ─────────────────────────────────────────────────────

        /// <summary>⚠ A refund gives back what the customer actually paid. Discounting it would refund
        /// them less than they handed over.</summary>
        [Fact]
        public void A_return_is_never_discounted()
        {
            Assert.Empty(Member(new List<IBasketRecord> { Returned("R", 10m) }));
        }

        /// <summary>
        /// ⚠⚠ NO STACKING, AND THIS IS THE ONE THE ASSOCIATION EXISTS FOR. `TargetsOf` would happily
        /// apportion an unassociated alteration onto an already-discounted line; naming the eligible
        /// items is what stops it. An automatic rate on top of a staff discount is how a basket leaves
        /// for less than cost without anyone choosing that.
        /// </summary>
        [Fact]
        public void A_line_that_already_has_a_manual_discount_is_excluded_from_the_association()
        {
            var discounted = Item("A", 10m, 10m);
            var plain = Item("B", 20m, 20m);
            var basket = new List<IBasketRecord> { discounted, plain, ManualDiscount(-5m, discounted) };

            var alteration = OneMember(basket);

            Assert.Equal(-2m, alteration.Price);   // only B is eligible: 10% of £20
            Assert.Same(plain, Assert.Single(alteration.ItemsAssocitated));
        }

        /// <summary>⚠ Stored value is a liability, not a supply. Selling £50 of spendable money for £45
        /// hands over £50 of purchasing power, which is then spent again on discounted goods.</summary>
        [Fact]
        public void A_gift_card_activation_line_is_never_discounted()
        {
            Assert.Empty(Member(new List<IBasketRecord> { Item(GiftCards.ItemIdOne, 50m, 50m) }));
        }

        /// <summary>⚠ MAUI's alone: the web till builds its card fee inside `checkout()` where no
        /// discount can reach it, while MAUI adds a real basket line mid-tender.</summary>
        [Fact]
        public void The_card_surcharge_line_is_never_discounted()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 10m, 10m),
                Item(CardSurchargeVat.ItemIdOne, 1m, 1m),
            };

            var alteration = OneMember(basket);

            Assert.Equal(-1m, alteration.Price);   // 10% of the £10 item only
            Assert.Same(basket[0], Assert.Single(alteration.ItemsAssocitated));
        }

        /// <summary>⚠ A WHOLE-BASKET manual discount (no association) discounts everything, so nothing
        /// is eligible. Answering "all of them" would stack an automatic rate on the operator's
        /// "£5 off the basket".</summary>
        [Fact]
        public void A_whole_basket_manual_discount_leaves_nothing_eligible()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 10m, 10m),
                new BasketAlteration(new NoteModel { Note = "5 off" }, new DiscountModel { Id = 7 },
                    Array.Empty<BasketItem>(), -5m, -5m),
            };

            Assert.Empty(Member(basket));
        }

        // ── membership state (ported) ────────────────────────────────────────────────────────

        /// <summary>⚠ A lapsed Gold member is a Gold member who is not currently entitled — the row
        /// keeps its rate so it can be renewed, and the rate must be ignored rather than trusted.</summary>
        [Fact]
        public void An_expired_membership_grants_nothing()
        {
            var basket = new List<IBasketRecord> { Item("A", 10m, 10m) };

            Assert.Empty(AutoDiscountBasket.Build(
                basket, new MemberStanding(true, true, 0.10m, "Gold"),
                Array.Empty<ScheduledDiscount>(), Thu, Cashier));
        }

        [Theory]
        [InlineData(false, 0.10)]   // attached, but no membership
        [InlineData(true, 0.0)]     // membership with no discount rate
        public void No_membership_and_no_rate_both_grant_nothing(bool hasMembership, double rate)
        {
            var basket = new List<IBasketRecord> { Item("A", 10m, 10m) };

            Assert.Empty(AutoDiscountBasket.Build(
                basket, new MemberStanding(hasMembership, false, (decimal)rate, "Gold"),
                Array.Empty<ScheduledDiscount>(), Thu, Cashier));
        }

        // ── recomputing, which is where this silently breaks (ported) ────────────────────────

        /// <summary>
        /// ⚠⚠ THE RE-APPLY TRAP. The basket changes on every scan, so the automatic alterations are
        /// rebuilt constantly. If the rebuild counted its OWN previous alteration as "this line is
        /// already discounted", every line would be ineligible from the second scan onwards and the
        /// discount would silently collapse to nothing — a member charged full price with a discount
        /// line still on screen.
        /// </summary>
        [Fact]
        public void Recomputing_ignores_its_own_previous_alterations()
        {
            var item = Item("A", 10m, 10m);
            var existing = OneMember(new List<IBasketRecord> { item });
            var basket = new List<IBasketRecord> { item, existing };

            var rebuilt = AutoDiscountBasket.Build(
                basket, Gold, Array.Empty<ScheduledDiscount>(), Thu, Cashier,
                replacing: new[] { existing });

            Assert.Equal(-1m, Assert.Single(rebuilt).Price);
        }

        /// <summary>
        /// ⚠⚠ FOUND BY THE `Automatic` FLAG, NOT BY DISCOUNT ID — and that is the change from the
        /// member-only design. A scheduled rule's `Discount.Id` is a REAL catalogue id, so it is
        /// indistinguishable from the same discount applied BY HAND off the Alterations list. Matching
        /// on id would make the rebuild overwrite the operator's own choice.
        /// </summary>
        [Fact]
        public void Automatic_alterations_are_identified_by_their_flag_not_by_their_id()
        {
            var item = Item("A", 10m, 10m, cat: Warhammer);
            var manual = ManualDiscount(-2m, item);           // a hand-applied catalogue discount, id 7
            var basket = new List<IBasketRecord> { item, manual };

            // A rule whose id COLLIDES with the manual discount's.
            var built = AutoDiscountBasket.Build(
                basket, MemberStanding.None, new[] { Rule(id: 7) }, Wed, Cashier);

            // Nothing to build: the only line is already discounted by hand.
            Assert.Empty(built);

            // And the manual one is not mistaken for ours.
            Assert.Empty(AutoDiscountBasket.ExistingOn(basket));
        }

        // ── the audit trail, default 22(c) (ported) ──────────────────────────────────────────

        /// <summary>
        /// ⚠ "All discounts need to be tracked" includes the automatic ones — this is the discount most
        /// likely to be queried later. ⚠ With NO authoriser: the tier or the rule authorised it, and
        /// both are portal-configured. Naming the operator would manufacture a self-approval.
        /// </summary>
        [Fact]
        public void An_automatic_discount_carries_its_own_audit_record_with_no_authoriser()
        {
            var alteration = OneMember(new List<IBasketRecord> { Item("A", 10m, 10m) });

            Assert.Equal("Gold 10%", alteration.DiscountReason);
            Assert.Equal(Cashier, alteration.RequestedByUserId);
            Assert.Null(alteration.AuthorisedByUserId);
            Assert.Null(alteration.AuthorisedByName);
            Assert.True(alteration.Automatic);
        }

        // ── scheduled rules — what is NEW here ───────────────────────────────────────────────

        [Fact]
        public void A_wednesday_rule_discounts_its_category_on_a_wednesday_only()
        {
            var basket = new List<IBasketRecord> { Item("SM", 10m, 10m, cat: Warhammer) };

            var onWed = AutoDiscountBasket.Build(basket, MemberStanding.None, new[] { Rule() }, Wed, Cashier);
            Assert.Equal(-1m, Assert.Single(onWed).Price);
            Assert.Equal("Wednesday Warhammer", Assert.Single(onWed).Note.Note);

            Assert.Empty(AutoDiscountBasket.Build(basket, MemberStanding.None, new[] { Rule() }, Thu, Cashier));
        }

        [Fact]
        public void A_rule_leaves_lines_outside_its_category_alone()
        {
            var basket = new List<IBasketRecord>
            {
                Item("SM", 10m, 10m, cat: Warhammer),
                Item("BRUSH", 20m, 20m, cat: Paint),
            };

            var alteration = Assert.Single(
                AutoDiscountBasket.Build(basket, MemberStanding.None, new[] { Rule() }, Wed, Cashier));

            Assert.Equal(-1m, alteration.Price);                                  // 10% of £10 only
            Assert.Same(basket[0], Assert.Single(alteration.ItemsAssocitated));
        }

        /// <summary>
        /// ⚠⚠ THE REASON THIS CLASS BUILDS A LIST RATHER THAN ONE ALTERATION. Two rules on two
        /// categories means two different discounts on one basket, and a single alteration cannot say
        /// "10% off these, 5% off those".
        /// </summary>
        [Fact]
        public void Two_rules_on_two_categories_produce_two_alterations()
        {
            var basket = new List<IBasketRecord>
            {
                Item("SM", 10m, 10m, cat: Warhammer),
                Item("BRUSH", 20m, 20m, cat: Paint),
            };

            var built = AutoDiscountBasket.Build(
                basket, MemberStanding.None,
                new[] { Rule(0.10m, id: 41, cat: Warhammer), Rule(0.05m, id: 42, cat: Paint) },
                Wed, Cashier);

            Assert.Equal(2, built.Count);
            Assert.Equal(-1m, built.Single(a => a.Discount.Id == 41).Price);   // 10% of £10
            Assert.Equal(-1m, built.Single(a => a.Discount.Id == 42).Price);   // 5% of £20
        }

        /// <summary>
        /// ⚠⚠ DECISION D2, THROUGH THE BASKET. A Gold member (10%) buying Warhammer on a Wednesday when
        /// the rule is 20%: the line takes 20%, ONCE — one alteration, not two, and never 30%.
        /// </summary>
        [Fact]
        public void When_a_rule_beats_the_members_tier_the_line_takes_the_rule_and_only_the_rule()
        {
            var basket = new List<IBasketRecord> { Item("SM", 10m, 10m, cat: Warhammer) };

            var built = AutoDiscountBasket.Build(basket, Gold, new[] { Rule(0.20m) }, Wed, Cashier);

            var alteration = Assert.Single(built);
            Assert.Equal(-2m, alteration.Price);        // 20%, not 30%
            Assert.Equal(41, alteration.Discount.Id);   // the rule's real id
        }

        /// <summary>⚠ And the other way: the tier wins when it is worth more, keeping its SENTINEL id
        /// so it stays off the legacy bridge's `Transaction_Discount` projection.</summary>
        [Fact]
        public void When_the_members_tier_beats_the_rule_the_line_keeps_the_sentinel_id()
        {
            var basket = new List<IBasketRecord> { Item("SM", 10m, 10m, cat: Warhammer) };

            var alteration = Assert.Single(
                AutoDiscountBasket.Build(basket, Gold, new[] { Rule(0.05m) }, Wed, Cashier));

            Assert.Equal(-1m, alteration.Price);
            Assert.Equal(MemberDiscount.SentinelDiscountId, alteration.Discount.Id);
        }

        /// <summary>
        /// ⚠ A basket where the tier wins on one line and a rule wins on another — two alterations,
        /// each naming only its own lines. This is the case a single-alteration design gets wrong
        /// silently.
        /// </summary>
        [Fact]
        public void The_tier_and_a_rule_can_each_win_on_different_lines_of_one_basket()
        {
            var warhammer = Item("SM", 10m, 10m, cat: Warhammer);
            var paint = Item("BRUSH", 10m, 10m, cat: Paint);
            var basket = new List<IBasketRecord> { warhammer, paint };

            var built = AutoDiscountBasket.Build(basket, Gold, new[] { Rule(0.20m) }, Wed, Cashier);

            Assert.Equal(2, built.Count);

            var byRule = built.Single(a => a.Discount.Id == 41);
            Assert.Equal(-2m, byRule.Price);
            Assert.Same(warhammer, Assert.Single(byRule.ItemsAssocitated));

            var byTier = built.Single(a => a.Discount.Id == MemberDiscount.SentinelDiscountId);
            Assert.Equal(-1m, byTier.Price);
            Assert.Same(paint, Assert.Single(byTier.ItemsAssocitated));
        }

        /// <summary>⚠ A line whose automatic discount the operator TOOK OFF stays off — otherwise the
        /// rebuild puts it straight back and full price is unreachable.</summary>
        [Fact]
        public void A_waived_line_takes_no_automatic_discount()
        {
            var item = Item("SM", 10m, 10m, cat: Warhammer);
            var basket = new List<IBasketRecord> { item };

            Assert.Empty(AutoDiscountBasket.Build(
                basket, Gold, new[] { Rule() }, Wed, Cashier, replacing: null, waived: new[] { item }));
        }

        /// <summary>⚠ A rule that is not `AutoApply` is a catalogue entry an operator picks by hand and
        /// must never apply itself — the whole difference between a rule and a list item.</summary>
        [Fact]
        public void A_rule_that_is_not_autoApply_never_applies_itself()
        {
            var basket = new List<IBasketRecord> { Item("SM", 10m, 10m, cat: Warhammer) };
            var manualOnly = Rule() with { AutoApply = false };

            Assert.Empty(AutoDiscountBasket.Build(basket, MemberStanding.None, new[] { manualOnly }, Wed, Cashier));
        }

        /// <summary>⚠⚠ A line with NO category cannot be matched by a category rule — and on this till
        /// that is the difference between working and silently never firing, because the legacy
        /// `ItemModel` has no room for the v2 category. See `BasketItem.CategoryId`.</summary>
        [Fact]
        public void A_line_with_no_category_is_not_matched_by_a_category_rule()
        {
            var basket = new List<IBasketRecord> { Item("SM", 10m, 10m, cat: null) };

            Assert.Empty(AutoDiscountBasket.Build(basket, MemberStanding.None, new[] { Rule() }, Wed, Cashier));
        }

        // ── end to end through the commit path (ported) ──────────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE POINT OF THE ASSOCIATION, PROVEN THROUGH THE REAL COMMIT PATH rather than asserted on
        /// the alteration. `CheckoutCommit.ApplyAlterations` decides which lines the money lands on,
        /// and its `TargetsOf` does NOT know about gift cards or already-discounted lines. This test
        /// fails if anyone "simplifies" the association away.
        /// </summary>
        [Fact]
        public void At_commit_the_automatic_money_lands_only_on_the_lines_it_named()
        {
            var discounted = Item("A", 10m, 10m);
            var plain = Item("B", 20m, 20m);

            var basket = new List<IBasketRecord> { discounted, plain, ManualDiscount(-5m, discounted) };
            basket.AddRange(Member(basket));

            var lines = CheckoutCommit.LinesFrom(basket);

            Assert.Equal(500, lines.Single(l => l.IdOne == "A").DiscountPence);   // the manual £5 only
            Assert.Equal(200, lines.Single(l => l.IdOne == "B").DiscountPence);   // 10% of £20
        }

        /// <summary>⚠ A scheduled discount reaches the sale the same way, and its REASON travels with
        /// it — so "why is this basket 10% under?" is answerable from the record.</summary>
        [Fact]
        public void At_commit_a_scheduled_discount_reaches_the_line_with_its_reason()
        {
            var basket = new List<IBasketRecord> { Item("SM", 10m, 10m, cat: Warhammer) };
            basket.AddRange(AutoDiscountBasket.Build(
                basket, MemberStanding.None, new[] { Rule() }, Wed, Cashier));

            var line = Assert.Single(CheckoutCommit.LinesFrom(basket));

            Assert.Equal(100, line.DiscountPence);
            var authority = Assert.Single(line.DiscountAuthorities);
            Assert.Equal("Wednesday Warhammer", authority.Reason);
            Assert.Equal(100, authority.AmountPence);
            Assert.Null(authority.AuthorisedByUserId);
        }
    }
}
