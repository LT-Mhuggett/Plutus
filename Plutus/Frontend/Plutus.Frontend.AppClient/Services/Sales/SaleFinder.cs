using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;

namespace Plutus.Frontend.AppClient.Services.Sales
{
    /// <summary>
    /// One sale an operator can pick — from this till or from any other.
    /// </summary>
    /// <param name="Source">Where it was found. ⚠ SHOWN to the operator: "this till" and "another
    /// till" behave differently when the network is down, and they must be able to see which they
    /// are looking at before they promise a customer anything.</param>
    public sealed record FoundSale(
        Guid SaleId,
        DateTime OccurredAtUtc,
        long GrossPence,
        SaleSource Source,
        string TillLabel = "")
    {
        /// <summary>⚠ A REFUND IS ITSELF A SALE, and its gross is NEGATIVE. One must never be offered
        /// as something to refund against — doing so refunded £13.99 twice on 2026-08-10.</summary>
        public bool IsRefund => GrossPence < 0;

        public string Describe() =>
            $"{OccurredAtUtc.ToLocalTime():dd MMM HH:mm} · {(GrossPence / 100m).ToString("C2", CultureInfo.CurrentCulture)}"
            + (Source == SaleSource.OtherTill && !string.IsNullOrWhiteSpace(TillLabel) ? $" · {TillLabel}" : string.Empty);
    }

    public enum SaleSource : byte
    {
        /// <summary>This till's own record — available OFFLINE, which is the common case.</summary>
        ThisTill = 0,

        /// <summary>The platform's record of a sale rung up elsewhere. ⚠ Needs connectivity.</summary>
        OtherTill = 1,
    }

    /// <summary>
    /// Finding a past sale, wherever it was rung up (step 26).
    ///
    /// ⚠⚠ THIS IS WHAT MAKES A CROSS-TILL REFUND AND REPRINT POSSIBLE. Until now the picker in front
    /// of the Returns dialog showed **this till's last 20 sales only**, so goods bought at another
    /// counter meant typing a UUID off a receipt. The platform's list is the only path to another
    /// till's sale, and the SAME picker serves the refund and the reprint — they are one screen with
    /// two callers, which is why they are built together.
    ///
    /// ⚠ LOCAL FIRST, ALWAYS. This till's own record works with the line down and covers the
    /// overwhelming majority of refunds; the platform is the fallback, not the primary. A picker that
    /// went to the server first would be slower every day to be better on rare days — and would show
    /// nothing at all during an outage.
    ///
    /// ⚠ IT DECIDES NOTHING ABOUT MONEY. What is still refundable comes from reading the real sale
    /// (`GET /api/v1/sales/{saleId}`) and `RefundRules`; a list entry cannot say, and a cap computed
    /// from one is not a cap.
    /// </summary>
    public static class SaleFinder
    {
        /// <summary>How many of this till's own sales to offer. ⚠ A picker is something a person
        /// reads, not a report — the web till shows a comparable handful.</summary>
        public const int LocalTake = 20;

        /// <summary>And how many from the platform. ⚠ The server clamps to 500; asking for that many
        /// would produce a picker nobody can use.</summary>
        public const int PlatformTake = 50;

        /// <summary>
        /// Merge this till's recent sales with the platform's.
        ///
        /// ⚠⚠ DE-DUPLICATED ON SALE ID, LOCAL WINNING. This till's own sales are ALSO in the
        /// platform's list, so without this every recent sale appears twice — and the operator has
        /// to guess which of two identical rows to press. Local wins because it is the record that
        /// still works when the connection drops mid-refund.
        ///
        /// ⚠ REFUNDS ARE EXCLUDED. A refund is itself a sale with a negative gross, and offering one
        /// as something to refund against is how the same £13.99 went back twice on 2026-08-10.
        ///
        /// ⚠ NEWEST FIRST. An operator looking for a sale is nearly always looking for a recent one.
        /// </summary>
        public static IReadOnlyList<FoundSale> Merge(
            IEnumerable<LocalSaleSummary> local,
            IEnumerable<SaleListEntry> platform)
        {
            var found = new List<FoundSale>();
            var seen = new HashSet<Guid>();

            foreach (var s in local ?? Enumerable.Empty<LocalSaleSummary>())
            {
                if (s.GrossPence < 0) continue;              // a refund is not refundable
                if (!seen.Add(s.SaleId)) continue;

                found.Add(new FoundSale(s.SaleId, s.OccurredAtUtc, s.GrossPence, SaleSource.ThisTill));
            }

            foreach (var s in platform ?? Enumerable.Empty<SaleListEntry>())
            {
                if (s.GrossPence < 0) continue;
                if (!seen.Add(s.Id)) continue;               // ⚠ local already has it — do not repeat

                found.Add(new FoundSale(
                    s.Id, s.OccurredAtUtc, s.GrossPence, SaleSource.OtherTill,
                    // ⚠ The channel matters at a counter: a webstore order is not refundable the
                    // same way as something rung up on a till.
                    string.IsNullOrWhiteSpace(s.Channel) ? "another till" : s.Channel));
            }

            return found.OrderByDescending(f => f.OccurredAtUtc).ToList();
        }
    }
}
