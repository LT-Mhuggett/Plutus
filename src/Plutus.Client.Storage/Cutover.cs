using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.SharedKernel;

namespace Plutus.Client.Storage;

/// <summary>One legacy catalogue row, as read out of the old Kapow-schema file.</summary>
public sealed record LegacyItem(string IdOne, string Name, decimal Price, decimal ExPrice, string? CategoryId);

/// <summary>What a cutover did, for the operator and the log.</summary>
public sealed record CutoverResult(string ArchivePath, int ItemsSeeded, int SpotChecked);

/// <summary>
/// MAUI retrofit WP2 — moving a till from the legacy Kapow-schema database to local store v2.
///
/// THE RULE (binding default §9.3): <b>archive, never merge, never delete</b>. The old file is
/// copied aside intact — it is the translation agent's input, and it is the only copy of that
/// till's history until the central migration has run. Nothing here writes to it.
///
/// THE HARD STOP (§10): a till's item ids MUST equal the ids the central migration produced, or
/// the same barcode means different things on the till and the server — every sale that till
/// pushes then attributes stock and revenue to the wrong item, silently, because lines key on the
/// barcode while stock keys on the id.
/// </summary>
public static class Cutover
{
    /// <summary>
    /// The catalogue id mapping. ⚠ This is <see cref="DeterministicGuid.ForItem"/> keyed on the
    /// legacy BUSINESS id — <b>not</b> the tenant id, and <b>not</b> <c>Plutus.Migration.Kapow</c>'s
    /// <c>IdRemap</c>, which mints random ids per run and applies only to historic sale rows.
    /// Checking determinism against IdRemap is the trap: it would fail, wrongly, and stop a
    /// cutover that was fine.
    /// </summary>
    public static Guid ItemIdFor(Guid businessId, string itemIdOne) =>
        DeterministicGuid.ForItem(businessId, itemIdOne);

    // ⚠ L1, 2026-08-23 — `ArchiveLegacyDatabase` deleted. It copied the legacy file aside and was
    // the only producer of the `LegacyArchivedAtUtc` stamp that gated enrolment. ⚠⚠ THE LEGACY
    // `Database.db` ITSELF IS UNTOUCHED AND MUST STAY: it is the shop's pre-cutover sales history
    // and there is no server copy (default 3 — archive, never delete).

