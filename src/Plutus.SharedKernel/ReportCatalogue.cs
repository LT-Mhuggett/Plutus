using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel
{
    /// <summary>
    /// Every report a till CAN offer — the superset the portal chooses from. Ruling 5b(a), 2026-08-19.
    ///
    /// ⚠⚠ MATT: *"Portal shows which reports a till can show."* That sentence only means something if
    /// there is one agreed list of what "a report" is. Before this file each client carried its own —
    /// `Services/Reporting/ReportCatalogue` on MAUI, subtab ids on the web till — so "which reports
    /// exist" had two answers and the portal had none. A portal that publishes a key no till renders,
    /// or a till that renders a key the portal cannot publish, is the same class of silent gap that
    /// `ReportPermissions` fails closed on.
    ///
    /// ⚠⚠ **THE PUBLISH DECIDES THE MENU, THE PERMISSION DECIDES THE DOOR** — the ruling's own words,
    /// and the two are independent. A till may be published a report whose permission its operator
    /// lacks, and must then show **nothing** rather than a refusal (a greyed row leaks what other roles
    /// can see). So a report is listed only when it is BOTH published to this till AND readable by this
    /// operator — see <see cref="ReportPermissions.MayReadReport"/>.
    ///
    /// ⚠ THIS IS NOT A PERMISSION LIST and must never become one. It answers "does this report exist",
    /// nothing about who may read it. The two are pinned to each other by
    /// <c>ReportCatalogueTests</c>: every key here has an entry in <see cref="ReportPermissions"/>, and
    /// every key there is either here or a documented alias. A key in one and not the other is exactly
    /// how a report becomes unreachable without any build noticing.
    ///
    /// ⚠ Keys are WIRE VALUES. They are stored in `ReportPublication.KeysJson` and travel to both
    /// clients, so renaming one silently unpublishes it for every tenant that had it. Add, never rename.
    /// </summary>
    public static class ReportCatalogue
    {
        /// <summary>One report the portal can publish to a till.</summary>
        /// <param name="Key">The wire value. ⚠ Stored in the database — add, never rename.</param>
        /// <param name="Label">What the portal and both tills call it, in English.</param>
        /// <param name="Blurb">One line for the portal, so an owner curating the list knows what they
        /// are turning off. ⚠ Written for a shopkeeper, not for us.</param>
        public sealed record Entry(string Key, string Label, string Blurb);

        /// <summary>
        /// ⚠ ORDER IS THE MENU ORDER on both tills — takings first because it is the one read every day,
        /// stock last because it is a different question from "how did we trade".
        /// </summary>
        public static IReadOnlyList<Entry> All { get; } = new[]
        {
            new Entry("summary", "Takings",
                "What was taken each day, and how it was paid for. The one most shops read every day."),
            new Entry("vat", "VAT",
                "Sales split by VAT rate, for the return. Read by whoever does the books."),
            new Entry("items-sold", "Items sold",
                "Every line sold in a date range — what moved, how many, at what price."),
            new Entry("category-sales", "Category sales",
                "Takings grouped by category, for deciding what to buy more of."),
            new Entry("best-sellers", "Best sellers",
                "The top-selling items over a range."),
            new Entry("stock", "Stock levels",
                "What the system thinks is on the shelf right now."),
            new Entry("negative-stock", "Negative stock",
                "Only the lines that have gone below zero — usually a goods-in that was never booked."),
            new Entry("sales", "Sale look-up",
                "Individual transactions and how each was paid. ⚠ Shows a customer's basket, so it is "
                + "the one report a shop may want readable by fewer people than the totals."),
        };

        /// <summary>Every key, in menu order.</summary>
        public static IReadOnlyList<string> Keys { get; } = All.Select(e => e.Key).ToList();

        /// <summary>Is this a report this platform knows about?</summary>
        public static bool IsKnown(string? key) =>
            key != null && Keys.Contains(key, StringComparer.Ordinal);

        /// <summary>
        /// The published set for a till, given what the portal stored — or **everything** when the portal
        /// has stored nothing.
        ///
        /// ⚠⚠ THE DEFAULT IS EVERY REPORT, and that is the whole compatibility story. A tenant who has
        /// never opened the new portal screen must see exactly the menu they saw yesterday; defaulting to
        /// "nothing published" would empty the Reports tab on every till in every shop the moment this
        /// deployed. **Absence of a choice is not a choice.**
        ///
        /// ⚠ Unknown keys are DROPPED rather than passed through. A key stored by a newer build, or a
        /// typo, must not reach a client as a menu row it cannot render.
        ///
        /// ⚠ Returned in catalogue order, not in the stored order, so the menu cannot be reshuffled by
        /// however the portal happened to serialise the list.
        /// </summary>
        /// <param name="storedKeys">What the portal published, or null/empty when it never has.</param>
        public static IReadOnlyList<string> PublishedOr(IEnumerable<string>? storedKeys)
        {
            if (storedKeys is null) return Keys;

            var stored = new HashSet<string>(storedKeys, StringComparer.Ordinal);

            // ⚠ An EMPTY stored list is a deliberate "publish nothing", which is different from never
            // having chosen — the caller distinguishes them by passing null for the latter. A shop that
            // genuinely wants no reports on a till is allowed to say so.
            return Keys.Where(stored.Contains).ToList();
        }
    }
}
