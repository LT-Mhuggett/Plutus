using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.Sales;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Sales
{
    /// <summary>
    /// Finding a past sale from ANY till (step 26) — the picker behind both the refund flow and the
    /// reprint.
    ///
    /// ⚠⚠ THE MERGE IS WHERE THIS GOES WRONG QUIETLY. This till's sales are ALSO in the platform's
    /// list, so a naive concatenation shows every recent sale twice and the operator has to guess
    /// which of two identical rows to press — with a customer waiting.
    /// </summary>
    public class SaleFinderTests
    {
        private static readonly DateTime Noon = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

        private static LocalSaleSummary Local(Guid id, long pence, int minutes = 0) =>
            new(id, Noon.AddMinutes(minutes), "2026-08-16", pence, 1, "A", 0);

        private static SaleListEntry Platform(Guid id, long pence, int minutes = 0, string channel = "Till") =>
            new()
            {
                Id = id,
                OccurredAtUtc = Noon.AddMinutes(minutes),
                GrossPence = pence,
                Channel = channel,
            };

        // ── the merge ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ THE DUPLICATE. A sale rung up HERE appears in both lists. Without de-duplication the
        /// operator sees it twice, identically, and picking the wrong one is a coin toss they should
        /// never have been asked to make.
        /// </summary>
        [Fact]
        public void A_sale_this_till_rang_up_appears_once_not_twice()
        {
            var id = Guid.NewGuid();

            var found = SaleFinder.Merge(
                new[] { Local(id, 1000) },
                new[] { Platform(id, 1000) });

            var only = Assert.Single(found);
            Assert.Equal(SaleSource.ThisTill, only.Source);
        }

        /// <summary>⚠ LOCAL WINS, because it is the record that still works when the connection drops
        /// halfway through a refund.</summary>
        [Fact]
        public void The_local_record_wins_a_tie()
        {
            var id = Guid.NewGuid();

            var found = SaleFinder.Merge(
                new[] { Local(id, 1000) },
                new[] { Platform(id, 1000, channel: "Webstore") });

            Assert.Equal(SaleSource.ThisTill, Assert.Single(found).Source);
        }

        [Fact]
        public void A_sale_from_another_till_is_included_and_marked_as_such()
        {
            var found = SaleFinder.Merge(
                new[] { Local(Guid.NewGuid(), 1000) },
                new[] { Platform(Guid.NewGuid(), 2000, channel: "Till") });

            Assert.Equal(2, found.Count);
            Assert.Contains(found, f => f.Source == SaleSource.OtherTill);
        }

        /// <summary>⚠ The operator can SEE which is which. "This till" and "another till" behave
        /// differently with the network down, and they must know before promising a customer.</summary>
        [Fact]
        public void The_channel_is_shown_for_a_sale_from_elsewhere()
        {
            var found = SaleFinder.Merge(
                Array.Empty<LocalSaleSummary>(),
                new[] { Platform(Guid.NewGuid(), 2000, channel: "Webstore") });

            Assert.Contains("Webstore", Assert.Single(found).Describe());
        }

        // ── refunds are not refundable ────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠⚠ A REFUND IS ITSELF A SALE, with a NEGATIVE gross. Offering one as something to refund
        /// against is how the same £13.99 went back twice on 2026-08-10. Excluded from BOTH sources,
        /// because it can arrive from either.
        /// </summary>
        [Fact]
        public void A_refund_is_never_offered_as_something_to_refund_against()
        {
            var found = SaleFinder.Merge(
                new[] { Local(Guid.NewGuid(), -1399) },
                new[] { Platform(Guid.NewGuid(), -1399) });

            Assert.Empty(found);
        }

        [Fact]
        public void A_refund_does_not_hide_the_real_sales_around_it()
        {
            var found = SaleFinder.Merge(
                new[] { Local(Guid.NewGuid(), -500), Local(Guid.NewGuid(), 1000) },
                Array.Empty<SaleListEntry>());

            Assert.Equal(1000, Assert.Single(found).GrossPence);
        }

        // ── order ─────────────────────────────────────────────────────────────────────────────

        /// <summary>⚠ Newest first, ACROSS both sources — an operator looking for a sale is nearly
        /// always looking for a recent one, and interleaving matters when the other till is busier.</summary>
        [Fact]
        public void The_newest_sale_is_first_whichever_till_took_it()
        {
            var oldLocal = Guid.NewGuid();
            var newPlatform = Guid.NewGuid();

            var found = SaleFinder.Merge(
                new[] { Local(oldLocal, 1000, minutes: -60) },
                new[] { Platform(newPlatform, 2000, minutes: -5) });

            Assert.Equal(newPlatform, found[0].SaleId);
            Assert.Equal(oldLocal, found[1].SaleId);
        }

        // ── nothing to find ───────────────────────────────────────────────────────────────────

        [Fact]
        public void Nothing_anywhere_is_an_empty_list_rather_than_a_throw()
        {
            Assert.Empty(SaleFinder.Merge(null, null));
            Assert.Empty(SaleFinder.Merge(Array.Empty<LocalSaleSummary>(), Array.Empty<SaleListEntry>()));
        }

        /// <summary>⚠ OFFLINE IS THE COMMON CASE, not an error: this till's own sales still list when
        /// the platform cannot be reached, which is the overwhelming majority of refunds.</summary>
        [Fact]
        public void With_the_platform_unreachable_this_tills_own_sales_still_list()
        {
            var found = SaleFinder.Merge(new[] { Local(Guid.NewGuid(), 1000) }, null);

            Assert.Single(found);
            Assert.Equal(SaleSource.ThisTill, found[0].Source);
        }
    }
}
