using System.Linq;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Views.CustomViews;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Sales
{
    /// <summary>
    /// WP14 — what the checkout says about cards, 2026-08-21.
    ///
    /// ⚠⚠ THE POINT OF THIS FEATURE IS THE MIDDLE CASE. A tenant who has chosen a real provider with
    /// no wired integration is STILL on the standalone flow — so a cashier who reads only
    /// "Card via Worldpay" stands waiting for a terminal prompt that is never coming, with a customer
    /// in front of them. Every provider on the platform reports `Integrated: false` today, so that is
    /// not the rare case, it is the only one a real shop will meet.
    ///
    /// ⚠ These pin the WORDING against the web till's `CheckoutDialog.tsx`, verbatim. Matt, 2026-08-19:
    /// *"I need the functionality and look and feel to be the same across both tills."* A sentence is
    /// look and feel — an operator who moves between tills mid-shift and meets two different
    /// explanations of the same machine has been retrained for nothing.
    ///
    /// ⚠ The DECISION (which of the three cases) is `Client.Core.PaymentGateway.Resolve`, covered by
    /// `PaymentGatewayTests`. These cover only the English.
    /// </summary>
    public class CardPaymentHintTests
    {
        private static string Text(CardPaymentDisplay card, bool refunding = false) =>
            string.Concat(CheckoutAlert.CardHintFor(card, refunding).Select(s => s.Text));

        private static bool AnyBold(CardPaymentDisplay card) =>
            CheckoutAlert.CardHintFor(card, false).Any(s => s.Bold);

        [Fact]
        public void No_provider_says_what_to_DO_not_what_is_unconfigured()
        {
            var hint = Text(PaymentGateway.Resolve(null));

            Assert.Equal(
                "💳 Card: take payment on the chip & pin terminal, confirm it's approved, then complete.",
                hint);

            // ⚠ It must not name the thing that failed. A cashier does not care which lookup the till
            // could not make; they care which machine to reach for.
            Assert.DoesNotContain("standalone", hint, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>⚠ Money going back is not "approved" — only the verb changes.</summary>
        [Fact]
        public void A_refund_changes_the_verb_and_nothing_else()
        {
            Assert.Equal(
                "💳 Card: refund on the chip & pin terminal, confirm it went through, then complete.",
                Text(PaymentGateway.Resolve(null), refunding: true));
        }

        /// <summary>
        /// ⚠⚠ THE LOAD-BEARING ONE. Named provider, no integration → the name AND the warning.
        /// </summary>
        [Fact]
        public void A_chosen_provider_with_no_integration_names_it_AND_says_the_integration_is_pending()
        {
            var card = PaymentGateway.Resolve(
                new Plutus.Contracts.Client.ActiveGatewayDto("worldpay", "Worldpay", Integrated: false));

            Assert.Equal(
                "💳 Card via Worldpay (integration pending — use the terminal and confirm approval as usual).",
                Text(card));
        }

        [Fact]
        public void A_wired_integration_names_the_provider_and_promises_nothing_else()
        {
            var card = PaymentGateway.Resolve(
                new Plutus.Contracts.Client.ActiveGatewayDto("worldpay", "Worldpay", Integrated: true));

            Assert.Equal("💳 Card via Worldpay.", Text(card));
        }

        /// <summary>
        /// ⚠ The provider name is bold, because the web till bolds it — and it is the only part of
        /// this line that differs between two shops.
        /// </summary>
        [Fact]
        public void The_provider_name_is_bold_and_the_standalone_sentence_has_nothing_bold()
        {
            Assert.True(AnyBold(PaymentGateway.Resolve(
                new Plutus.Contracts.Client.ActiveGatewayDto("worldpay", "Worldpay", Integrated: true))));

            Assert.False(AnyBold(PaymentGateway.Resolve(null)));
        }

        /// <summary>
        /// ⚠⚠ NOT BY COMPARING THE LABEL. A tenant is free to call their provider anything, and one
        /// who typed the standalone wording as their own label must still get the named branch —
        /// otherwise a shop with a real integration reads "take payment on the chip & pin terminal".
        /// </summary>
        [Fact]
        public void A_provider_labelled_like_the_standalone_default_still_takes_the_named_branch()
        {
            var card = PaymentGateway.Resolve(new Plutus.Contracts.Client.ActiveGatewayDto(
                "worldpay", PaymentGateway.StandaloneLabel, Integrated: true));

            Assert.Equal($"💳 Card via {PaymentGateway.StandaloneLabel}.", Text(card));
        }

        /// <summary>
        /// ⚠ Null is tolerated rather than thrown on. The caller resolves through `PaymentGateway`
        /// and cannot produce null — but a blank line on a checkout screen would be a till that says
        /// nothing about cards at all, which is worse than the default sentence.
        /// </summary>
        [Fact]
        public void Null_reads_as_standalone_rather_than_a_blank_line()
        {
            Assert.Equal(Text(PaymentGateway.Resolve(null)), Text(null));
        }
    }
}
