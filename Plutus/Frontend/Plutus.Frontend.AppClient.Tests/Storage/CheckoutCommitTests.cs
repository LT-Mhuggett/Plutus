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
    /// Cutover step 11 — turning the till's basket into sale lines and tenders.
    ///
    /// ⚠ THE POINT OF PUTTING THIS IN A SERVICE. `TillViewModel` is 1,200 lines with no test
    /// coverage whatsoever, and this is the money path. Everything that can be decided without a
    /// device lives here so it can be tested here; the viewmodel is left holding only the UI.
    /// </summary>
    public class CheckoutCommitTests
    {
        private static BasketItem Item(string idOne, decimal price, decimal exPrice, int qty = 1) =>
            new(new ItemModel
            {
                Id = idOne,
                Name = "Item " + idOne,
                Price = price,
                ExPrice = exPrice,
                Vat = new TaxModel { Name = "Standard" },
            }, qty);

        /// <summary>
        /// A discount as the till really builds it: a separate record holding a NEGATIVE price,
        /// with the items it applies to hanging off it. ⚠ `ExecuteAlterTransaction` never writes
        /// the discount back onto `BasketItem.Price` — that mistaken belief is what step 11 shipped.
        /// </summary>
        private static BasketAlteration Alteration(decimal price, BasketItem applsTo) =>
            applsTo is null
                ? new BasketAlteration(new NoteModel { Note = "Discount" }, new DiscountModel(),
                    Array.Empty<BasketItem>(), price, price)
                : new BasketAlteration(new NoteModel { Note = "Discount" }, new DiscountModel(),
                    applsTo, price, price);

        [Fact]
        public void Every_basket_item_becomes_a_line_carrying_its_barcode()
        {
            var lines = CheckoutCommit.LinesFrom(new IBasketRecord[]
            {
                Item("5010001", 14.99m, 12.49m),
                Item("5010002", 2.50m, 2.50m, qty: 3),
            });

            Assert.Equal(2, lines.Count);
            Assert.Equal("5010001", lines[0].IdOne);
            Assert.Equal(3, lines[1].Quantity);
        }

        /// <summary>
        /// ⚠ LOSSLESS ONLY BECAUSE OF STEP 10. These prices originated as PENCE from
        /// `EffectivePricePairAsync` and were divided by 100 purely to satisfy the legacy decimal
        /// model; multiplying back recovers exactly what was resolved. £14.99 → 1499, not 1498.
        /// </summary>
        [Fact]
        public void Decimal_prices_convert_back_to_the_pence_they_came_from()
        {
            var line = Assert.Single(CheckoutCommit.LinesFrom(new IBasketRecord[] { Item("A", 14.99m, 12.49m) }));

            Assert.Equal(1499, line.UnitIncPence);
            Assert.Equal(1249, line.UnitExPence);
        }

        /// <summary>
        /// ⚠ AND IT ROUNDS AWAY FROM ZERO, NOT TRUNCATES — which the test above does NOT prove.
        ///
        /// Found by mutation: replacing `Pence.FromDecimal` with `(long)(price * 100)` left every
        /// assertion above passing, because a 2dp decimal that came from pence scales exactly and
        /// the two are indistinguishable. They diverge the moment a third decimal place appears —
        /// which an apportioned discount or a legacy price can produce — and truncation loses a
        /// penny per line in the shop's favour, quietly, on the VAT return as well as the receipt.
        ///
        /// £0.125 → 13p away from zero, 12p truncated. Banker's rounding would also give 12p, which
        /// is why `Money.FromDecimal` states its mode rather than taking .NET's default.
        /// </summary>
        [Theory]
        [InlineData(0.125, 13)]
        [InlineData(0.135, 14)]
        [InlineData(0.005, 1)]
        public void A_fractional_penny_rounds_away_from_zero_rather_than_being_truncated(double price, long expected)
        {
            var line = Assert.Single(CheckoutCommit.LinesFrom(
                new IBasketRecord[] { Item("A", (decimal)price, (decimal)price) }));

            Assert.Equal(expected, line.UnitIncPence);
        }

        /// <summary>
        /// ⚠ The item id is left EMPTY on purpose. `SaleAssembler` derives it from the business id
        /// and the barcode and refuses a mismatched one, so exactly one place knows that rule —
        /// deriving it here as well would be a second implementation of the thing that decides
        /// which item a sale attaches to.
        /// </summary>
        [Fact]
        public void The_item_id_is_left_for_the_assembler_to_derive()
        {
            var line = Assert.Single(CheckoutCommit.LinesFrom(new IBasketRecord[] { Item("A", 1m, 1m) }));
            Assert.Equal(Guid.Empty, line.ItemId);
        }

        /// <summary>
        /// A note carrying no money is not a sale line.
        ///
        /// ⚠ THIS TEST'S COMMENT USED TO CLAIM that an alteration's money "is already reflected in
        /// the adjusted price of the line it applies to". That was FALSE, and it vouched for a
        /// defect: `ExecuteAlterTransaction` appends a SEPARATE `BasketAlteration` record and never
        /// touches `BasketItem.Price`. See `A_discounted_basket_reconciles_with_what_the_customer_pays`.
        /// </summary>
        [Fact]
        public void A_note_with_no_money_on_it_is_not_a_sale_line()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 10m, 8.33m),
                new BasketNote(new NoteModel { Note = "gift wrap" }),
            };

            var line = Assert.Single(CheckoutCommit.LinesFrom(basket));
            Assert.Equal("A", line.IdOne);
        }

        // ── discounts: the step 11 defect ──

        /// <summary>
        /// ⚠ THE P0 THIS FIXES. A discount is a separate `BasketAlteration` record holding a
        /// NEGATIVE price; `ExecuteAlterTransaction` never writes it back onto the item. The till's
        /// own total (`sale.Total`) sums EVERY basket record, so the tenders settled against the
        /// DISCOUNTED figure — while `LinesFrom` dropped the alteration entirely and assembled
        /// `GrossPence` from the UNDISCOUNTED lines.
        ///
        /// The server's invariant is `Σ tender − Σ change == GrossPence`. It would have failed on
        /// every discounted sale, answering `202 Quarantined` — which `OutboxPusher` treats as
        /// TERMINAL and never retries. The sale would have looked successful at the counter and
        /// been destroyed hours later.
        /// </summary>
        [Fact]
        public void A_discounted_basket_reconciles_with_what_the_customer_pays()
        {
            var item = Item("A", 10m, 10m, qty: 2);      // £20 of goods
            var basket = new List<IBasketRecord>
            {
                item,
                Alteration(-5m, item),                    // £5 off
            };

            var lines = CheckoutCommit.LinesFrom(basket);
            var line = Assert.Single(lines);

            Assert.Equal(500, line.DiscountPence);

            // The line's gross is what the customer actually pays, and it equals the till's own
            // basket total — which is the figure the tenders settle against.
            Assert.Equal(1500, line.UnitIncPence * line.Quantity - line.DiscountPence);
            Assert.Equal(1500, CheckoutCommit.BasketMoneyPence(basket));
        }

        /// <summary>⚠ A whole-basket discount splits across the lines with NOTHING LOST. Three
        /// lines sharing £10 by proportion is 333.33p each; the missing penny fails the server's
        /// reconcile invariant and quarantines the sale.</summary>
        [Fact]
        public void A_basket_wide_discount_is_apportioned_without_losing_a_penny()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 10m, 10m), Item("B", 10m, 10m), Item("C", 10m, 10m),
                Alteration(-10m, null),                   // £10 off the basket, no association
            };

            var lines = CheckoutCommit.LinesFrom(basket);

            Assert.Equal(1000, lines.Sum(l => l.DiscountPence));
            Assert.Equal(2000, lines.Sum(l => l.UnitIncPence * l.Quantity - l.DiscountPence));
            Assert.Equal(2000, CheckoutCommit.BasketMoneyPence(basket));
        }

        /// <summary>
        /// ⚠⚠ **THIS IS NOW THE BACKSTOP, NOT THE DEFECT** — updated 2026-08-13, later the same day.
        /// The defect (a basket that could not be completed) was closed at the **entry** gate:
        /// `TillViewModel.ExecuteAlterTransaction` now runs `BasketDiscounts.Authorise` before any
        /// alteration reaches the basket, so an operator is refused while they can still act, with a
        /// message naming the headroom. **This throw stays**, deliberately, as the second gate — a
        /// basket assembled some other way (a recalled parked basket, a future caller) must still
        /// never produce a negative-gross sale. Binding default 12's shape: enforce at BOTH gates.
        ///
        /// ⚠ So it no longer "must fail when fixed". What it pins is that the backstop is still
        /// armed. If a change ever makes this NOT throw, that is the regression.
        ///
        /// Discounts totalling more than the basket make `ApplyAlterations` throw, because it
        /// computes each alteration's `grosses` NET OF THE DISCOUNTS ALREADY APPLIED and
        /// `DiscountApportionment.Across` refuses a discount larger than the lines it lands on
        /// (rightly — the alternative is a negative-gross "sale").
        ///
        /// ⚠ WHY IT MATTERS AT A COUNTER: `CommitAsync` catches this and reports *"The sale couldn't
        /// be recorded on this till. Nothing has been taken — try again."* No money moves, which is
        /// the one thing it gets right. But **trying again does exactly the same thing** — the basket
        /// is permanently un-completable, and nothing tells the operator that a discount is the
        /// cause or which one to remove.
        ///
        /// ⚠ IT IS REACHABLE: nothing caps a discount at the basket's value. `TillViewModel` builds
        /// each `BasketAlteration` straight from the entered amount (~line 1022–1043) with no check
        /// against `SaleIncTax`, and `Alterations` is a collection, so two are allowed.
        ///
        /// ⚠ THE FIX IS NOT "cap it silently" — a £5 discount quietly becoming £3 is the silent
        /// money change this codebase exists to avoid. It belongs at the point of APPLYING the
        /// discount, where the operator can still act, and the web till's behaviour decides the
        /// shape (binding default 10).
        /// </summary>
        [Fact]
        public void BACKSTOP_discounts_exceeding_the_basket_still_throw_at_commit()
        {
            var basket = new List<IBasketRecord>
            {
                Item("A", 8m, 8m),          // £8 of goods
                Alteration(-5m, null),      // £5 off …
                Alteration(-5m, null),      // … and £5 off again = £10 off an £8 basket
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => CheckoutCommit.LinesFrom(basket));

            // The message names the real numbers, which is what makes the log actionable even though
            // the operator never sees it.
            Assert.Contains("500p", ex.Message);
            Assert.Contains("300p", ex.Message);   // £8 less the first £5 — the remaining gross
        }

        /// <summary>A single discount equal to the whole basket is FINE — the boundary is inclusive,
        /// so "everything free" works and only *more* than everything is refused. Worth pinning
        /// separately: an off-by-one here would refuse a legitimate 100% staff discount.</summary>
        [Fact]
        public void A_discount_equal_to_the_whole_basket_is_allowed()
        {
            var basket = new List<IBasketRecord> { Item("A", 8m, 8m), Alteration(-8m, null) };

            var lines = CheckoutCommit.LinesFrom(basket);

            Assert.Equal(800, lines.Single().DiscountPence);
            Assert.Equal(0, lines.Sum(l => l.UnitIncPence * l.Quantity - l.DiscountPence));
            Assert.Equal(0, CheckoutCommit.BasketMoneyPence(basket));
        }

        /// <summary>A discount attached to one item does not come off another.</summary>
        [Fact]
        public void A_discount_lands_only_on_the_item_it_was_applied_to()
        {
            var cheap = Item("A", 10m, 10m);
            var dear = Item("B", 50m, 50m);
            var basket = new List<IBasketRecord> { cheap, dear, Alteration(-2m, dear) };

            var lines = CheckoutCommit.LinesFrom(basket);

            Assert.Equal(0, lines.Single(l => l.IdOne == "A").DiscountPence);
            Assert.Equal(200, lines.Single(l => l.IdOne == "B").DiscountPence);
        }

        // ── the card surcharge ──

        /// <summary>
        /// ⚠ THE LINE THE LEGACY BASKETNOTE COULD NEVER BE. A surcharged basket must RECONCILE —
        /// the fee is a real line, so `GrossPence` carries it and the tenders settle against the
        /// same figure. And the fee's VAT FOLLOWS THE BASKET (Bookit/NEC): on 20% goods the fee
        /// carries 20%, never a hardcoded rate and never zero.
        /// </summary>
        [Fact]
        public void A_surcharged_basket_reconciles_and_the_fee_follows_the_goods()
        {
            var basket = new List<IBasketRecord> { Item("A", 12m, 10m) };   // £12/£10 — 20% goods

            // 1.69% + 20p on £12.00 = 20.28 → 20p percent half + 20p flat = 40p fee.
            var fee = CheckoutCommit.SurchargeItem(basket, surchargeBp: 169, surchargeFlatPence: 20);

            Assert.NotNull(fee);
            Assert.Equal(0.40m, fee.Price);
            Assert.Equal(0.33m, fee.PriceExTax);   // 40 × 1000/1200 = 33.3 → 33p: the basket's own mix

            basket.Add(fee);
            var lines = CheckoutCommit.LinesFrom(basket);

            Assert.Equal(2, lines.Count);
            Assert.Equal(1240, lines.Sum(l => l.UnitIncPence * l.Quantity - l.DiscountPence));
            Assert.Equal(1240, CheckoutCommit.BasketMoneyPence(basket));   // guard passes
        }

        /// <summary>⚠ THE ONE A HARDCODED RATE GETS WRONG: a fee on zero-rated goods carries NO
        /// VAT — the fee follows the goods, and 20% here is output tax HMRC says is not due.</summary>
        [Fact]
        public void A_fee_on_zero_rated_goods_carries_no_VAT()
        {
            var fee = CheckoutCommit.SurchargeItem(
                new List<IBasketRecord> { Item("BOOK", 10m, 10m) }, 0, 50);

            Assert.Equal(0.50m, fee.Price);
            Assert.Equal(0.50m, fee.PriceExTax);   // ex == inc → VAT 0
        }

        /// <summary>⚠ A refund attracts no fee — surcharging money you are giving BACK is
        /// indefensible at the counter and the shared rule refuses the arithmetic anyway.</summary>
        [Fact]
        public void A_refund_only_basket_attracts_no_fee()
        {
            var basket = new List<IBasketRecord> { new BasketReturnItem(new ItemModel
            {
                Id = "A", Name = "A", Price = 10m, ExPrice = 10m, Vat = new TaxModel { Name = "" },
            }, 1) };

            Assert.Null(CheckoutCommit.SurchargeItem(basket, 169, 20));
        }

        [Fact]
        public void No_setting_no_fee()
        {
            Assert.Null(CheckoutCommit.SurchargeItem(
                new List<IBasketRecord> { Item("A", 10m, 10m) }, 0, 0));
        }

        /// <summary>Applied ONCE — a split payment across two cards must not charge the flat fee
        /// twice, and `HasSurcharge` is what the till checks before adding.</summary>
        [Fact]
        public void The_fee_is_detectable_so_it_is_only_added_once()
        {
            var basket = new List<IBasketRecord> { Item("A", 12m, 10m) };
            Assert.False(CheckoutCommit.HasSurcharge(basket));

            basket.Add(CheckoutCommit.SurchargeItem(basket, 169, 20));
            Assert.True(CheckoutCommit.HasSurcharge(basket));
        }

        /// <summary>
        /// ⚠ `BasketMoneyPence` must agree with the till's `sale.Total`, which is
        /// `Σ Price × Quantity`, returns negated — including the alteration's negative price.
        /// If these two ever disagree the reconciliation guard fires on honest baskets and the
        /// till refuses sales it should take.
        /// </summary>
        [Fact]
        public void The_basket_total_matches_the_sum_the_till_settles_against()
        {
            var item = Item("A", 10m, 10m, qty: 2);
            var basket = new List<IBasketRecord> { item, Alteration(-5m, item) };

            var asTheTillSumsIt = basket.Sum(r => r.Price * (r is BasketReturnItem ? -1 : 1) * r.Quantity);

            Assert.Equal(15m, asTheTillSumsIt);
            Assert.Equal(1500, CheckoutCommit.BasketMoneyPence(basket));
        }

        [Fact]
        public void An_empty_basket_produces_no_lines()
        {
            Assert.Empty(CheckoutCommit.LinesFrom(Array.Empty<IBasketRecord>()));
            Assert.Empty(CheckoutCommit.LinesFrom(null));
        }

        // ── tenders ──

        /// <summary>
        /// ⚠ THE ORDERING TRAP, reached through the till's own path. "Gift card" contains neither
        /// "cash" nor "online", so a naive chain files it as STORE CREDIT — a different liability
        /// with a different reconciliation, and the payment-split report groups by this byte.
        /// </summary>
        [Fact]
        public void Payment_method_names_map_to_the_shared_tender_bytes()
        {
            var tenders = CheckoutCommit.TendersFrom(new (string?, decimal, decimal)[]
            {
                ("Cash", 10.00m, 0.50m),
                ("Gift card", 5.00m, 0m),
                ("Store credit", 2.00m, 0m),
                ("Visa Debit", 1.00m, 0m),
            });

            Assert.Equal(Tenders.Cash, tenders[0].TenderType);
            Assert.Equal(Tenders.GiftCard, tenders[1].TenderType);
            Assert.Equal(Tenders.Credit, tenders[2].TenderType);
            Assert.Equal(Tenders.Card, tenders[3].TenderType);
        }

        [Fact]
        public void Tender_amounts_and_change_convert_to_pence()
        {
            var tender = Assert.Single(CheckoutCommit.TendersFrom(
                new (string?, decimal, decimal)[] { ("Cash", 20.00m, 5.01m) }));

            Assert.Equal(2000, tender.AmountPence);
            Assert.Equal(501, tender.ChangePence);
        }

        /// <summary>
        /// ⚠ NO TENDERS IS NOT "CASH". A basket that reached checkout with no payment row is a bug
        /// upstream, and defaulting it to cash here would record money as taken that nobody
        /// counted. The assembler refuses the sale instead.
        /// </summary>
        [Fact]
        public void No_payment_rows_produces_no_tenders_rather_than_assuming_cash()
        {
            Assert.Empty(CheckoutCommit.TendersFrom(Array.Empty<(string?, decimal, decimal)>()));
            Assert.Empty(CheckoutCommit.TendersFrom(null));
        }

        // ── the discount audit trail (binding default 22c) ──

        /// <summary>An alteration as the till builds it once a reason has been given.</summary>
        private static BasketAlteration Attributed(
            decimal price, BasketItem appliesTo, string reason,
            Guid? authorisedBy = null, string authorisedByName = null)
        {
            var a = Alteration(price, appliesTo);
            a.DiscountReason = reason;
            a.RequestedByUserId = Cashier;
            a.AuthorisedByUserId = authorisedBy;
            a.AuthorisedByName = authorisedByName;
            return a;
        }

        private static readonly Guid Cashier = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid Supervisor = Guid.Parse("22222222-2222-2222-2222-222222222222");

        [Fact]
        public void A_discounts_reason_and_authoriser_travel_with_the_line_it_lands_on()
        {
            var item = Item("A", 10m, 10m);
            var lines = CheckoutCommit.LinesFrom(new List<IBasketRecord>
            {
                item,
                Attributed(-5m, item, "damaged box", Supervisor, "Sam Supervisor"),
            });

            var authority = Assert.Single(Assert.Single(lines).DiscountAuthorities);

            Assert.Equal("damaged box", authority.Reason);
            Assert.Equal(500, authority.AmountPence);
            Assert.Equal(Cashier, authority.RequestedByUserId);
            Assert.Equal(Supervisor, authority.AuthorisedByUserId);
        }

        /// <summary>
        /// ⚠⚠ THE ATTRIBUTION FOLLOWS THE MONEY, SHARE BY SHARE — and the shares are the SAME ones
        /// the lines' `DiscountPence` took, never a second apportionment. A second one would drift
        /// from the first only on baskets that do not divide evenly, which is exactly when nobody is
        /// checking: the audit trail would say a line took 334p while the line itself says 333p.
        ///
        /// £10 across three £10 lines is 334/333/333 by largest-remainder, and the authorities must
        /// sum back to the £10 an auditor is asking about.
        /// </summary>
        [Fact]
        public void A_basket_wide_discount_splits_its_attribution_by_the_same_shares_as_the_money()
        {
            var a = Item("A", 10m, 10m);
            var b = Item("B", 10m, 10m);
            var c = Item("C", 10m, 10m);

            var lines = CheckoutCommit.LinesFrom(new List<IBasketRecord>
            {
                a, b, c,
                Attributed(-10m, null, "manager discount", Supervisor, "Sam Supervisor"),
            });

            var shares = lines.Select(l => Assert.Single(l.DiscountAuthorities).AmountPence).ToList();

            Assert.Equal(1000, shares.Sum());
            Assert.Equal(lines.Select(l => l.DiscountPence), shares);
            Assert.All(lines, l => Assert.Equal("manager discount", l.DiscountAuthorities[0].Reason));
        }

        /// <summary>
        /// ⚠ TWO DISCOUNTS ON ONE LINE ARE BOTH RECORDED. `DiscountPence` is their sum, so it cannot
        /// say which half a supervisor approved; appending rather than replacing is what keeps the
        /// answer available.
        /// </summary>
        [Fact]
        public void A_second_discount_on_the_same_line_is_appended_rather_than_replacing_the_first()
        {
            var item = Item("A", 20m, 20m);
            var lines = CheckoutCommit.LinesFrom(new List<IBasketRecord>
            {
                item,
                Attributed(-2m, item, "Gold member 10%"),
                Attributed(-3m, item, "damaged box", Supervisor, "Sam Supervisor"),
            });

            var authorities = Assert.Single(lines).DiscountAuthorities;

            Assert.Equal(2, authorities.Count);
            Assert.Equal(500, authorities.Sum(x => x.AmountPence));
            Assert.Null(authorities[0].AuthorisedByUserId);
            Assert.Equal(Supervisor, authorities[1].AuthorisedByUserId);
        }

        /// <summary>
        /// ⚠⚠ A BASKET PARKED BEFORE 2026-08-14 AND RECALLED AFTERWARDS HAS NO REASON ON ITS
        /// ALTERATIONS, and this is the case where inventing one would be worst.
        ///
        /// ⚠ The money still goes through — the refusal belongs where the discount is APPLIED, while
        /// the operator can still act on it. By checkout the basket is assembled and a customer is
        /// waiting; dropping the sale over a missing string would cost far more than it is worth.
        /// What must NOT happen is an authority with a blank reason, which would masquerade as a
        /// complete record.
        /// </summary>
        [Fact]
        public void A_discount_recalled_from_before_this_field_existed_sends_no_authority_rather_than_a_blank_one()
        {
            var item = Item("A", 10m, 10m);
            var lines = CheckoutCommit.LinesFrom(new List<IBasketRecord>
            {
                item,
                Alteration(-5m, item),      // no reason — an old parked basket
            });

            var line = Assert.Single(lines);

            Assert.Equal(500, line.DiscountPence);            // the money is still right
            Assert.True(line.DiscountAuthorities is null || line.DiscountAuthorities.Count == 0);
        }

        /// <summary>⚠ Whitespace is not a reason. `"   "` looks present to a query and is blank to a
        /// human — the same trap `DiscountAudit.NormaliseReason` exists for, re-checked here because
        /// this is the path a recalled basket takes.</summary>
        [Fact]
        public void A_reason_of_only_whitespace_is_not_recorded_as_an_authority()
        {
            var item = Item("A", 10m, 10m);
            var lines = CheckoutCommit.LinesFrom(new List<IBasketRecord>
            {
                item,
                Attributed(-5m, item, "   "),
            });

            var line = Assert.Single(lines);
            Assert.True(line.DiscountAuthorities is null || line.DiscountAuthorities.Count == 0);
        }

        /// <summary>
        /// ⚠ A DISCOUNT ON A RETURNS-ONLY BASKET LANDS NOWHERE, so it records nothing either. The
        /// attribution must not appear on a line that took no money off — `TargetsOf` excludes
        /// returns because `VatLineMath.ForLine` drops a discount on one by design.
        /// </summary>
        [Fact]
        public void A_discount_that_lands_on_no_line_records_no_attribution()
        {
            var returned = new BasketReturnItem(new ItemModel
            {
                Id = "R", Name = "Returned", Price = 10m, ExPrice = 10m,
                Vat = new TaxModel { Name = "Standard" },
            }, 1);

            var lines = CheckoutCommit.LinesFrom(new List<IBasketRecord>
            {
                returned,
                Attributed(-5m, null, "should not stick"),
            });

            Assert.All(lines, l => Assert.True(l.DiscountAuthorities is null || l.DiscountAuthorities.Count == 0));
        }
    }
}
