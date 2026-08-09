using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;

namespace Plutus.Client.Storage;

/// <summary>
/// Where a till keeps the portal's VAT bands: one Meta row, holding the wire payload verbatim.
///
/// ⚠ THE ONLY PRODUCTION IMPLEMENTATION OF <see cref="IVatBandStore"/>. Until this existed the
/// interface had exactly one implementation in the whole repo — a fake inside `VatBandCacheTests`
/// — so `VatBandCache` was fully built, fully tested, and could not be constructed by any till.
/// Another instance of the pattern that has run through this retrofit: a tested component is not a
/// working feature until something can actually use it.
///
/// ⚠ STORED AS THE RAW PAYLOAD, deliberately. Re-shaping the bands into local tables would mean
/// this file deciding which parts of a VAT rule matter — and the parts that look redundant are the
/// ones that matter: the FUTURE-dated points (a till applies a rate change on the day while
/// offline), and the band identity that separates zero-rated from exempt at the same 0%. Keeping
/// the payload whole means a till caches everything the portal published, including fields added
/// after this build shipped.
/// </summary>
public sealed class MetaVatBandStore : IVatBandStore
{
    private readonly TillStore _store;

    public MetaVatBandStore(TillStore store) => _store = store;

    /// <summary>
    /// The cached bands, or null when this till has never been told them.
    ///
    /// ⚠ NULL IS NOT "no VAT". A till that has never synced is UNINFORMED, and
    /// <see cref="VatBandCache.HasBandsAsync"/> exists so callers can tell the two apart — treating
    /// an uninformed till as zero-rated would put 0% on every line of a real sale.
    /// </summary>
    public async Task<VatBandsResult?> LoadAsync(CancellationToken ct = default)
    {
        var json = await _store.GetMetaAsync(MetaKeys.VatBands, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<VatBandsResult>(json, PlutusApiClient.Json);
        }
        catch (JsonException)
        {
            // ⚠ A half-written or older-shaped blob reads as "not told yet", never as an exception
            // out of a lookup on the selling path. The next sync overwrites it.
            return null;
        }
    }

    public Task SaveAsync(VatBandsResult bands, CancellationToken ct = default) =>
        _store.SetMetaAsync(MetaKeys.VatBands, JsonSerializer.Serialize(bands, PlutusApiClient.Json), ct);
}
