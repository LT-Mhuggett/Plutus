using System;
using System.Collections.Generic;
using Database.Models;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.Security;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Security
{
    /// <summary>
    /// Cutover step 12 — the till's permission gate.
    ///
    /// ⚠ EVERY TEST HERE IS ABOUT A GATE FALLING OPEN, because that is the only failure mode that
    /// matters: a gate that wrongly refuses is a complaint, a gate that wrongly permits is money
    /// leaving the drawer with nobody's name on it.
    /// </summary>
    public class TillGateTests
    {
        private static SignedInOperator Operator(params PermissionGrant[] grants) =>
            new(Guid.NewGuid(), "Sam", grants, OfflineTrust.Full, TimeSpan.Zero, false, "");

        private static PermissionGrant Grant(string code, long? maxPence = null) =>
            new(code, maxPence, null, null, null, null, null);

        private static BasketItem Item(string idOne, decimal price, int qty = 1) =>
            new(new ItemModel { Id = idOne, Name = idOne, Price = price, ExPrice = price, Vat = new TaxModel { Name = "" } }, qty);

        private static BasketReturnItem Return(string idOne, decimal price, int qty = 1) =>
            new(new ItemModel { Id = idOne, Name = idOne, Price = price, ExPrice = price, Vat = new TaxModel { Name = "" } },
                qty);

        // ── the null operator ──

        /// <summary>
        /// ⚠ THE ONE THAT MATTERS MOST. `SignedInOperator` is null after a legacy local login, and
        /// "we don't know who this is" must never resolve to "let them". A till that falls open
        /// when it cannot identify the operator has no ceilings at all, and the resulting refund is
        /// unattributable.
        /// </summary>
        [Theory]
        [InlineData(PermissionCatalogue.PosSell)]
        [InlineData(PermissionCatalogue.PosRefund)]
        [InlineData(PermissionCatalogue.PosPriceOverride)]
        public void A_null_operator_is_refused_every_permission(string permission)
        {
            var decision = TillGate.Check(null, permission);

            Assert.False(decision.Allowed);
            // ⚠ And NOT offered as an override: another person's password does not answer
            // "nobody is signed in". Offering one would send an operator hunting for a supervisor
            // to fix a problem a supervisor cannot fix.
            Assert.False(decision.NeedsOverride);
        }

        [Fact]
        public void A_null_operator_cannot_check_out_even_an_ordinary_basket()
        {
            var decision = TillGate.CheckCheckout(null, new IBasketRecord[] { Item("A", 5m) });
            Assert.False(decision.Allowed);
        }

        // ── ceilings ──

        [Fact]
        public void An_operator_with_the_permission_and_no_ceiling_may_proceed()
        {
            var sam = Operator(Grant(PermissionCatalogue.PosRefund));
            Assert.True(TillGate.Check(sam, PermissionCatalogue.PosRefund, 999_99).Allowed);
        }

        /// <summary>⚠ The boundary itself is allowed — a £100 ceiling authorises £100, not £99.99.
        /// Off-by-one here is a refund refused at the counter for no stateable reason.</summary>
        [Theory]
        [InlineData(9_99, true)]
        [InlineData(10_00, true)]
        [InlineData(10_01, false)]
        public void A_ceiling_allows_up_to_and_including_itself(long amountPence, bool allowed)
        {
            var sam = Operator(Grant(PermissionCatalogue.PosRefund, maxPence: 10_00));
            Assert.Equal(allowed, TillGate.Check(sam, PermissionCatalogue.PosRefund, amountPence).Allowed);
        }

        /// <summary>Refused over a ceiling is offered as an OVERRIDE — a more senior person can
        /// authorise it. Refused for lacking the permission entirely is too.</summary>
        [Fact]
        public void Being_over_a_ceiling_offers_a_supervisor_override()
        {
            var sam = Operator(Grant(PermissionCatalogue.PosRefund, maxPence: 10_00));
            var decision = TillGate.Check(sam, PermissionCatalogue.PosRefund, 50_00);

            Assert.False(decision.Allowed);
            Assert.True(decision.NeedsOverride);
        }

        [Fact]
        public void An_operator_without_the_permission_at_all_is_refused()
        {
            var cashier = Operator(Grant(PermissionCatalogue.PosSell));
            Assert.False(TillGate.Check(cashier, PermissionCatalogue.PosRefund, 1_00).Allowed);
            Assert.False(TillGate.Check(cashier, PermissionCatalogue.PosPriceOverride).Allowed);
        }

        // ── the refund amount ──

        /// <summary>
        /// ⚠ THE HOLE THIS STEP CLOSES. The legacy check summed each return line's UNIT price and
        /// ignored quantity — five £30 returns on one line tested as £30 and went straight through
        /// a £100 band that should have stopped them at £150. The ceiling was not being enforced
        /// against the money actually leaving the drawer.
        /// </summary>
        [Fact]
        public void The_refund_amount_counts_quantity()
        {
            var basket = new IBasketRecord[] { Return("A", 30m, qty: 5) };

            Assert.Equal(150_00, TillGate.RefundAmountPence(basket));

            // and the gate refuses it against a £100 ceiling, which the legacy check did not
            var supervisor = Operator(Grant(PermissionCatalogue.PosRefund, maxPence: 100_00));
            Assert.False(TillGate.CheckCheckout(supervisor, basket).Allowed);
        }

        /// <summary>⚠ Splitting a refund across lines must not get under a ceiling either — the
        /// ceiling is checked against the WHOLE refunded amount.</summary>
        [Fact]
        public void Refunds_split_across_lines_are_totalled_not_checked_one_by_one()
        {
            var basket = new IBasketRecord[]
            {
                Return("A", 40m), Return("B", 40m), Return("C", 40m),
            };

            Assert.Equal(120_00, TillGate.RefundAmountPence(basket));

            var supervisor = Operator(Grant(PermissionCatalogue.PosRefund, maxPence: 100_00));
            Assert.False(TillGate.CheckCheckout(supervisor, basket).Allowed);
        }

        /// <summary>⚠ A POSITIVE magnitude. `SaleAssembler.Total` signs returns negative, and a
        /// ceiling compared against a negative number passes everything.</summary>
        [Fact]
        public void The_refund_amount_is_positive_so_a_ceiling_can_be_compared_against_it()
        {
            Assert.True(TillGate.RefundAmountPence(new IBasketRecord[] { Return("A", 30m, 5) }) > 0);
        }

        [Fact]
        public void A_basket_with_no_returns_refunds_nothing_and_needs_only_pos_sell()
        {
            var basket = new IBasketRecord[] { Item("A", 10m, 3) };
            var cashier = Operator(Grant(PermissionCatalogue.PosSell));

            Assert.Equal(0, TillGate.RefundAmountPence(basket));
            Assert.False(TillGate.HasReturns(basket));
            Assert.True(TillGate.CheckCheckout(cashier, basket).Allowed);
        }

        /// <summary>⚠ Sale lines in the same basket must NOT inflate the refund figure — a £500
        /// purchase alongside a £10 return is a £10 refund, and counting the sale would demand a
        /// ceiling nobody needs.</summary>
        [Fact]
        public void Items_being_sold_do_not_count_towards_the_refund_amount()
        {
            var basket = new IBasketRecord[] { Item("A", 500m), Return("B", 10m) };
            Assert.Equal(10_00, TillGate.RefundAmountPence(basket));
        }

        // ── what the caller escalates ──

        /// <summary>
        /// ⚠ THE DECISION MUST NAME WHAT IT REFUSED. `CheckCheckout` tests `pos.sell` FIRST, so a
        /// caller that hardcodes "refund" when escalating asks a supervisor to authorise a £0.00
        /// refund — a basket with no returns refunds nothing — and then lets an ordinary sale
        /// through on the strength of it. Any supervisor holding `pos.refund`, including one
        /// explicitly DENIED `pos.sell`, would wave it past, and nobody at any point is asked
        /// whether this operator may sell.
        /// </summary>
        [Fact]
        public void A_sell_refusal_escalates_as_sell_not_as_a_zero_pound_refund()
        {
            // Holds refund rights but may not sell at all.
            var refundOnly = Operator(Grant(PermissionCatalogue.PosRefund));

            var decision = TillGate.CheckCheckout(refundOnly, new IBasketRecord[] { Item("A", 5m) });

            Assert.False(decision.Allowed);
            Assert.Equal(PermissionCatalogue.PosSell, decision.Permission);
            Assert.Null(decision.AmountPence);
        }

        /// <summary>A refund refusal escalates as a refund, for the amount actually being refunded
        /// — so the supervisor's OWN ceiling is applied to the real figure.</summary>
        [Fact]
        public void A_refund_refusal_escalates_as_a_refund_for_the_real_amount()
        {
            var capped = Operator(Grant(PermissionCatalogue.PosSell), Grant(PermissionCatalogue.PosRefund, 10_00));

            var decision = TillGate.CheckCheckout(capped, new IBasketRecord[] { Return("A", 30m, qty: 5) });

            Assert.False(decision.Allowed);
            Assert.Equal(PermissionCatalogue.PosRefund, decision.Permission);
            Assert.Equal(150_00, decision.AmountPence);
        }

        /// <summary>⚠ An amount-less refusal must not be described as an amount problem. Reaching
        /// the message at all with a null amount means the operator lacks the permission OUTRIGHT,
        /// so "can't authorise this amount" sends them to a supervisor for the wrong reason — and
        /// every price-override refusal used to read exactly that way.</summary>
        [Fact]
        public void Lacking_a_permission_outright_is_not_reported_as_an_amount_problem()
        {
            var cashier = Operator(Grant(PermissionCatalogue.PosSell));
            var decision = TillGate.Check(cashier, PermissionCatalogue.PosPriceOverride);

            Assert.False(decision.Allowed);
            Assert.Contains("doesn't have permission", decision.Message);
            Assert.DoesNotContain("this amount", decision.Message);
        }

        /// <summary>A cashier who may sell but not refund is stopped by a basket containing a
        /// return, even though the same basket without it would go through.</summary>
        [Fact]
        public void A_cashier_may_sell_but_a_return_in_the_basket_stops_them()
        {
            var cashier = Operator(Grant(PermissionCatalogue.PosSell));

            Assert.True(TillGate.CheckCheckout(cashier, new IBasketRecord[] { Item("A", 5m) }).Allowed);

            var withReturn = TillGate.CheckCheckout(cashier, new IBasketRecord[] { Item("A", 5m), Return("B", 5m) });
            Assert.False(withReturn.Allowed);
            Assert.True(withReturn.NeedsOverride);
        }
    }
}
