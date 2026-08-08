using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Entities.Models;
using Plutus.Reporting;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP2c-exempt — keeping ZERO-RATED and EXEMPT apart, which the rate alone cannot do.
///
/// Both charge the customer nothing (0%). In law they are completely different: zero-rated is a
/// taxable supply and input tax on related costs IS recoverable; exempt is not a taxable supply and
/// input tax attributable to it is NOT (partial exemption, HMRC Notice 706). Kapow sells nothing
/// exempt, but other shops do — so the distinction has to survive every hop from the till to the
/// return, and the places it could silently collapse are all pinned here.
/// </summary>
public class VatExemptBandTests
{
    private static readonly VatBand Standard = new("standard", "20%", VatClass.Standard, 2000, DateTime.UnixEpoch);
    private static readonly VatBand Zero = new("zero", "Zero rated (books)", VatClass.Zero, 0, DateTime.UnixEpoch);
    private static readonly VatBand Exempt = new("exempt", "Exempt", VatClass.Exempt, 0, DateTime.UnixEpoch);

    private static readonly VatBand[] WithExempt = { Standard, Zero, Exempt };
    private static readonly VatBand[] WithoutExempt = { Standard, Zero };

    // ── the rate cannot resolve an ambiguity, and must not pretend to ──────────────────────────

    [Fact]
    public void Snapping_by_RATE_refuses_to_choose_between_two_bands_at_zero_percent()
    {
        // ⚠ THE BUG THIS PREVENTS. `BandFor` used to take the "nearest" band and break ties by list
        // order, so every 0% line in a shop with both bands would be silently attributed to
        // whichever happened to sort first — corrupting the exact figure the split exists to
        // produce. Unresolvable means unclassified, which is visible.
        Assert.Null(VatAccounting.BandFor(WithExempt, 0));

        // With only one band at 0%, the rate IS a complete answer.
        Assert.Equal("zero", VatAccounting.BandFor(WithoutExempt, 0)?.Key);
    }

    [Fact]
    public void An_unambiguous_band_still_snaps_including_a_wobbled_till_rate()
    {
        // Adding an exempt band must not disturb ordinary trade: a real £14.99/£12.49 line declares
        // 2002bp and still belongs to the 20% band.
        Assert.Equal("standard", VatAccounting.BandFor(WithExempt, 2002)?.Key);
        Assert.Equal("standard", VatAccounting.BandFor(WithExempt, 1993)?.Key);
        // …and genuinely off-band takings are still nobody's band.
        Assert.Null(VatAccounting.BandFor(WithExempt, 2500));
    }

    // ── resolving a legacy tax row to a band ──────────────────────────────────────────────────

    [Fact]
    public void A_tax_row_resolves_by_rate_when_that_is_unambiguous()
    {
        Assert.Equal("standard", VatBandResolution.Resolve(WithoutExempt, null, 2000));
        Assert.Equal("zero", VatBandResolution.Resolve(WithoutExempt, null, 0));
    }

    [Fact]
    public void A_tax_row_at_zero_percent_needs_a_HUMAN_once_exempt_exists()
    {
        // Null is the whole point: "I cannot know, and I will not guess."
        Assert.Null(VatBandResolution.Resolve(WithExempt, null, 0));
        // …and the explicit mapping is what answers it.
        Assert.Equal("exempt", VatBandResolution.Resolve(WithExempt, "exempt", 0));
        Assert.Equal("zero", VatBandResolution.Resolve(WithExempt, "zero", 0));
    }

    [Fact]
    public void An_explicit_mapping_beats_the_rate_even_when_the_rate_is_unambiguous()
    {
        // The owner's statement always wins — that is what makes it a decision rather than a hint.
        Assert.Equal("exempt", VatBandResolution.Resolve(WithoutExempt, "exempt", 0));
    }

    [Fact]
    public void Mapping_is_only_DEMANDED_when_two_bands_share_a_rate()
    {
        // A shop with no exempt band is never nagged: the rate answers everything.
        Assert.False(VatBandResolution.NeedsExplicitMapping(WithoutExempt));
        Assert.True(VatBandResolution.NeedsExplicitMapping(WithExempt));
        // A single band, or none, cannot be ambiguous.
        Assert.False(VatBandResolution.NeedsExplicitMapping(new[] { Standard }));
        Assert.False(VatBandResolution.NeedsExplicitMapping(Array.Empty<VatBand>()));
    }

