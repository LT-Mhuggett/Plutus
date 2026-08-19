using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// ⚠⚠ THE CATALOGUE AND THE PERMISSION MAP MUST AGREE — ruling 5b, both halves.
///
/// `ReportCatalogue` answers "does this report exist"; `ReportPermissions` answers "who may read it".
/// A key in one and not the other is how a report becomes unreachable with nothing to show for it: a
/// catalogue entry with no permission mapping is master-key-only and vanishes for every narrow role,
/// and a permission code for a report no catalogue lists can be granted and opens nothing.
///
/// Both failures are SILENT, which is why this is a test and not a comment in either file.
/// </summary>
public class ReportCatalogueTests
{
    /// <summary>
    /// ⚠ `takings` is a deliberate ALIAS of `summary` in the permission map — the web till has used both
    /// names for the same figures. It is not a catalogue entry of its own, and must not become one.
    /// </summary>
    private static readonly string[] PermissionOnlyAliases = { "takings" };

    [Fact]
    public void Every_report_in_the_catalogue_has_a_permission_mapping()
    {
        var unmapped = ReportCatalogue.Keys
            .Where(k => ReportPermissions.SpecificCodeFor(k) is null)
            .ToList();

        Assert.True(unmapped.Count == 0,
            "These reports exist but no permission grants them specifically, so they are master-key-only "
            + "and invisible to every narrow role:\n  " + string.Join("\n  ", unmapped));
    }

    /// <summary>
    /// ⚠ THE OTHER DIRECTION. A code that can be granted and opens nothing looks like giving somebody a
    /// report and gives them an empty menu.
    /// </summary>
    [Fact]
    public void Every_mapped_report_key_is_either_in_the_catalogue_or_a_declared_alias()
    {
        // The map is private, so probe it through the public surface using the union of what both sides
        // could plausibly name. ⚠ Deliberately includes keys NOT in the catalogue — that is the point.
        var probes = ReportCatalogue.Keys
            .Concat(PermissionOnlyAliases)
            .Concat(new[] { "not-a-report-at-all" })
            .ToList();

        var mappedButUnlisted = probes
            .Where(k => ReportPermissions.SpecificCodeFor(k) is not null)
            .Where(k => !ReportCatalogue.IsKnown(k))
            .Where(k => !PermissionOnlyAliases.Contains(k))
            .ToList();

        Assert.True(mappedButUnlisted.Count == 0,
            "These permission codes grant a report the catalogue does not list:\n  "
            + string.Join("\n  ", mappedButUnlisted));

        // ⚠ And the alias must still actually BE an alias, or this test has been quietly defanged.
        Assert.Equal(ReportPermissions.SpecificCodeFor("summary"), ReportPermissions.SpecificCodeFor("takings"));
    }

    // ── ⚠⚠ The compatibility rule: absence of a choice is not a choice ───────

    /// <summary>
    /// ⚠⚠ THE MOST IMPORTANT CASE HERE. A tenant who has never opened the portal screen must see the
    /// menu they saw yesterday. Defaulting to "nothing published" would empty the Reports tab on every
    /// till in every shop the moment this deployed.
    /// </summary>
    [Fact]
    public void A_tenant_who_has_never_chosen_gets_every_report()
    {
        Assert.Equal(ReportCatalogue.Keys, ReportCatalogue.PublishedOr(null));
    }

    /// <summary>
    /// ⚠ But an EMPTY stored list is a real decision — "this till shows no reports" — and must be
    /// honoured. That is why the never-chosen case is `null` and not `[]`: collapsing the two would make
    /// one of them impossible to express.
    /// </summary>
    [Fact]
    public void An_empty_published_list_is_a_deliberate_choice_and_is_honoured()
    {
        Assert.Empty(ReportCatalogue.PublishedOr(Array.Empty<string>()));
    }

    [Fact]
    public void Only_the_published_reports_come_back()
    {
        var published = ReportCatalogue.PublishedOr(new[] { "vat", "summary" });

        Assert.Equal(new[] { "summary", "vat" }, published);   // ⚠ catalogue order, not stored order
    }

    /// <summary>
    /// ⚠ A key from a newer build, or a typo, must not reach a client as a menu row it cannot render.
    /// </summary>
    [Fact]
    public void An_unknown_stored_key_is_dropped_rather_than_passed_through()
    {
        var published = ReportCatalogue.PublishedOr(new[] { "vat", "a-report-from-the-future" });

        Assert.Equal(new[] { "vat" }, published);
    }

    /// <summary>
    /// ⚠ The menu order is the catalogue's, whatever order the portal serialised. Otherwise the Reports
    /// tab reshuffles itself depending on the order somebody happened to tick the boxes.
    /// </summary>
    [Fact]
    public void The_menu_order_is_the_catalogues_not_the_stored_order()
    {
        var backwards = ReportCatalogue.Keys.Reverse().ToList();

        Assert.Equal(ReportCatalogue.Keys, ReportCatalogue.PublishedOr(backwards));
    }

    /// <summary>⚠ Duplicates in stored data must not duplicate the menu row.</summary>
    [Fact]
    public void A_key_stored_twice_appears_once()
    {
        Assert.Equal(new[] { "vat" }, ReportCatalogue.PublishedOr(new[] { "vat", "vat", "vat" }));
    }

    // ── The catalogue's own integrity ────────────────────────────────────────

    [Fact]
    public void Keys_are_unique_and_none_is_blank()
    {
        Assert.Equal(ReportCatalogue.Keys.Count, ReportCatalogue.Keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ReportCatalogue.All, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Key));
            Assert.False(string.IsNullOrWhiteSpace(e.Label));
            Assert.False(string.IsNullOrWhiteSpace(e.Blurb));
        });
    }

    /// <summary>
    /// ⚠⚠ KEYS ARE WIRE VALUES stored in `ReportPublication.KeysJson`. Renaming one silently unpublishes
    /// it for every tenant that had it ticked. This pins the set that shipped, so a rename fails here
    /// rather than in a shop.
    /// </summary>
    [Fact]
    public void The_shipped_keys_are_pinned_because_they_are_stored_in_the_database()
    {
        Assert.Equal(
            new[]
            {
                "summary", "vat", "items-sold", "category-sales",
                "best-sellers", "stock", "negative-stock", "sales",
            },
            ReportCatalogue.Keys);
    }

    /// <summary>
    /// ⚠ The two halves of the ruling are independent, and this is the case that proves it: a report can
    /// be PUBLISHED to a till and still not be readable by the operator in front of it. The client must
    /// apply both, and showing nothing is the required outcome — never a refusal.
    /// </summary>
    [Fact]
    public void Published_does_not_imply_readable()
    {
        var published = ReportCatalogue.PublishedOr(new[] { "vat" });
        Assert.Contains("vat", published);

        // An operator holding only the takings code sees nothing, despite VAT being published.
        var visible = published.Where(k => ReportPermissions.MayRead(k, new[] { PermissionCatalogue.PosReportsTakings })).ToList();
        Assert.Empty(visible);
    }
}
