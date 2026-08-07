using System;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP2b: the VAT rule for a till that was offline across a rate change.
///
/// ⚠ These were rewritten on 2026-08-08. The first version asserted EXACT basis-point membership,
/// which encoded a model this platform does not use: the web till derives each line's rate from
/// its price pair, so ordinary lines arrive at 1998–2002bp by design. Checking declared bp would
/// have rejected normal trade. The rule under test is the price-pair tolerance the catalogue guard
/// already applies — and the case that must NOT fire is as important as the one that must.
/// </summary>
public class VatRateHistoryTests
{
    private static readonly DateTime Change = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    // The UK shape: zero, reduced and standard coexist, and the STANDARD BAND's rate moves from
    // 20% to 17.5% on 1 Sept — one band changing value, which retires the old rate.
    private static readonly VatRate[] History =
    {
        new(VatRateHistory.Zero, 0, DateTime.UnixEpoch),
        new(VatRateHistory.Reduced, 500, DateTime.UnixEpoch),
        new(VatRateHistory.Standard, 2000, DateTime.UnixEpoch),
        new(VatRateHistory.Standard, 1750, Change),
    };

    private static VatLineAssessment Assess(long inc, long ex, DateTime at) =>
        VatRateHistory.Assess(History, inc, ex, at);

    [Fact]
    public void A_REAL_webtill_price_pair_is_consistent_even_though_its_declared_rate_wobbles()
    {
        // THE REGRESSION THIS FILE EXISTS FOR. £14.99 ex £12.49 is 20% priced to the penny, but
        // the web till ships it as 2002bp (round((14.99/12.49 − 1) × 10000)). Judged on the pair,
        // it is plainly consistent; judged on the declared rate, it would have been quarantined —
        // and every Kapow sale of that item with it.
        Assert.Equal(2002, (int)Math.Round((1499m / 1249m - 1m) * 10000m));   // what the till sends

        var verdict = Assess(1499, 1249, Change.AddDays(-1));
        Assert.Equal(VatLineVerdict.Consistent, verdict.Verdict);
        Assert.Equal(2000, verdict.ExplainedByBp);
    }

    [Theory]
    // Everyday shop pricing, all penny-rounded, all legitimately consistent with a live band.
    [InlineData(1499, 1249, 2000)]   // declared 2002
    [InlineData(999, 833, 2000)]     // declared 1993
    [InlineData(600, 500, 2000)]     // exact
    [InlineData(525, 500, 500)]      // exact 5%
    [InlineData(800, 800, 0)]        // zero-rated
    public void Ordinary_pairs_are_explained_by_a_band_in_force(long inc, long ex, int expectedBand)
    {
        var verdict = Assess(inc, ex, Change.AddDays(-1));
        Assert.Equal(VatLineVerdict.Consistent, verdict.Verdict);
        Assert.Equal(expectedBand, verdict.ExplainedByBp);
    }

    [Fact]
    public void A_till_still_pricing_at_the_OLD_standard_rate_after_the_change_is_caught()
    {
        // The case the whole mechanism exists for: same pair, now sold after the rate moved.
        var verdict = Assess(1499, 1249, Change.AddDays(2));
        Assert.Equal(VatLineVerdict.StaleBand, verdict.Verdict);
        Assert.Equal(2000, verdict.ExplainedByBp);          // named, so the reason can say so
        Assert.Contains(1750, verdict.InForceBp);           // …against what was actually in force
        Assert.DoesNotContain(2000, verdict.InForceBp);
    }

    [Fact]
    public void The_same_pair_BEFORE_the_change_is_fine_no_false_positives()
    {
        // A till draining a week-old backlog must not have its legitimate history quarantined —
        // that is what trains people to ignore a quarantine queue.
        Assert.Equal(VatLineVerdict.Consistent, Assess(1499, 1249, Change.AddDays(-3)).Verdict);
    }

    [Fact]
    public void Correctly_repriced_at_the_NEW_rate_is_fine_from_the_instant_it_takes_effect()
    {
        // £11.75 ex £10.00 = 17.5%
        Assert.Equal(VatLineVerdict.StaleBand, Assess(1175, 1000, Change.AddTicks(-1)).Verdict);
        var atChange = Assess(1175, 1000, Change);
        Assert.Equal(VatLineVerdict.Consistent, atChange.Verdict);
        Assert.Equal(1750, atChange.ExplainedByBp);
    }

    [Fact]
    public void Legacy_OFF_BAND_damage_is_accepted_not_blocked()
    {
        // Owner decision (VAT-FixLater report): off-band items keep selling and are SURFACED by
        // the VatIntegrity report. £11.00 ex £10.00 is 10% — no band of this tenant's, ever.
        var verdict = Assess(1100, 1000, Change.AddDays(-1));
        Assert.Equal(VatLineVerdict.OffBand, verdict.Verdict);
        Assert.Null(verdict.ExplainedByBp);
    }

    [Fact]
    public void The_tolerance_matches_the_catalogue_guard_exactly()
    {
        // Same 2p the portal's item editor enforces — an item the portal accepts can never be a
        // sale ingest refuses.
        Assert.True(VatRateHistory.Explains(1200, 1000, 2000));        // exact
        Assert.True(VatRateHistory.Explains(1202, 1000, 2000));        // +2p, the boundary
        Assert.True(VatRateHistory.Explains(1198, 1000, 2000));        // −2p
        Assert.False(VatRateHistory.Explains(1203, 1000, 2000));       // 3p out
    }

    [Fact]
    public void An_empty_history_or_an_unusable_pair_is_skipped_never_quarantined()
    {
        // Turning a compliance guard on must not become an outage for unseeded tenants.
        Assert.Equal(VatLineVerdict.Consistent,
            VatRateHistory.Assess(Array.Empty<VatRate>(), 1499, 1249, DateTime.UtcNow).Verdict);
        Assert.Equal(VatLineVerdict.Consistent,
            VatRateHistory.Assess(null!, 1499, 1249, DateTime.UtcNow).Verdict);
        // a free line, or metadata we could not read — no pair to reason about, so don't guess
        Assert.Equal(VatLineVerdict.Consistent, Assess(0, 0, Change.AddDays(2)).Verdict);
    }

    [Fact]
    public void A_superseded_rate_is_retired_the_instant_its_replacement_lands()
    {
        // The mechanism: history is keyed by BAND, so the standard band holding 17.5% means 20%
        // is no longer in force — not "both are fine because both appear in the table".
        Assert.Equal(new[] { 0, 500, 2000 }, VatRateHistory.InForceAt(History, Change.AddDays(-1)).OrderBy(x => x));
        Assert.Equal(new[] { 0, 500, 1750 }, VatRateHistory.InForceAt(History, Change).OrderBy(x => x));
        Assert.DoesNotContain(2000, VatRateHistory.InForceAt(History, Change));
        Assert.Empty(VatRateHistory.InForceAt(History, DateTime.UnixEpoch.AddTicks(-1)));

        // …but it is still one of the tenant's OWN rates, which is how a stale band is told apart
        // from off-band damage.
        Assert.Contains(2000, VatRateHistory.EverKnown(History));
    }
}
