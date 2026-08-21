using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;

namespace Plutus.Client.Core;

/// <summary>Where a till keeps the portal's discount rules. Platform-specific storage, shared
/// rules — the same split as <see cref="IVatBandStore"/> and <see cref="IOperatorStore"/>.</summary>
public interface IDiscountRuleStore
{
    Task<DiscountRulesResult?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(DiscountRulesResult rules, CancellationToken ct = default);
}

/// <summary>
/// The till's copy of the shop's scheduled discount rules.
///
/// ⚠⚠ THE WHOLE RULE IS CACHED, SCHEDULE AND ALL — never "the discounts that apply today", and that
/// is the entire point of this class, exactly as it is for <see cref="VatBandCache"/>. A till that
/// stored today's answer and then went offline would apply Monday's discounts all week: no Wednesday
/// discount on Wednesday, or a Wednesday discount every day. With the raw schedule it starts and
/// stops on the right day, offline, with nobody touching it.
///
/// ⚠ NO TILL MAY HOLD A DISCOUNT OF ITS OWN. This class holds no rules; it holds what the portal
/// said — the same discipline as the VAT bands, and for the same reason: two tills that disagree
/// about a promotion charge two prices for one basket.
/// </summary>
public sealed class DiscountRuleCache
{
    private readonly PlutusApiClient _api;
    private readonly IDiscountRuleStore _store;

    public DiscountRuleCache(PlutusApiClient api, IDiscountRuleStore store)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Pull the published rules and cache them. Returns how many the till now holds, or null if the
    /// server could not be asked.
    ///
    /// ⚠ ON FAILURE THE EXISTING CACHE IS KEPT — the VAT-band rule, for a softer version of the same
    /// reason. Dropping the rules because the wifi blinked would charge full price on every basket
    /// until the next successful sync, and nothing on the screen would say why.
    /// </summary>
    public async Task<int?> RefreshAsync(CancellationToken ct = default)
    {
        var rules = await _api.GetDiscountRulesAsync(ct).ConfigureAwait(false);
        if (rules is null) return null;

        await _store.SaveAsync(rules, ct).ConfigureAwait(false);
        return rules.Rules?.Length ?? 0;
    }

    /// <summary>
    /// The cached rules, in the shape the shared engine takes.
    ///
    /// ⚠ EMPTY, NEVER NULL. "This till has not been told any rules" and "this shop has no rules" mean
    /// the same thing at a counter — full price — so there is nothing for a caller to tell apart, and
    /// an empty list is the shape that cannot be dereferenced wrongly on the selling path. That is
    /// the OPPOSITE of the VAT bands, where null must stay null because an uninformed till rendered
    /// as 0% would put a wrong figure on a VAT return. A missing discount charges the shelf price.
    /// </summary>
    public async Task<IReadOnlyList<ScheduledDiscount>> RulesAsync(CancellationToken ct = default)
    {
        var cached = await _store.LoadAsync(ct).ConfigureAwait(false);
        if (cached?.Rules is null || cached.Rules.Length == 0) return Array.Empty<ScheduledDiscount>();

        return cached.Rules.Select(ToRule).ToList();
    }

    /// <summary>
    /// ⚠ The wire DTO and the shared rule are separate types ON PURPOSE, and this is the one seam
    /// between them — the same arrangement as <c>OperatorGrantDto</c> → <c>PermissionGrant</c>.
    /// `Plutus.Contracts.Client` ships onto tills and may reference nothing, so it cannot hold the
    /// engine; `SharedKernel` holds the engine and may reference nothing, so it cannot hold the wire.
    /// </summary>
    private static ScheduledDiscount ToRule(DiscountRuleDto d) => new(
        d.Id,
        d.Name ?? string.Empty,
        d.Type,
        d.PercentFraction,
        d.FixedAmountPence,
        d.AutoApply,
        d.AllApplicable,
        d.DaysOfWeekMask,
        d.WindowStartLocal,
        d.WindowEndLocal,
        d.ValidFromUtc,
        d.ValidToUtc,
        d.CategoryIds ?? Array.Empty<Guid>(),
        d.ItemIdOnes ?? Array.Empty<string>());
}
