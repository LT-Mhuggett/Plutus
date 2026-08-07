using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>
/// One VAT band's rate, in force from a date until a later point for the SAME band supersedes it.
///
/// ⚠ The band — not the rate — is the identity. When the standard rate moves 20% → 17.5% that is
/// one band changing value, so 20% must STOP being valid; modelling history as a bag of rates
/// leaves the old rate legal forever, which defeats the entire check.
/// </summary>
public readonly record struct VatRate(string Band, int RateBp, DateTime EffectiveFromUtc);

/// <summary>
/// MAUI retrofit WP2b — VAT-rate-change compliance for offline tills.
///
/// THE PROBLEM: a till offline across a government rate change keeps selling at the rate it last
/// cached. When it reconnects it pushes sales rung up at, say, 20% on a day when the legal rate
/// had become 17.5%. Accepting those silently files an incorrect VAT return; rewriting them
/// silently changes what the customer was actually charged, which is worse. So: quarantine.
///
/// THE RULE: a line is valid if its rate is the CURRENT rate of some band at the moment of sale.
/// Note "some band" — 0%, 5% and 20% coexist legitimately (zero-rated books beside standard-rated
/// toys), so this is set membership at a point in time, never a single-rate comparison.
///
/// Pure and clock-free: the caller supplies the history and the timestamp, so every boundary case
/// is testable without a database or waiting for a date.
/// </summary>
public static class VatRateHistory
{
    /// <summary>Standard band keys. Tenants may add their own; nothing here is closed.</summary>
    public const string Standard = "standard";
    public const string Reduced = "reduced";
    public const string Zero = "zero";

    /// <summary>The rates in force at <paramref name="atUtc"/>: for each band, the value from its
    /// most recent point at or before that instant. A superseded rate is absent — that is the
    /// whole mechanism.</summary>
    public static IReadOnlySet<int> InForceAt(IEnumerable<VatRate> history, DateTime atUtc) =>
        (history ?? Enumerable.Empty<VatRate>())
            .Where(p => p.EffectiveFromUtc <= atUtc)
            .GroupBy(p => p.Band, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.EffectiveFromUtc).First().RateBp)
            .ToHashSet();

    /// <summary>
    /// Is <paramref name="rateBp"/> legitimate for a sale that happened at <paramref name="atUtc"/>?
    ///
    /// ⚠ An EMPTY history means "this tenant has never had rates configured", and returns true —
    /// deliberately. The alternative is that switching this on quarantines every sale from every
    /// unseeded tenant, turning a compliance guard into an outage. The caller logs the skip.
    /// </summary>
    public static bool IsValidAt(IEnumerable<VatRate> history, int rateBp, DateTime atUtc)
    {
        var live = InForceAt(history, atUtc);
        return live.Count == 0 || live.Contains(rateBp);
    }
}
