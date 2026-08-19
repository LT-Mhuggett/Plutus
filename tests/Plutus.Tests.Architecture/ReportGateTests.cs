using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// ⚠⚠ THE LIST AND THE DOOR MUST AGREE — ruling 5b, 2026-08-19.
///
/// Matt: *"Portal shows which reports a till can show. Separate permissions need to be created for
/// viewing them."* Both tills now filter their own report menu through `ReportPermissions` (C#) and
/// `reportPermissions.ts` (the C2 twin). **A menu is not a permission.** If a report's specific code is
/// not also accepted by the endpoint that serves it, a role granted only `pos.reports.vat` is *offered*
/// the VAT report and gets a **403** when it taps — which reads to a shop as a broken till, not as a
/// permission they were never given.
///
/// ⚠ The failure is SILENT in both directions and that is why it is worth a test rather than a comment.
/// Add a report to the catalogue and forget the gate → offered, then 403. Add a code to the catalogue and
/// forget everything else → it opens nothing at all, for ever, and no build says a word.
///
/// ⚠ Scanned on disk rather than by reflection, matching the rest of this suite (no product refs).
/// </summary>
public class ReportGateTests
{
    private static readonly string[] ApiRoots = { "src", Path.Combine("Plutus", "Endpoints") };

    /// <summary>
    /// Route → the `PermissionCatalogue` constant its `[Authorize]` must accept, on top of the master
    /// key it already accepts.
    ///
    /// ⚠ `summary` and `summary-rich` share `PosReportsTakings` — they are the same figures at two
    /// levels of detail, and a role that may read the takings may read the takings. ⚠ `stock/levels`
    /// serves BOTH the stock report and the negative-stock report (the same rows filtered below zero),
    /// which is why one code covers them.
    /// </summary>
    private static readonly Dictionary<string, string> RequiredCodeByRoute = new()
    {
        ["api/v1/reports/summary"] = "PosReportsTakings",
        ["api/v1/reports/summary-rich"] = "PosReportsTakings",
        ["api/v1/reports/vat"] = "PosReportsVat",
        ["api/v1/reports/items-sold"] = "PosReportsItemsSold",
        ["api/v1/reports/category-sales"] = "PosReportsCategorySales",
        ["api/v1/reports/best-sellers"] = "PosReportsBestSellers",
        ["api/v1/stock/levels"] = "PosReportsStock",
        ["api/v1/sales"] = "PosReportsSales",
        ["api/v1/sales/{saleId}"] = "PosReportsSales",
    };

    /// <summary>
    /// Endpoints that carry the reports MASTER key but deliberately no specific code, each with the
    /// reason. ⚠ A specific code on any of these would be an INVENTED mapping — `ReportPermissions`
    /// names no report key for them — and inventing one is how the list and the door come apart again.
    /// </summary>
    private static readonly Dictionary<string, string> NoSpecificCodeOnPurpose = new()
    {
        ["api/v1/reports/vat-integrity"] = "A portal diagnostic, not a report on either till's menu.",
        ["api/v1/stock/levels/bulk"] = "The till's catalogue SYNC, not something an operator reads.",
        ["api/v1/cash-events"] = "Drawer movements; no report key covers them.",
    };

    /// <summary>Every `[HttpX("route")]` with the attribute text that follows it, to the member.</summary>
    private static IEnumerable<(string Route, string Attributes, string File)> Endpoints()
    {
        var routeRx = new Regex("""\[Http(?:Get|Post|Put|Delete|Patch)\("([^"]+)"\)\]""");

        foreach (var file in ApiRoots.SelectMany(Repo.CsFiles))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var m = routeRx.Match(lines[i]);
                if (!m.Success) continue;

                // Everything from the route down to the first line that is not an attribute — the
                // gate can sit either side of the route line, so take a window and search it.
                var start = Math.Max(0, i - 6);
                var window = string.Join("\n", lines.Skip(start).Take(i - start + 8));
                yield return (m.Groups[1].Value.TrimStart('/'), window, file);
            }
        }
    }

    /// <summary>
    /// ⚠⚠ THE ONE THAT MATTERS. Every report a till can be granted individually must be readable with
    /// that grant alone.
    /// </summary>
    [Fact]
    public void Every_report_endpoint_accepts_its_own_specific_permission()
    {
        var found = new Dictionary<string, string>();
        foreach (var (route, attributes, _) in Endpoints())
            if (RequiredCodeByRoute.ContainsKey(route) && attributes.Contains("Authorize"))
                found[route] = attributes;

        var missing = new List<string>();
        foreach (var (route, code) in RequiredCodeByRoute)
        {
            if (!found.TryGetValue(route, out var attributes))
            {
                missing.Add($"{route} — no gated endpoint found for this route at all");
                continue;
            }

            if (!attributes.Contains(code))
                missing.Add($"{route} — its [Authorize] does not accept PermissionCatalogue.{code}");
        }

        Assert.True(missing.Count == 0,
            "A report is offered on a till menu that the server will refuse:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// ⚠ THE OTHER DIRECTION: a specific code that opens nothing. Granting it would look like giving
    /// somebody a report and would give them a 403 — and nothing else in the build has an opinion about
    /// a permission that no endpoint accepts.
    /// </summary>
    [Fact]
    public void Every_specific_report_permission_opens_at_least_one_endpoint()
    {
        var catalogue = File.ReadAllText(
            Path.Combine(Repo.Root(), "src", "Plutus.SharedKernel", "Permissions.cs"));

        // The specific report codes are `pos.reports.<something>` other than the master key.
        var codes = Regex.Matches(catalogue, """public const string (PosReports\w+) = "(pos\.reports\.[^"]+)";""")
            .Select(m => (Constant: m.Groups[1].Value, Code: m.Groups[2].Value))
            .Where(c => c.Code != "pos.reports.view")
            .ToList();

        Assert.NotEmpty(codes);   // ⚠ a regex that matches nothing would pass this test vacuously

        var allGates = string.Join("\n", Endpoints().Select(e => e.Attributes));

        var orphans = codes.Where(c => !allGates.Contains(c.Constant)).Select(c => c.Code).ToList();

        Assert.True(orphans.Count == 0,
            "These report permissions can be granted but open no endpoint:\n  " + string.Join("\n  ", orphans));
    }

    /// <summary>
    /// ⚠ The deliberate exclusions stay deliberate. If one of these grows a specific code, someone has
    /// invented a report key — read `ReportPermissions` first and add it there if it is real.
    /// </summary>
    [Fact]
    public void The_endpoints_left_without_a_specific_code_stay_that_way()
    {
        var wrong = new List<string>();

        foreach (var (route, attributes, _) in Endpoints())
        {
            if (!NoSpecificCodeOnPurpose.ContainsKey(route)) continue;
            if (!attributes.Contains("Authorize")) continue;

            // ⚠ `PosReportsView` is the master key and is expected here; anything else is a specific code.
            var specifics = Regex.Matches(attributes, @"PermissionCatalogue\.(PosReports\w+)")
                .Select(m => m.Groups[1].Value)
                .Where(n => n != "PosReportsView")
                .Distinct()
                .ToList();

            if (specifics.Count > 0)
                wrong.Add($"{route} — gained {string.Join(", ", specifics)}; reason it had none: {NoSpecificCodeOnPurpose[route]}");
        }

        Assert.True(wrong.Count == 0,
            "An endpoint deliberately without a per-report code has grown one:\n  " + string.Join("\n  ", wrong));
    }
}
