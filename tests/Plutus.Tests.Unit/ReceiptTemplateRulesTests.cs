using System;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// How a portal-set receipt template and a store's real details combine (step 26).
///
/// ⚠⚠ THE TWIN THIS PROTECTS: the web till merges these in `api.ts mergeTemplate`, and MAUI was
/// about to grow its own. Two implementations means the same store prints its address on one till
/// and not on the other, from identical portal settings — and a receipt is legally required to
/// identify the trader.
/// </summary>
public class ReceiptTemplateRulesTests
{
    private static readonly ReceiptStoreDetails Store = new(
        Name: "Kapow Comics",
        AdLine1: "12 High Street",
        AdLine2: "",
        City: "Leeds",
        PostCode: "LS1 1AA",
        ContactNumber: "0113 000 0000",
        VatNumber: "GB123456789");

    // ── the rule: the store fills the blanks ──────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE ONE THAT MATTERS. A shop that has never touched its template still prints its own
    /// name, address, phone and VAT number. Before the web till did this it printed NONE of them
    /// while the portal's preview pretended otherwise.
    /// </summary>
    [Fact]
    public void An_untouched_template_still_prints_the_stores_own_details()
    {
        var effective = ReceiptTemplateRules.Merge(saved: null, Store);

        Assert.Equal("Kapow Comics", effective.StoreName);
        Assert.Equal("0113 000 0000", effective.Phone);
        Assert.Equal("GB123456789", effective.VatNumber);
        Assert.Equal(new[] { "12 High Street", "Leeds", "LS1 1AA" }, effective.AddressLines);
    }

