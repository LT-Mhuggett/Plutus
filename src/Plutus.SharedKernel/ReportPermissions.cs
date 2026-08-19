using System.Collections.Generic;

namespace Plutus.SharedKernel
{
    /// <summary>
    /// Which permission lets somebody read which report — ruling 5b, 2026-08-18.
    ///
    /// ⚠⚠ MATT: *"Portal shows which reports a till can show. Separate permissions need to be created
    /// for viewing them."* This file is the second half. The first half — the portal choosing which
    /// reports a till OFFERS — is a separate work package, and the ruling settles how the two meet:
    /// **the publish decides the menu, the permission decides the door.**
    ///
    /// ⚠⚠ THIS EXISTS SO A TILL CAN FILTER ITS OWN MENU. Gating the endpoints alone is not enough: an
    /// operator would see eight reports and get a refusal from seven of them. Worse, a greyed-out row
    /// **leaks what other roles can see** — so a report somebody may not read is simply not listed.
    ///
    /// ⚠ `pos.reports.view` IS A MASTER KEY. Every report resolves to *"the master, or this report's own
    /// code"*, which is what lets existing roles keep working untouched while a new narrow role is
    /// built from specific codes alone. See `PermissionCatalogue`'s comment for why making the specific
    /// codes mandatory would have been a live-shop regression.
    ///
    /// ⚠⚠ C2 TWIN. The web till needs the same map (`reportPermissions.ts`) because it filters its own
    /// subtabs, and two tills that disagree about who may read the VAT report disagree about who may
    /// read the VAT report. Pinned by shared vectors — `ReportPermissionsTests` here and its TS
    /// counterpart. ⚠ **A report added to one client's catalogue and not to this map is invisible on
    /// that till**, which is the failure mode to watch: it fails CLOSED, which is the right direction,
    /// but it fails silently.
    /// </summary>
    public static class ReportPermissions
    {
        /// <summary>
        /// Report key → the permission that specifically grants it.
        ///
        /// ⚠ The keys are the ones both clients use for their menus (`ReportCatalogue.Key` on MAUI, the
        /// subtab ids on the web till). ⚠ **`stock` and `negative-stock` share one code** — negative
        /// stock is the same rows filtered below zero, so splitting them would invent a role that can
        /// see the worst of the data and not its context.
        /// </summary>
        private static readonly Dictionary<string, string> Specific = new()
        {
            ["summary"] = PermissionCatalogue.PosReportsTakings,
            ["takings"] = PermissionCatalogue.PosReportsTakings,
            ["vat"] = PermissionCatalogue.PosReportsVat,
            ["items-sold"] = PermissionCatalogue.PosReportsItemsSold,
            ["category-sales"] = PermissionCatalogue.PosReportsCategorySales,
            ["best-sellers"] = PermissionCatalogue.PosReportsBestSellers,
            ["stock"] = PermissionCatalogue.PosReportsStock,
            ["negative-stock"] = PermissionCatalogue.PosReportsStock,
            ["sales"] = PermissionCatalogue.PosReportsSales,
        };

        /// <summary>
        /// The specific code for a report key, or null when the key is unknown.
        ///
        /// ⚠ NULL FOR AN UNKNOWN KEY rather than an exception: a client on an older build may name a
        /// report this map has never heard of, and a reports screen that throws is worse than one
        /// missing a row. <see cref="MayRead"/> then falls back to the master key, so an unmapped
        /// report behaves exactly as it did before 5b — visible to anyone with `pos.reports.view`.
        /// </summary>
        public static string? SpecificCodeFor(string? reportKey) =>
            reportKey != null && Specific.TryGetValue(reportKey, out var code) ? code : null;

        /// <summary>
        /// Every code that opens this report — the master keys, plus its own if it has one.
        ///
        /// ⚠⚠ THIS EXISTS BECAUSE MAUI MUST NOT USE <see cref="MayRead"/>. A till resolves permissions
        /// through `TillGate`, which also applies the **staleness tier** and the **permission window** —
        /// a supervisor whose roster is a fortnight old, or who is only authorised on Saturdays, is
        /// answered differently from one who simply holds the code. Testing a raw code list here would
        /// silently bypass both, and a report is not the place to invent a second answer to "may this
        /// person do this right now".
        ///
        /// ⚠ So the RULE lives here and the GATE stays where it is: the caller feeds these codes to its
        /// own `CheckAny`. That keeps one definition of which codes open a report without duplicating
        /// how a till decides whether somebody holds one.
        /// </summary>
        public static string[] CodesThatOpen(string? reportKey)
        {
            var specific = SpecificCodeFor(reportKey);

            return specific == null
                ? new[]
                {
                    PermissionCatalogue.PosReportsView,
                    PermissionCatalogue.PortalReportsView,
                    PermissionCatalogue.PortalFinancialsView,
                }
                : new[]
                {
                    PermissionCatalogue.PosReportsView,
                    PermissionCatalogue.PortalReportsView,
                    PermissionCatalogue.PortalFinancialsView,
                    specific,
                };
        }

        /// <summary>
        /// May somebody holding <paramref name="heldCodes"/> read this report?
        ///
        /// ⚠ FOR CALLERS THAT ALREADY HAVE A FLAT CODE LIST — the web till, and tests. ⚠⚠ **A till must
        /// use <see cref="CodesThatOpen"/> with its own gate instead**, or it loses the staleness tier
        /// and the permission window.
        ///
        /// ⚠⚠ THE MASTER KEY OR THE SPECIFIC ONE — never both required. `pos.reports.view` has always
        /// meant "the till's reports", and it still does.
        ///
        /// ⚠ A PORTAL READER PASSES TOO. `portal.reports.view` and `portal.financials.view` already
        /// reach every one of these endpoints server-side, so a screen that hid a report from somebody
        /// the SERVER would serve would be lying to them. The list and the door must agree.
        /// </summary>
        /// <param name="heldCodes">The codes this person actually holds. ⚠ Case-sensitive, as
        /// `PermissionCatalogue.All` is — the codes are constants, not typed by anybody.</param>
        public static bool MayRead(string? reportKey, IReadOnlyCollection<string>? heldCodes)
        {
            if (heldCodes == null || heldCodes.Count == 0) return false;

            if (heldCodes.Contains(PermissionCatalogue.PosReportsView)
                || heldCodes.Contains(PermissionCatalogue.PortalReportsView)
                || heldCodes.Contains(PermissionCatalogue.PortalFinancialsView))
                return true;

            var specific = SpecificCodeFor(reportKey);

            // ⚠ An unmapped report has no specific code, so it is master-key-only — the pre-5b
            // behaviour, and the safe answer for a key this build does not recognise.
            return specific != null && heldCodes.Contains(specific);
        }
    }
}