    [Fact]
    public void Two_bands_within_the_snap_tolerance_are_ambiguous_even_though_their_rates_differ()
    {
        // Not just an exact tie: two bands closer together than the tolerance cannot be told apart
        // by a till-derived rate either. (A fixed-width bucketing would miss 1990 vs 2010.)
        var near = new[] { new VatBand("a", "A", VatClass.Standard, 1990, DateTime.UnixEpoch),
                           new VatBand("b", "B", VatClass.Standard, 2010, DateTime.UnixEpoch) };
        Assert.True(VatBandResolution.NeedsExplicitMapping(near));
        Assert.Null(VatAccounting.BandFor(near, 2000));
    }

    // ── the rollup grain ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rollups_key_on_the_BAND_so_zero_and_exempt_takings_never_merge()
    {
        // ⚠ THE PLACE THE DISTINCTION WOULD BE LOST FOREVER. Both lines are 0bp; keyed on the rate
        // alone they become one bucket, and after that no report can separate them without
        // replaying every sale line.
        var lines = new List<SaleLine>
        {
            new() { VatRateBp = 0, VatBand = "zero",   LineGrossPence = 800, VatAmountPence = 0 },
            new() { VatRateBp = 0, VatBand = "exempt", LineGrossPence = 500, VatAmountPence = 0 },
            new() { VatRateBp = 2002, VatBand = "standard", LineGrossPence = 1499, VatAmountPence = 250 },
        };

        var byBand = RollupProjectionConsumer.VatByRateAndBand(lines);

        Assert.Equal(3, byBand.Count);
        Assert.Equal(800, byBand[(0, "zero")].Gross);
        Assert.Equal(500, byBand[(0, "exempt")].Gross);
        Assert.Equal(1499, byBand[(2002, "standard")].Gross);
    }

    [Fact]
    public void Lines_with_no_band_group_together_and_do_not_contaminate_a_named_band()
    {
        // Every line recorded before this shipped has a null band, as does any till that doesn't
        // send one. They must stay their own bucket — merging them into "zero" would assert
        // something nobody recorded.
        var lines = new List<SaleLine>
        {
            new() { VatRateBp = 0, VatBand = null,   LineGrossPence = 300, VatAmountPence = 0 },
            new() { VatRateBp = 0, VatBand = "zero", LineGrossPence = 700, VatAmountPence = 0 },
        };

        var byBand = RollupProjectionConsumer.VatByRateAndBand(lines);

        Assert.Equal(2, byBand.Count);
        Assert.Equal(300, byBand[(0, null)].Gross);
        Assert.Equal(700, byBand[(0, "zero")].Gross);
    }

    // ── output tax is unaffected: both are 0% ─────────────────────────────────────────────────

    [Fact]
    public void Neither_zero_rated_nor_exempt_produces_output_tax_the_difference_is_RECOVERY()
    {
        // Worth stating explicitly, because it is why the mistake is invisible on a receipt and on
        // Box 1: reclassifying moves no output tax at all. Only input-tax recovery changes.
        Assert.Equal(0, VatAccounting.OutputTaxOn(40_364_972, 0));
        Assert.Equal(Zero.RateBp, Exempt.RateBp);
        Assert.NotEqual(Zero.Class, Exempt.Class);
    }

    [Fact]
    public void The_snap_tolerance_is_ONE_number_shared_by_every_consumer()
    {
        // Two tolerances would disagree about which takings belong where — the band-snap, the
        // tax-row mapping guard and the reports all have to use the same figure.
        Assert.Equal(25, VatAccounting.BandSnapToleranceBp);
        // A band exactly at the tolerance is in; one basis point beyond is out.
        Assert.Equal("standard", VatAccounting.BandFor(WithoutExempt, 2000 + VatAccounting.BandSnapToleranceBp)?.Key);
        Assert.Null(VatAccounting.BandFor(WithoutExempt, 2000 + VatAccounting.BandSnapToleranceBp + 1));
    }
}
