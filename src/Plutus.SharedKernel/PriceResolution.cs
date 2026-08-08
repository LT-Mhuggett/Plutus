using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>Who owns an item's price. Mirrors <c>Plutus.Entities.Models.PricePolicy</c>; declared
/// here so a till can reason about pricing without referencing a backend entity.</summary>
public enum PriceOwner : byte
{
    /// <summary>HQ sets it; stores are read-only.</summary>
    Central = 0,
    /// <summary>HQ default, but a store override survives HQ repricing until force-reset.</summary>
    CentralWithOverride = 1,
    /// <summary>The store owns it; HQ sees but does not set.</summary>
    Local = 2,
}

/// <summary>
/// One dated price. ⚠ Both halves of the PAIR travel together — the till derives a line's
/// <c>vatRateBp</c> from <c>inc/ex</c>, so an ex-price that does not belong to its inc-price is a
/// VAT figure that is wrong on the receipt the customer is holding.
/// </summary>
/// <param name="CreatedAtUtc">Breaks ties when two points share an effective date: the one entered
/// later wins. Repricing APPENDS rather than editing, so same-instant duplicates are normal.</param>
public sealed record PricePoint(
    long PricePence,
    long ExPricePence,
    DateTime EffectiveFromUtc,
    DateTime CreatedAtUtc);

/// <summary>What an item actually costs at a moment, and where the number came from.</summary>
public sealed record ResolvedPrice(long PricePence, long ExPricePence, string Source);

/// <summary>
/// Effective-price resolution: **store override → company price list → the legacy baseline**.
///
/// ⚠ WHY THIS IS IN SHAREDKERNEL. A till that trades offline has to answer this itself, at the
/// SALE's instant — and it has to get the same answer the server would. A second implementation is
/// two tills quoting different prices for the same barcode on the same day, which a customer sees
/// before anyone else does.
///
/// ⚠ EFFECTIVE DATING IS THE POINT. The latest point whose <c>EffectiveFromUtc</c> has passed wins,
/// so a Sunday-night reprice scheduled on Thursday activates at the boundary — on a till that has
/// been offline the whole time, with nobody touching it.
/// </summary>
public static class PriceResolution
{
    public const string SourceOverride = "override";
    public const string SourceLocal = "local";
    public const string SourceCentral = "central";
    public const string SourceLegacy = "legacy";

    /// <summary>
    /// Resolve at <paramref name="atUtc"/>.
    /// </summary>
    /// <param name="storeOverrides">This STORE's non-revoked overrides. ⚠ The caller filters by
    /// store and by revocation: a revoked override is not a price, and another store's override is
    /// not this store's business.</param>
    /// <param name="legacyPricePence">The evolve-in-place baseline from the catalogue row, used
    /// until a first price-list entry exists. Null when the item is unknown.</param>
    public static ResolvedPrice? Resolve(
        PriceOwner policy,
        IEnumerable<PricePoint>? centralPoints,
        IEnumerable<PricePoint>? storeOverrides,
        long? legacyPricePence,
        long? legacyExPricePence,
        DateTime atUtc)
    {
        // ⚠ Only points that have COME INTO FORCE. A future-dated point is cached deliberately and
        // must stay invisible until its moment — that is the whole reason the till holds a timeline
        // rather than a number.
        var central = Latest(centralPoints, atUtc);

        // A store override only counts when the policy lets the store own or amend the price.
        var store = policy == PriceOwner.Central ? null : Latest(storeOverrides, atUtc);

        if (store != null)
            return new ResolvedPrice(store.PricePence, store.ExPricePence,
                policy == PriceOwner.Local ? SourceLocal : SourceOverride);

        if (central != null)
            return new ResolvedPrice(central.PricePence, central.ExPricePence, SourceCentral);

        return legacyPricePence is long p && legacyExPricePence is long ex
            ? new ResolvedPrice(p, ex, SourceLegacy)
            : null;
    }

    /// <summary>The point in force at <paramref name="atUtc"/> — latest effective date, then latest
    /// entry. Null when none has started yet.</summary>
    public static PricePoint? Latest(IEnumerable<PricePoint>? points, DateTime atUtc) =>
        points?
            .Where(p => p.EffectiveFromUtc <= atUtc)
            .OrderByDescending(p => p.EffectiveFromUtc)
            .ThenByDescending(p => p.CreatedAtUtc)
            .FirstOrDefault();
}
