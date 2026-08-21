using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;

namespace Plutus.Client.Storage;

/// <summary>
/// Where a till keeps the shop's discount rules: one Meta row, holding the wire payload verbatim.
///
/// ⚠ STORED AS THE RAW PAYLOAD, deliberately — the <see cref="MetaVatBandStore"/> argument, applied
/// to a second feed. Re-shaping the rules into local tables would mean this file deciding which parts
/// of a promotion matter, and the parts that look redundant are the ones that matter: the day mask
/// and the window are what let an OFFLINE till start discounting on Wednesday morning and stop on
/// Wednesday night. Keeping the payload whole also means a till caches fields added after this build
/// shipped, instead of silently dropping them.
/// </summary>
public sealed class MetaDiscountRuleStore : IDiscountRuleStore
{
    private readonly TillStore _store;

    public MetaDiscountRuleStore(TillStore store) => _store = store;

    /// <summary>
    /// The cached rules, or null when this till has never been told them.
    ///
    /// ⚠ NULL HERE IS SAFE, unlike the VAT bands. An uninformed till charges the shelf price, which
    /// is what a shop with no promotions charges anyway — so <see cref="DiscountRuleCache.RulesAsync"/>
    /// flattens it to an empty list rather than making every caller handle the difference.
    /// </summary>
    public async Task<DiscountRulesResult?> LoadAsync(CancellationToken ct = default)
    {
        var json = await _store.GetMetaAsync(MetaKeys.DiscountRules, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<DiscountRulesResult>(json, PlutusApiClient.Json);
        }
        catch (JsonException)
        {
            // ⚠ A half-written or older-shaped blob reads as "not told yet", never as an exception out
            // of a lookup on the selling path. The next sync overwrites it.
            return null;
        }
    }

    public Task SaveAsync(DiscountRulesResult rules, CancellationToken ct = default) =>
        _store.SetMetaAsync(MetaKeys.DiscountRules, JsonSerializer.Serialize(rules, PlutusApiClient.Json), ct);
}
