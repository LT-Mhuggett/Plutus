using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Contracts.Client;

namespace Plutus.Client.Core;

/// <summary>Where a till keeps the portal's published VAT bands. Platform-specific storage, shared
/// rules — same split as <see cref="IOperatorStore"/>.</summary>
public interface IVatBandStore
{
    Task<VatBandsResult?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(VatBandsResult bands, CancellationToken ct = default);
}

/// <summary>
/// The till's copy of the portal's VAT bands.
///
/// ⚠ THE WHOLE TIMELINE IS CACHED, NOT TODAY'S RATE, and that is the entire point of this class.
/// `GET /api/v1/vat/bands` ships every dated point including future ones. A till that stored only
/// "the standard rate is 20%" and then went offline across a rate change would keep charging the
/// old rate — and WP2b's ingest check would quarantine its whole backlog on reconnect. With the
/// timeline it applies the change on the day, offline, with nobody touching it.
///
/// ⚠ NO TILL MAY HOLD A HARD-CODED VAT RATE — pinned platform-wide by
/// <c>Till_libraries_stay_platform_neutral_and_hold_no_VAT_rates_of_their_own</c>. This class holds
/// no rates; it holds what the portal said.
/// </summary>
public sealed class VatBandCache
{
    private readonly PlutusApiClient _api;
    private readonly IVatBandStore _store;

    public VatBandCache(PlutusApiClient api, IVatBandStore store)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Pull the published bands and cache them. Returns how many bands the till now holds, or null
    /// if the server could not be asked.
    ///
    /// ⚠ On failure the EXISTING cache is kept. A till that dropped its bands because the wifi
    /// blinked would have nothing to price with — and an empty band list is not a smaller problem
    /// than a stale one, it is a till that cannot sell.
    /// </summary>
    public async Task<int?> RefreshAsync(CancellationToken ct = default)
    {
        var bands = await _api.GetVatBandsAsync(ct);
        if (bands is null) return null;

        await _store.SaveAsync(bands, ct);
        return bands.Bands.Length;
    }

    /// <summary>
    /// The rate in force for one band at a moment — the value a line is priced against.
    ///
    /// ⚠ <paramref name="atUtc"/> is the SALE's instant, not "now" at sync time. That is what makes
    /// a future-dated change apply on the day to a till that has been offline for a fortnight.
    /// </summary>
    public async Task<int?> RateBpAtAsync(string bandKey, DateTime atUtc, CancellationToken ct = default)
    {
        var bands = await _store.LoadAsync(ct);
        return bands?.RateBpAt(bandKey, atUtc);
    }

    /// <summary>
    /// Which published band a legacy tax row maps to, or null when nothing says.
    ///
    /// ⚠ NULL IS A CORRECT ANSWER and must be sent as null. It means the tenant has two bands at
    /// one rate (zero and exempt are both 0%) and nobody has said which this tax row is. The server
    /// backfills what it can and reports the rest as *unclassified*, which is visible — whereas a
    /// guess here would be an invented figure on a VAT return, and nothing would ever flag it.
    /// </summary>
    public async Task<string?> BandKeyForTaxIdAsync(int taxId, CancellationToken ct = default)
    {
        var bands = await _store.LoadAsync(ct);
        if (bands is null) return null;

        var matches = bands.Bands
            .Where(b => b.LegacyTaxIds != null && b.LegacyTaxIds.Contains(taxId))
            .Select(b => b.Key)
            .ToList();

        // ⚠ A tie is ambiguity, not a coin toss: two bands claiming one tax row means the mapping
        // is wrong, and picking the first would silently attribute takings to whichever sorted
        // first. That is exactly the bug VatAccounting.BandFor was changed to stop doing.
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Has this till ever been told what the bands are? ⚠ A till with none has nothing to
    /// apply — it is not "zero-rated", it is uninformed, and the caller must not treat the two the
    /// same.</summary>
    public async Task<bool> HasBandsAsync(CancellationToken ct = default) =>
        (await _store.LoadAsync(ct))?.Bands.Length > 0;
}
