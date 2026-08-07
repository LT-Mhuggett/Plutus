using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>
/// One VAT band's rate, in force from a date until a later point for the SAME band supersedes it.
///
/// ⚠ The band — not the rate — is the identity. When the standard rate moves 20% → 17.5% that is
/// one band changing value, so 20% must STOP being in force; modelling history as a bag of rates
/// leaves the old rate live forever, which defeats the entire check.
/// </summary>
public readonly record struct VatRate(string Band, int RateBp, DateTime EffectiveFromUtc);

/// <summary>What a sale line's price pair says about the VAT it was rung up at.</summary>
public enum VatLineVerdict
{
    /// <summary>A rate in force at the time of sale explains the price pair. Normal trade.</summary>
    Consistent,
    /// <summary>Explained ONLY by a rate of this tenant's own bands that was NOT in force then —
    /// a retired rate, or one not yet effective. This is a till trading on a stale cached band,
    /// which is the case this whole mechanism exists to catch.</summary>
    StaleBand,
    /// <summary>No band of this tenant's explains it, in force or not. That is legacy off-band
    /// damage — deliberately NOT blocked (owner decision, VAT-FixLater report); the VatIntegrity
    /// report is where it is seen and corrected.</summary>
    OffBand,
}

/// <summary>The verdict plus what explained it, so a quarantine reason can name both rates.</summary>
public readonly record struct VatLineAssessment(
    VatLineVerdict Verdict, int? ExplainedByBp, IReadOnlySet<int> InForceBp);

/// <summary>
/// MAUI retrofit WP2b — VAT-rate-change compliance for offline tills.
///
/// THE PROBLEM: a till offline across a government rate change keeps pricing at the rate it last
/// cached. When it reconnects it pushes a backlog priced at, say, 20% on days when the legal
/// standard rate had become 17.5%. Accepting silently files an incorrect return; rewriting
/// silently changes what the customer was actually charged, which is worse. So: quarantine.
///
/// ⚠ THE CHECK IS ON THE PRICE PAIR, NOT THE DECLARED RATE. This is the correction of 2026-08-08.
/// The web till derives each line's `vatRateBp` from its price pair
/// (`round((inc/ex − 1) × 10000)`, api.ts), so ordinary lines legitimately arrive at 1998–2002bp
/// — its own FE7 comment says exactly that. Comparing declared bp against a set of clean band
/// values therefore rejects normal trade. The platform's canonical rule is the one the catalogue
/// guard already uses: <c>|inc − round(ex × rate)| ≤ 2p</c>
/// (<c>ItemController.BandInconsistency</c>). We use the same rule and the same tolerance.
///
/// Pure and clock-free: the caller supplies the history, the pair and the timestamp, so every
/// boundary case is testable without a database or waiting for a date.
/// </summary>
public static class VatRateHistory
{
    /// <summary>Standard band keys. Tenants may add their own; nothing here is closed.</summary>
    public const string Standard = "standard";
    public const string Reduced = "reduced";
    public const string Zero = "zero";

    /// <summary>The catalogue guard's tolerance, in pence — kept identical on purpose so an item
    /// the portal accepts can never be a sale ingest refuses.</summary>
    public const long TolerancePence = 2;

    /// <summary>The rates in force at <paramref name="atUtc"/>: for each band, the value from its
    /// most recent point at or before that instant. A superseded rate is absent — that is the
    /// whole mechanism.</summary>
    public static IReadOnlySet<int> InForceAt(IEnumerable<VatRate> history, DateTime atUtc) =>
        (history ?? Enumerable.Empty<VatRate>())
            .Where(p => p.EffectiveFromUtc <= atUtc)
            .GroupBy(p => p.Band, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.EffectiveFromUtc).First().RateBp)
            .ToHashSet();

    /// <summary>Every rate this tenant has ever had, in force or not — used to tell "stale band"
    /// (their own rate, wrong moment) from "off-band" (never one of their rates at all).</summary>
    public static IReadOnlySet<int> EverKnown(IEnumerable<VatRate> history) =>
        (history ?? Enumerable.Empty<VatRate>()).Select(p => p.RateBp).ToHashSet();

    /// <summary>
    /// Does <paramref name="rateBp"/> explain this price pair, within the catalogue guard's 2p?
    /// Mirrors <c>ItemController.BandInconsistency</c>: expected = round(ex × (1 + bp/10000)).
    /// </summary>
    public static bool Explains(long unitIncPence, long unitExPence, int rateBp, long tolerancePence = TolerancePence)
    {
        var expected = (long)Math.Round(unitExPence * (1m + rateBp / 10000m), MidpointRounding.AwayFromZero);
        return Math.Abs(unitIncPence - expected) <= tolerancePence;
    }

    /// <summary>
    /// Assess one line's price pair against the tenant's band history at the moment of sale.
    ///
    /// ⚠ An EMPTY history returns Consistent — deliberately. Turning a compliance guard on must
    /// not quarantine every sale from every tenant whose bands nobody has configured yet; that
    /// converts a safeguard into an outage. The caller logs the skip.
    ///
    /// A non-positive ex price (a free line, or metadata we cannot read) is also Consistent:
    /// there is no pair to reason about, and guessing is worse than passing.
    /// </summary>
    public static VatLineAssessment Assess(
        IEnumerable<VatRate> history, long unitIncPence, long unitExPence, DateTime atUtc,
        long tolerancePence = TolerancePence)
    {
        var all = (history ?? Enumerable.Empty<VatRate>()).ToList();
        var inForce = InForceAt(all, atUtc);
        if (all.Count == 0 || inForce.Count == 0 || unitExPence <= 0 || unitIncPence <= 0)
            return new VatLineAssessment(VatLineVerdict.Consistent, null, inForce);

        foreach (var bp in inForce.OrderBy(b => b))
            if (Explains(unitIncPence, unitExPence, bp, tolerancePence))
                return new VatLineAssessment(VatLineVerdict.Consistent, bp, inForce);

        // Not explained by anything current. Was it one of this tenant's OWN rates at some other
        // time? That is a till pricing on a stale band — the real target of this check.
        foreach (var bp in EverKnown(all).Except(inForce).OrderBy(b => b))
            if (Explains(unitIncPence, unitExPence, bp, tolerancePence))
                return new VatLineAssessment(VatLineVerdict.StaleBand, bp, inForce);

        // Explained by no band of theirs at all: legacy off-band damage, which the owner decided
        // is surfaced by the VatIntegrity report and never blocks trading.
        return new VatLineAssessment(VatLineVerdict.OffBand, null, inForce);
    }
}