    /// <summary>
    /// Seed the v2 catalogue from legacy rows, minting each item's id deterministically.
    ///
    /// <paramref name="centralIdLookup"/> is the hard stop: when supplied, a sample of the seeded
    /// ids is compared against what the server holds for the same barcodes, and a mismatch throws
    /// rather than leaving the till subtly wrong.
    ///
    /// ⚠ **Pass null against today's server** (checked 2026-08-08). The central catalogue has no
    /// item UUIDs to ask about — `Items` is still keyed by barcode (gap-analysis F4, deferred as
    /// option (b) in `NatApp-Translation-Agent-Plan` §3.3). The only central item GUIDs that exist
    /// are the RANDOM ones `Migration.Kapow`'s `IdRemap` minted for historic SALE LINES, and
    /// comparing against those would fail a perfectly good cutover.
    ///
    /// What actually has to hold is that this till derives ids the same way the WEB TILL does —
    /// which it does by construction, since both call <see cref="DeterministicGuid.ForItem"/> on
    /// the legacy business id. Wire this parameter up only if the catalogue ever gains real UUID
    /// PKs, and then only if those PKs are themselves derived rather than minted.
    /// </summary>
    public static async Task<CutoverResult> SeedCatalogueAsync(
        TillDbContext db,
        Guid businessId,
        IReadOnlyList<LegacyItem> legacyItems,
        string archivePath,
        Func<string, Task<Guid?>>? centralIdLookup = null,
        int spotCheck = 10,
        CancellationToken ct = default)
    {
        if (businessId == Guid.Empty)
            throw new ArgumentException(
                "businessId is required and must be the LEGACY Business id, not the tenant id — " +
                "deriving item ids from the tenant id corrupts them silently.", nameof(businessId));

        var seeded = new List<CatalogueItem>(legacyItems.Count);
        foreach (var l in legacyItems)
        {
            if (string.IsNullOrWhiteSpace(l.IdOne)) continue; // an item with no barcode cannot be scanned or keyed
            seeded.Add(new CatalogueItem
            {
                Id = ItemIdFor(businessId, l.IdOne),
                IdOne = l.IdOne,
                Name = l.Name ?? "",
                Kind = 0,
                PricePence = Pence.FromDecimal(l.Price),
                VatRateBp = VatRateBpFrom(l.Price, l.ExPrice),
                CategoryId = Guid.TryParse(l.CategoryId, out var c) ? c : null,
                Removed = false,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }

        // Deduplicate on barcode: the legacy file can hold two rows with the same code, and a
        // unique index would otherwise fail the whole cutover on the last one.
        var distinct = seeded.GroupBy(i => i.IdOne).Select(g => g.First()).ToList();

        db.CatalogueItems.AddRange(distinct);
        await db.SaveChangesAsync(ct);

        var checkedCount = 0;
        if (centralIdLookup != null)
        {
            foreach (var item in distinct.Take(spotCheck))
            {
                var central = await centralIdLookup(item.IdOne);
                if (central == null) continue;      // the server hasn't been migrated for this item yet
                checkedCount++;
                if (central.Value != item.Id)
                    throw new InvalidOperationException(
                        $"STOP: item id mismatch for barcode {item.IdOne} — till derived {item.Id}, " +
                        $"server holds {central.Value}. The catalogue mapping is not deterministic " +
                        "against this server; do not enrol this till (see the retrofit plan §10).");
            }
        }

        await db.Meta.AddRangeAsync(new[]
        {
            new MetaEntry { Key = MetaKeys.BusinessId, Value = businessId.ToString("D") },
            // ⚠ L1, 2026-08-23 — the `LegacyArchivedAtUtc` stamp is gone with the gate that read
            // it. Nothing consumes it any more, and a stamp nobody reads is a fact nobody can act on.
        }, ct);
        await db.SaveChangesAsync(ct);

        return new CutoverResult(archivePath, distinct.Count, checkedCount);
    }

    /// <summary>
    /// A LAST-RESORT band list for a till that is cutting over before it has ever reached the server.
    ///
    /// ⚠ THIS IS THE ONLY UK VAT KNOWLEDGE LEFT ON A CLIENT, and it is deliberately not the source of
    /// truth: WP2c made the portal the owner of every band, and a till applies what it is published
    /// (<c>GET /api/v1/vat/bands</c>). This list exists solely because cutover can run before the
    /// first successful sync, and the alternative — refusing to seed a catalogue — would block a shop
    /// from opening. Pass the published bands to <see cref="VatRateBpFrom"/> whenever they are known,
    /// and note the values here are DISPLAY LABELS on the local catalogue: at sale time every line's
    /// rate is derived from its price pair, exactly as the web till does it.
    /// </summary>
    internal static readonly int[] FallbackBandsBp = { 0, 500, 1750, 2000 };

    /// <summary>
    /// Recover the VAT band from the legacy decimal pair — the old schema stores inc- and ex-VAT
    /// prices rather than a rate, so it has to be inferred.
    ///
    /// ⚠ NAIVE INFERENCE IS WRONG, and wrong in a way that bites later. Real data: £14.99 ex
    /// £12.49 is 20% priced to the penny, but the arithmetic yields 2001.6 → 2002bp. Left like
    /// that, every cutover would seed slightly-off bands, and WP2b's ingest validation would then
    /// quarantine every sale of those items as "a rate not in force". So: snap to the nearest
    /// KNOWN band when within tolerance, and only fall back to the raw figure when nothing is
    /// close (which is a genuine oddity worth preserving rather than silently flattening to 20%).
    /// </summary>
    /// <param name="bandsBp">The rates the PORTAL has published, when the till knows them. Null or
    /// empty falls back to <see cref="FallbackBandsBp"/> — see the note there.</param>
    internal static int VatRateBpFrom(decimal price, decimal exPrice, IReadOnlyCollection<int>? bandsBp = null)
    {
        if (exPrice <= 0m || price <= 0m) return 0;
        // The pair → rate step is the SHARED implementation every till uses, so a snapped label can
        // never be derived differently from the rate the same line will declare at sale time.
        var derived = VatLineMath.RateBpFromPair(Pence.FromDecimal(price), Pence.FromDecimal(exPrice));

        var bands = bandsBp is { Count: > 0 } ? bandsBp : FallbackBandsBp;
        var nearest = bands.OrderBy(b => Math.Abs(b - derived)).First();
        // One tolerance for the whole platform (VatAccounting.BandSnapToleranceBp) — a client with
        // its own number would disagree with the server about which band takings belong to.
        return Math.Abs(nearest - derived) <= VatAccounting.BandSnapToleranceBp ? nearest : derived;
    }
}
