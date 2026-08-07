using System;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP2b: the VAT rule for a till that was offline across a rate change. The boundary cases are
/// the whole point — a guard that fires on the wrong side of midnight is worse than no guard,
/// because it quarantines correct sales and trains everyone to ignore the queue.
/// </summary>
public class VatRateHistoryTests
{
    private static readonly DateTime Change = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    // The UK shape: zero, reduced and standard coexist, and the STANDARD BAND's rate moves from
    // 20% to 17.5% on 1 Sept — one band changing value, which must retire the old rate.
    private static readonly VatRate[] History =
    {
        new(VatRateHistory.Zero, 0, DateTime.UnixEpoch),
        new(VatRateHistory.Reduced, 500, DateTime.UnixEpoch),
        new(VatRateHistory.Standard, 2000, DateTime.UnixEpoch),
        new(VatRateHistory.Standard, 1750, Change),
    };

    [Fact]
    public void Coexisting_bands_are_all_valid_at_once()
    {
        // Not a single-rate check: a basket can legitimately carry 0%, 5% and 20% lines together.
        var before = Change.AddDays(-1);
        Assert.True(VatRateHistory.IsValidAt(History, 0, before));
        Assert.True(VatRateHistory.IsValidAt(History, 500, before));
        Assert.True(VatRateHistory.IsValidAt(History, 2000, before));
        Assert.False(VatRateHistory.IsValidAt(History, 1234, before));
    }

    [Fact]
    public void The_new_rate_is_invalid_before_it_takes_effect_and_valid_from_the_instant_it_does()
    {
        Assert.False(VatRateHistory.IsValidAt(History, 1750, Change.AddTicks(-1)));
        Assert.True(VatRateHistory.IsValidAt(History, 1750, Change));            // inclusive boundary
        Assert.True(VatRateHistory.IsValidAt(History, 1750, Change.AddDays(30)));
    }

    [Fact]
    public void A_sale_from_BEFORE_the_change_carrying_the_old_rate_is_still_valid()
    {
        // The false-positive guard. A till pushing a week-late backlog must not have its correct,
        // pre-change sales quarantined just because the rate has moved since.
        Assert.True(VatRateHistory.IsValidAt(History, 2000, Change.AddDays(-3)));
    }

    [Fact]
    public void An_offline_till_pushing_the_STALE_rate_after_the_change_is_caught()
    {
        // This is the case the whole WP exists for.
        Assert.False(VatRateHistory.IsValidAt(History, 2000, Change.AddDays(2)));
    }

    [Fact]
    public void An_empty_history_skips_validation_rather_than_quarantining_everything()
    {
        // Switching a compliance guard on must not become an outage for every unseeded tenant.
        Assert.True(VatRateHistory.IsValidAt(Array.Empty<VatRate>(), 2000, DateTime.UtcNow));
        Assert.True(VatRateHistory.IsValidAt(null!, 9999, DateTime.UtcNow));
    }

    [Fact]
    public void A_superseded_rate_is_retired_the_instant_its_replacement_lands()
    {
        // The mechanism: history is keyed by BAND, so the standard band holding 17.5% means 20%
        // is no longer in force — not "both are fine because both appear in the table".
        Assert.Equal(new[] { 0, 500, 2000 }, VatRateHistory.InForceAt(History, Change.AddDays(-1)).OrderBy(x => x));
        Assert.Equal(new[] { 0, 500, 1750 }, VatRateHistory.InForceAt(History, Change).OrderBy(x => x));
        Assert.DoesNotContain(2000, VatRateHistory.InForceAt(History, Change));
        // nothing in force before the earliest point
        Assert.Empty(VatRateHistory.InForceAt(History, DateTime.UnixEpoch.AddTicks(-1)));
    }
}
