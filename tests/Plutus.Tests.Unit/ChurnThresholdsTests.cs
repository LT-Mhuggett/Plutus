using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP16.1/16.2 churn + renewal thresholds tested at their boundaries — the arithmetic that decides
/// whether a signal fires, independent of any database.
/// </summary>
public class ChurnThresholdsTests
{
    [Theory]
    [InlineData(100, 100, false)]  // flat — not declining
    [InlineData(70, 100, false)]   // down exactly 30% — NOT more than 30%, so no signal
    [InlineData(69, 100, true)]    // down 31% — declining
    [InlineData(0, 100, true)]     // fell off a cliff
    [InlineData(50, 0, false)]     // no prior baseline — never flag (new tenant)
    [InlineData(0, 0, false)]      // no activity either side
    public void IsDeclining_at_the_30pct_boundary(long current, long prior, bool expected) =>
        Assert.Equal(expected, ChurnThresholds.IsDeclining(current, prior));

    [Theory]
    [InlineData(13, false)]
    [InlineData(14, false)]  // exactly 14 days silent is still OK (strictly greater-than)
    [InlineData(15, true)]   // 15 days → gone quiet
    public void IsGoneQuiet_at_the_14_day_boundary(int days, bool expected) =>
        Assert.Equal(expected, ChurnThresholds.IsGoneQuiet(days));

    [Theory]
    [InlineData(90, null)]  // further out than 60 → not due
    [InlineData(61, null)]
    [InlineData(60, 60)]    // crosses the 60-day threshold
    [InlineData(31, 60)]
    [InlineData(30, 30)]    // escalates at 30
    [InlineData(8, 30)]
    [InlineData(7, 7)]      // escalates at 7
    [InlineData(0, 7)]      // due today
    [InlineData(-1, null)]  // already past — cleared
    public void RenewalThresholdCrossed_escalates(int daysUntil, int? expected) =>
        Assert.Equal(expected, ChurnThresholds.RenewalThresholdCrossed(daysUntil));
}
