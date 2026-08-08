using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Effective-price resolution: **store override → company price list → legacy baseline**.
///
/// ⚠ Shared between the server and every till because a till trading offline answers this itself,
/// at the sale's instant. A second implementation is two tills quoting different prices for the
/// same barcode on the same day — which a customer finds before anyone else does.
/// </summary>
public class PriceResolutionTests
{
    private static readonly DateTime Thu = new(2026, 8, 6, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Sun = new(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc);

    private static PricePoint P(long inc, long ex, DateTime from, DateTime? created = null) =>
        new(inc, ex, from, created ?? from);

    // ── the headline: scheduled repricing ──

    [Fact]
    public void A_SUNDAY_night_reprice_scheduled_on_THURSDAY_activates_at_the_boundary()
    {
        // ⚠ THE WHOLE REASON THE TILL HOLDS A TIMELINE. HQ schedules it on Thursday; the till syncs
        // Thursday and is never online again. It must charge the old price on Saturday and the new
        // one on Monday, entirely on its own.
        var points = new[] { P(1499, 1249, Thu.AddDays(-30)), P(1699, 1416, Sun) };

        Assert.Equal(1499, Resolve(points, Thu)!.PricePence);
        Assert.Equal(1499, Resolve(points, Sun.AddSeconds(-1))!.PricePence);
        Assert.Equal(1699, Resolve(points, Sun)!.PricePence);
        Assert.Equal(1699, Resolve(points, Sun.AddDays(1))!.PricePence);
    }

    [Fact]
    public void A_FUTURE_point_is_invisible_until_its_moment()
    {
        // Cached deliberately, and it must not leak early — an item priced ahead of time would ring
        // up at next week's price today.
        var points = new[] { P(1000, 833, Thu), P(2000, 1667, Sun) };
        Assert.Equal(1000, Resolve(points, Thu.AddHours(1))!.PricePence);
    }

    [Fact]
    public void Two_points_on_the_SAME_instant_break_the_tie_by_when_they_were_entered()
    {
        // Repricing APPENDS rather than editing, so same-instant duplicates are normal — a
        // correction entered after a mistake must win.
        var points = new[]
        {
            P(1000, 833, Sun, created: Thu),
            P(1100, 917, Sun, created: Thu.AddHours(2)),
        };
        Assert.Equal(1100, Resolve(points, Sun)!.PricePence);
    }

    // ── precedence ──

    [Fact]
    public void A_store_override_beats_the_central_list()
    {
        var result = PriceResolution.Resolve(
            PriceOwner.CentralWithOverride,
            new[] { P(1000, 833, Thu) },
            new[] { P(1200, 1000, Thu) },
            999, 832, Sun);

        Assert.Equal(1200, result!.PricePence);
        Assert.Equal(PriceResolution.SourceOverride, result.Source);
    }

    [Fact]
    public void Under_CENTRAL_policy_a_store_override_is_IGNORED()
    {
        // ⚠ Not merely absent — ignored. Under Central the stores are read-only, so an override row
        // that exists (from before the policy changed) must not quietly keep applying.
        var result = PriceResolution.Resolve(
            PriceOwner.Central,
            new[] { P(1000, 833, Thu) },
            new[] { P(1200, 1000, Thu) },
            999, 832, Sun);

        Assert.Equal(1000, result!.PricePence);
        Assert.Equal(PriceResolution.SourceCentral, result.Source);
    }

    [Fact]
    public void Under_LOCAL_policy_the_store_price_is_THE_price_and_says_so()
    {
        var result = PriceResolution.Resolve(
            PriceOwner.Local, new[] { P(1000, 833, Thu) }, new[] { P(1200, 1000, Thu) }, 999, 832, Sun);

        Assert.Equal(PriceResolution.SourceLocal, result!.Source);
    }

    [Fact]
    public void With_no_price_points_at_all_the_legacy_catalogue_price_is_used()
    {
        // The evolve-in-place baseline: an item that has never been repriced still has a price.
        var result = PriceResolution.Resolve(PriceOwner.Central, null, null, 1499, 1249, Sun);

        Assert.Equal(1499, result!.PricePence);
        Assert.Equal(1249, result.ExPricePence);
        Assert.Equal(PriceResolution.SourceLegacy, result.Source);
    }

    [Fact]
    public void An_item_with_nothing_at_all_resolves_to_NULL_not_to_zero()
    {
        // ⚠ Zero would ring up as free. Null makes the caller decide, which is the only safe
        // default for a number that is about to be charged to somebody.
        Assert.Null(PriceResolution.Resolve(PriceOwner.Central, null, null, null, null, Sun));
    }

    [Fact]
    public void Before_ANY_point_has_started_the_baseline_still_applies()
    {
        var result = PriceResolution.Resolve(
            PriceOwner.Central, new[] { P(2000, 1667, Sun) }, null, 1499, 1249, Thu);

        Assert.Equal(1499, result!.PricePence);
        Assert.Equal(PriceResolution.SourceLegacy, result.Source);
    }

    [Fact]
    public void The_PAIR_travels_together()
    {
        // A line's vatRateBp is derived from inc/ex, so an ex-price belonging to a different
        // inc-price is a wrong VAT figure on a receipt the customer is holding.
        var result = PriceResolution.Resolve(
            PriceOwner.Central, new[] { P(1499, 1249, Thu) }, null, 1, 1, Sun);

        Assert.Equal(1499, result!.PricePence);
        Assert.Equal(1249, result.ExPricePence);
    }

    private static ResolvedPrice? Resolve(PricePoint[] central, DateTime at) =>
        PriceResolution.Resolve(PriceOwner.Central, central, null, null, null, at);
}