    /// <summary>⚠ A blank address line on the store record is DROPPED, not printed — an empty line
    /// mid-address reads as a printer fault. (`AdLine2` is empty above.)</summary>
    [Fact]
    public void Blank_store_address_lines_are_dropped_rather_than_printed()
    {
        var effective = ReceiptTemplateRules.Merge(null, Store);

        Assert.DoesNotContain(effective.AddressLines!, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void What_the_template_sets_beats_the_store_record()
    {
        var saved = new ReceiptTemplate(StoreName: "Kapow!", Phone: "0800 000 0000");

        var effective = ReceiptTemplateRules.Merge(saved, Store);

        Assert.Equal("Kapow!", effective.StoreName);
        Assert.Equal("0800 000 0000", effective.Phone);
        // ⚠ and the fields it did NOT set still fall back
        Assert.Equal("GB123456789", effective.VatNumber);
    }

    /// <summary>⚠ WHITESPACE COUNTS AS UNSET. A field somebody cleared in the portal falls back
    /// rather than printing a space and looking like a rendering fault.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_cleared_template_field_falls_back_to_the_store(string cleared)
    {
        var effective = ReceiptTemplateRules.Merge(new ReceiptTemplate(StoreName: cleared), Store);

        Assert.Equal("Kapow Comics", effective.StoreName);
    }

    /// <summary>
    /// ⚠⚠ AN ADDRESS THE TEMPLATE SETS WINS ENTIRELY. Merging two addresses line by line produces
    /// one that belongs to nobody — half the shop's, half the portal's.
    /// </summary>
    [Fact]
    public void A_template_address_replaces_the_stores_rather_than_interleaving_with_it()
    {
        var saved = new ReceiptTemplate(AddressLines: new[] { "Unit 4", "Trading Estate" });

        var effective = ReceiptTemplateRules.Merge(saved, Store);

        Assert.Equal(new[] { "Unit 4", "Trading Estate" }, effective.AddressLines);
        Assert.DoesNotContain("Leeds", effective.AddressLines!);
    }

    [Fact]
    public void An_empty_template_address_falls_back_to_the_stores()
    {
        var saved = new ReceiptTemplate(AddressLines: Array.Empty<string>());

        Assert.Contains("Leeds", ReceiptTemplateRules.Merge(saved, Store).AddressLines!);
    }

    [Fact]
    public void With_no_store_record_the_template_is_used_as_it_stands()
    {
        var saved = new ReceiptTemplate(StoreName: "Kapow!");

        Assert.Equal("Kapow!", ReceiptTemplateRules.Merge(saved, null).StoreName);
    }

    // ── toggles ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ TOGGLES COME FROM THE SAVED TEMPLATE ONLY, never from the store record. A store simply
    /// HAVING a VAT number is not the same as choosing to print it — that is a layout decision
    /// somebody made in the portal.
    /// </summary>
    [Fact]
    public void Turning_the_vat_number_off_beats_the_store_having_one()
    {
        var saved = new ReceiptTemplate(ShowVatNumber: false);

        var effective = ReceiptTemplateRules.Merge(saved, Store);

        Assert.Equal("GB123456789", effective.VatNumber);      // still known…
        Assert.False(ReceiptTemplateRules.PrintsVatNumber(effective));  // …and still not printed
    }

    /// <summary>⚠ BOTH the toggle and a number are needed. A "VAT No:" label with nothing after it
    /// is worse than omitting the line — it looks like the shop failed to fill something in.</summary>
    [Fact]
    public void A_vat_line_needs_both_the_toggle_and_an_actual_number()
    {
        Assert.False(ReceiptTemplateRules.PrintsVatNumber(
            ReceiptTemplateRules.Merge(null, new ReceiptStoreDetails(Name: "No VAT Ltd"))));

        Assert.True(ReceiptTemplateRules.PrintsVatNumber(ReceiptTemplateRules.Merge(null, Store)));
    }

    // ── header and footer ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_template_header_is_used_when_it_sets_one()
    {
        var saved = new ReceiptTemplate(HeaderLines: new[] { "Kapow! Comics", "Est. 2004" });

        Assert.Equal(
            new[] { "Kapow! Comics", "Est. 2004" },
            ReceiptTemplateRules.HeaderOr(saved, new[] { "Thank you" }));
    }

    [Fact]
    public void The_callers_default_header_is_used_when_the_template_sets_none()
    {
        Assert.Equal(
            new[] { "Thank you" },
            ReceiptTemplateRules.HeaderOr(new ReceiptTemplate(), new[] { "Thank you" }));
    }

    /// <summary>
    /// ⚠ THERE IS NO DEFAULT FOOTER, deliberately. A header can be a pleasantry; a footer is usually
    /// a returns policy, and inventing one is a promise the shop never made.
    /// </summary>
    [Fact]
    public void There_is_no_invented_footer()
    {
        Assert.Empty(ReceiptTemplateRules.FooterOr(new ReceiptTemplate()));
        Assert.Empty(ReceiptTemplateRules.FooterOr(null));
    }

    /// <summary>⚠ Blank lines are dropped from both — an empty line on a receipt reads as a fault.</summary>
    [Fact]
    public void Blank_header_and_footer_lines_are_dropped()
    {
        var saved = new ReceiptTemplate(
            HeaderLines: new[] { "Kapow!", "  ", "" },
            FooterLines: new[] { "", "No refunds after 30 days" });

        Assert.Equal(new[] { "Kapow!" }, ReceiptTemplateRules.HeaderOr(saved, new[] { "x" }));
        Assert.Equal(new[] { "No refunds after 30 days" }, ReceiptTemplateRules.FooterOr(saved));
    }

    /// <summary>⚠ A template whose lines are ALL blank counts as setting none, so the caller's
    /// default still appears rather than a header of nothing.</summary>
    [Fact]
    public void An_all_blank_header_falls_back_to_the_default()
    {
        var saved = new ReceiptTemplate(HeaderLines: new[] { "", "   " });

        Assert.Equal(new[] { "Thank you" }, ReceiptTemplateRules.HeaderOr(saved, new[] { "Thank you" }));
    }
}
