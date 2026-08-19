using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Who may read which report — ruling 5b, 2026-08-18.
///
/// ⚠⚠ THESE VECTORS ARE THE C2 PIN. The same cases run in
/// `Plutus.Frontend.WebApp/src/reporting/reportPermissions.test.ts`, because the web till filters its
/// own subtabs and two tills that disagree about who may read the VAT report disagree about who may
/// read the VAT report. **Add a case here and add it there, in the same commit.**
/// </summary>
public class ReportPermissionsTests
{
    private static string[] Held(params string[] codes) => codes;

    /// <summary>
    /// ⚠⚠ THE COMPATIBILITY GUARANTEE, AND THE MOST IMPORTANT TEST HERE. `pos.reports.view` is a MASTER
    /// KEY: every existing Supervisor, Store Manager and Auditor keeps every report the moment this
    /// deploys, with no re-seed and no waiting for a 12-hour token to expire.
    ///
    /// ⚠ If this ever goes red, the deploy logs every supervisor out of every report in a live shop.
    /// </summary>
    [Theory]
    [InlineData("summary")]
    [InlineData("vat")]
    [InlineData("items-sold")]
    [InlineData("category-sales")]
    [InlineData("best-sellers")]
    [InlineData("stock")]
    [InlineData("negative-stock")]
    [InlineData("sales")]
    public void The_master_key_still_opens_every_report(string reportKey)
    {
        Assert.True(ReportPermissions.MayRead(reportKey, Held(PermissionCatalogue.PosReportsView)));
    }

    /// <summary>⚠ A portal reader passes too — the server already serves them every one of these
    /// endpoints, so a screen that hid a report from somebody the SERVER would serve would be lying.
    /// The list and the door must agree.</summary>
    [Theory]
    [InlineData(PermissionCatalogue.PortalReportsView)]
    [InlineData(PermissionCatalogue.PortalFinancialsView)]
    public void A_portal_reader_sees_the_reports_the_server_would_serve_them(string code)
    {
        Assert.True(ReportPermissions.MayRead("vat", Held(code)));
    }

    /// <summary>
    /// ⚠⚠ THE POINT OF THE WHOLE RULING: a narrow role. Somebody holding ONLY `pos.reports.vat` reads
    /// the VAT report and **nothing else** — which is what "separate permissions" has to mean, or it
    /// means nothing.
    /// </summary>
    [Fact]
    public void One_specific_code_opens_exactly_one_report()
    {
        var vatOnly = Held(PermissionCatalogue.PosReportsVat);

        Assert.True(ReportPermissions.MayRead("vat", vatOnly));

        Assert.False(ReportPermissions.MayRead("summary", vatOnly));
        Assert.False(ReportPermissions.MayRead("items-sold", vatOnly));
        Assert.False(ReportPermissions.MayRead("stock", vatOnly));
        Assert.False(ReportPermissions.MayRead("sales", vatOnly));
    }

    /// <summary>
    /// ⚠⚠ STOCK AND NEGATIVE STOCK ARE ONE PERMISSION, and that is a decision rather than an
    /// oversight. Negative stock is the SAME rows filtered below zero — a role that could see stock
    /// but not negative stock would be nonsense, and one that could see negative stock but not stock
    /// would be shown the worst of the data and denied its context.
    /// </summary>
    [Fact]
    public void Stock_and_negative_stock_travel_together()
    {
        var stockOnly = Held(PermissionCatalogue.PosReportsStock);

        Assert.True(ReportPermissions.MayRead("stock", stockOnly));
        Assert.True(ReportPermissions.MayRead("negative-stock", stockOnly));
        Assert.Equal(
            ReportPermissions.SpecificCodeFor("stock"),
            ReportPermissions.SpecificCodeFor("negative-stock"));
    }

    /// <summary>
    /// ⚠ THE SALES DRILL-DOWN IS ITS OWN CODE because it is a different kind of look: every other
    /// report is an aggregate, and this one shows individual transactions and how each was paid. A
    /// shop may want the day's takings readable without a customer's basket being readable line by
    /// line — so takings must NOT open it.
    /// </summary>
    [Fact]
    public void Takings_does_not_open_the_sale_drill_down()
    {
        Assert.False(ReportPermissions.MayRead("sales", Held(PermissionCatalogue.PosReportsTakings)));
        Assert.True(ReportPermissions.MayRead("sales", Held(PermissionCatalogue.PosReportsSales)));
    }

    /// <summary>⚠ Holding nothing reads nothing. Both the null and the empty case, because a
    /// not-yet-loaded operator and a genuinely ungranted one arrive here differently.</summary>
    [Fact]
    public void Nobody_with_no_codes_reads_anything()
    {
        Assert.False(ReportPermissions.MayRead("vat", null));
        Assert.False(ReportPermissions.MayRead("vat", System.Array.Empty<string>()));
    }

    /// <summary>
    /// ⚠⚠ AN UNKNOWN REPORT KEY IS MASTER-KEY-ONLY, never an exception. A client on an older or newer
    /// build may name a report this map has not heard of; a reports screen that throws is worse than
    /// one missing a row, and falling back to the master key is exactly the pre-5b behaviour.
    ///
    /// ⚠ It fails CLOSED for a narrow role — which is the right direction, and the reason the class
    /// header warns that a report added to a catalogue but not to the map goes invisible silently.
    /// </summary>
    [Fact]
    public void An_unmapped_report_falls_back_to_the_master_key()
    {
        Assert.Null(ReportPermissions.SpecificCodeFor("some-report-from-the-future"));

        Assert.True(ReportPermissions.MayRead(
            "some-report-from-the-future", Held(PermissionCatalogue.PosReportsView)));

        Assert.False(ReportPermissions.MayRead(
            "some-report-from-the-future", Held(PermissionCatalogue.PosReportsVat)));
    }

    /// <summary>
    /// ⚠⚠ EVERY NEW CODE MUST BE IN `PermissionCatalogue.All`, or the portal cannot offer it —
    /// `AdminController` builds its grantable list from that set, so a code missing from it exists in
    /// the source and can never be given to anybody. A permission nobody can grant is a feature that
    /// looks built and does nothing.
    /// </summary>
    [Fact]
    public void Every_report_code_is_grantable_in_the_portal()
    {
        foreach (var key in new[]
                 {
                     "summary", "vat", "items-sold", "category-sales",
                     "best-sellers", "stock", "negative-stock", "sales",
                 })
        {
            var code = ReportPermissions.SpecificCodeFor(key);
            Assert.NotNull(code);
            Assert.Contains(code!, PermissionCatalogue.All);
        }
    }
}
