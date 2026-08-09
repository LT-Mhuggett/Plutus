using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;

namespace Plutus.Client.Storage;

/// <summary>
/// MAUI retrofit WP2/WP3: the till's local operations — identity, catalogue lookup, effective
/// pricing, and the sale commit path.
///
/// It implements <see cref="IOutboxStore"/>, which is the point: WP1 already built and tested the
/// pusher and its retry policy against that interface, so wiring a real SQLite queue underneath
/// required no change to the engine at all.
/// </summary>
public sealed class TillStore : IOutboxStore, ISyncStore
{
    private readonly TillDbContext _db;
    private readonly Func<DateTime> _utcNow;

    public TillStore(TillDbContext db, Func<DateTime>? utcNow = null)
    {
        _db = db;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    // ── identity / meta ──

    public async Task<string?> GetMetaAsync(string key, CancellationToken ct = default) =>
        (await _db.Meta.AsNoTracking().FirstOrDefaultAsync(m => m.Key == key, ct))?.Value;

    public async Task SetMetaAsync(string key, string? value, CancellationToken ct = default)
    {
        var row = await _db.Meta.FirstOrDefaultAsync(m => m.Key == key, ct);
        if (row == null) _db.Meta.Add(new MetaEntry { Key = key, Value = value });
        else row.Value = value;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> GetGuidMetaAsync(string key, CancellationToken ct = default) =>
        Guid.TryParse(await GetMetaAsync(key, ct), out var g) ? g : null;

    public async Task<int?> GetIntMetaAsync(string key, CancellationToken ct = default) =>
        int.TryParse(await GetMetaAsync(key, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    // ── catalogue ──

    /// <summary>
    /// Resolve a scanned code. Binned items are excluded — an offline till must stop selling
    /// something the portal has withdrawn.
    ///
    /// ⚠ ONE CODE PER ITEM: <see cref="CatalogueItem.IdOne"/> IS the barcode. There is no alias
    /// lookup, because there is nothing to look up — the platform has **no barcode entity at all**
    /// (verified 2026-08-09: nothing in `Plutus.Entities` models one, and the catalogue feed
    /// carries no alias list), and Matt confirmed the same day that multi-barcode items are not
    /// needed.
    ///
    /// This method used to fall back to a local `Barcodes` table that **nothing has ever written**.
    /// That is worse than not having the feature: it reads as support for multiple barcodes, so the
    /// next person to be asked for them would reasonably assume the till half-supports it already.
    /// Removed deliberately. Adding real multi-barcode support means a server entity, a feed field
    /// and a portal UI first — a platform decision, not a till change.
    /// </summary>
    public async Task<CatalogueItem?> FindByBarcodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return await _db.CatalogueItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.IdOne == code && !i.Removed, ct);
    }

    /// <summary>
    /// Search the catalogue the way an operator types — part of a name, a code, several words in
    /// any order.
    ///
    /// ⚠ MATCHING IS <see cref="ItemSearch"/>, NOT a LIKE clause written here. That rule went from
    /// three implementations to one on 2026-08-08 precisely so two tills cannot disagree about
    /// what "batman one" finds; a fourth copy in this method would undo it. Kept in memory after a
    /// cheap SQL prefilter because the matcher is word-based and SQLite cannot express it.
    ///
    /// ⚠ Binned items are excluded, like <see cref="FindByBarcodeAsync"/> — an offline till must
    /// stop selling what the portal has withdrawn.
    /// </summary>
    /// <param name="matchAllWords">Per-device preference: every word must match, or any one.
    /// ⚠ Two tills with the SAME setting must agree; the setting itself is allowed to differ.</param>
    public async Task<IReadOnlyList<CatalogueItem>> SearchAsync(
        string? search, bool matchAllWords = true, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search)) return Array.Empty<CatalogueItem>();
        if (limit <= 0) limit = 50;

        var tokens = ItemSearch.Tokenise(search, matchAllWords).ToList();
        if (tokens.Count == 0) return Array.Empty<CatalogueItem>();

        // Prefilter on the LONGEST token so a 20k catalogue does not come back in full to be
        // filtered in memory. It is a superset of the real answer — ItemSearch still decides.
        var widest = tokens.OrderByDescending(t => t.Length).First();

        // ⚠ LIKE, NOT Contains. ItemSearch lowercases both sides, but EF translates
        // string.Contains to SQLite's instr(), which is CASE-SENSITIVE — so the lowercased token
        // "bat" never matched "Batman" and this returned nothing at all for ordinary searches.
        // SQLite's LIKE is case-insensitive for ASCII, which is what the matcher expects.
        // (`%`/`_` typed by an operator widen the prefilter harmlessly: it only has to be a
        // superset, and ItemSearch makes the real decision below.)
        var pattern = "%" + widest + "%";

        var candidates = await _db.CatalogueItems.AsNoTracking()
            .Where(i => !i.Removed && (EF.Functions.Like(i.Name, pattern) || EF.Functions.Like(i.IdOne, pattern)))
            .Take(Math.Max(limit * 20, 200))
            .ToListAsync(ct);

        return candidates
            .Where(i => ItemSearch.Matches(tokens, i.Name, i.IdOne, null))
            .OrderBy(i => i.Name)
            .Take(limit)
            .ToList();
    }

    /// <summary>How many sellable items this till holds. ⚠ Zero means the catalogue has never
    /// synced — which is a DIFFERENT problem from "nothing matched your search", and the two must
    /// not be reported with the same message.</summary>
    public Task<int> CatalogueCountAsync(CancellationToken ct = default) =>
        _db.CatalogueItems.AsNoTracking().CountAsync(i => !i.Removed, ct);

    /// <summary>
    /// The price to charge right now: the latest scheduled price whose effective moment has
    /// passed, else the item's standing price.
    ///
    /// Evaluated AT LOOKUP TIME rather than applied by a sync job, so a price scheduled for 02:00
    /// takes effect at 02:00 on a till that has been offline for a week — the till doesn't need to
    /// hear from anyone for a planned change to happen on time.
    /// </summary>
    public async Task<long> EffectivePricePenceAsync(Guid itemId, DateTime? atUtc = null, CancellationToken ct = default) =>
        (await EffectivePricePairAsync(itemId, atUtc, ct)).IncPence;

    /// <summary>
    /// The price to charge right now, as the PAIR the wire needs: inc-VAT and ex-VAT.
    ///
    /// ⚠ WHY A PAIR AND NOT A RATE. `VatLineMath.ForLine` takes inc AND ex, and C1 rule 2 forbids
    /// deriving one from the other at sale time: a line's declared rate comes FROM the pair
    /// (`round((inc/ex − 1) × 10000)`), which is why an ordinary 20% line legitimately ships as
    /// 1993–2004bp. Handing the basket a single number and a rate would force it to re-derive the
    /// other half with its own rounding, and that is a penny-per-line disagreement with the web
    /// till on every VAT return, for ever, with nothing to flag it.
    ///
    /// The ex figure comes from the SAME price point as the inc figure wherever the platform
    /// supplied one — <see cref="PricePointDto.ExPricePence"/> is authoritative. It is only
    /// derived from the snapped rate for the baseline case, where no price point exists at all.
    /// </summary>
    /// <returns>Both halves, and zero/zero for an unknown item — a caller must never treat that as
    /// a free item; <see cref="FindByBarcodeAsync"/> is what decides whether an item is sellable.</returns>
    public async Task<PricePair> EffectivePricePairAsync(
        Guid itemId, DateTime? atUtc = null, CancellationToken ct = default)
    {
        var at = atUtc ?? _utcNow();
        var item = await _db.CatalogueItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item == null) return new PricePair(0, 0);

        // ⚠ RESOLVED WITH THE SHARED RULE (SharedKernel.PriceResolution), not a local
        // reimplementation: store override → central list → the catalogue baseline. A till that
        // resolved this its own way would quote a different price from the server and from the
        // next counter along, and a customer would find it before anyone else did.
        var pricing = DeserialisePricing(item.BandData);

        // ⚠ The local PriceSchedule table is folded in as CENTRAL points rather than replaced. It
        // is WP2's original mechanism and the cutover tool writes it; the feed's timeline arrived
        // later. Two sources of scheduled prices that ignored each other would be a till where the
        // answer depended on which one happened to be populated.
        var scheduled = await _db.PriceSchedule.AsNoTracking()
            .Where(p => p.ItemId == itemId)
            .Select(p => new { p.PricePence, p.EffectiveFromUtc })
            .ToListAsync(ct);

        var central = pricing.Central.Select(ToPoint)
            .Concat(scheduled.Select(s => new PricePoint(
                s.PricePence,
                ExFromInc(s.PricePence, item.VatRateBp),
                s.EffectiveFromUtc,
                s.EffectiveFromUtc)))
            .ToList();

        var resolved = PriceResolution.Resolve(
            (PriceOwner)pricing.PricePolicy,
            central,
            pricing.Store.Select(ToPoint),
            item.PricePence,
            // The baseline ex-price is not stored separately; derive it from the snapped rate so
            // the pair stays coherent. Only used when NO price point exists at all.
            ExFromInc(item.PricePence, item.VatRateBp),
            at);

        // ⚠ Both halves come from the SAME resolved point. Taking inc from the timeline and ex from
        // the baseline would pair two prices that were never a pair, and the derived rate would be
        // whatever fell out of that.
        if (resolved is { } point && point.PricePence > 0)
            return new PricePair(
                point.PricePence,
                point.ExPricePence > 0 ? point.ExPricePence : ExFromInc(point.PricePence, item.VatRateBp));

        return new PricePair(item.PricePence, ExFromInc(item.PricePence, item.VatRateBp));

        static PricePoint ToPoint(PricePointDto p) =>
            new(p.PricePence, p.ExPricePence, p.EffectiveFromUtc, p.CreatedAtUtc);

        static long ExFromInc(long inc, int rateBp) =>
            rateBp <= 0 ? inc : (long)Math.Round(inc * 10000d / (10000d + rateBp), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Which published VAT band this item's legacy tax row maps to, and whether it is stock-tracked.
    ///
    /// ⚠ `TaxId` was serialised into <c>BandData</c> and never exposed, so
    /// `VatBandCache.BandKeyForTaxIdAsync` had nothing to be given and `LineMeta.VatBand` could
    /// never be set — which means zero-rated and exempt were indistinguishable on the wire at 0bp.
    /// That is the one VAT distinction no rate can carry, and it decides whether a shop can recover
    /// input tax.
    /// </summary>
    public async Task<ItemTaxInfo?> TaxInfoAsync(Guid itemId, CancellationToken ct = default)
    {
        var item = await _db.CatalogueItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item == null) return null;

        var pricing = DeserialisePricing(item.BandData);
        return new ItemTaxInfo(pricing.TaxId, item.VatRateBp, item.StockUntracked);
    }

    /// <summary>Price timeline + tax row for one item, or empty when this row predates the feed
    /// carrying them. ⚠ Never throws: a legacy or half-written value must fall back to the
    /// baseline price rather than stopping a sale.</summary>
    private static LocalPricing DeserialisePricing(string? bandData)
    {
        if (string.IsNullOrWhiteSpace(bandData)) return Empty;
        try
        {
            return JsonSerializer.Deserialize<LocalPricing>(bandData) ?? Empty;
        }
        catch (JsonException)
        {
            // Older rows held the bare TaxId as text. Not an error — just nothing to resolve with.
            return Empty;
        }
    }

    private static readonly LocalPricing Empty =
        new(0, 0, Array.Empty<PricePointDto>(), Array.Empty<PricePointDto>());

    /// <summary>Apply a page of the changes feed. Tombstones (removed) are honoured, not skipped —
    /// otherwise a binned item stays sellable on every offline till indefinitely.</summary>
    public async Task ApplyCatalogueChangesAsync(IEnumerable<CatalogueItem> changes, long newVersion, CancellationToken ct = default)
    {
        foreach (var incoming in changes)
        {
            var existing = await _db.CatalogueItems.FirstOrDefaultAsync(i => i.Id == incoming.Id, ct);
            if (existing == null) _db.CatalogueItems.Add(incoming);
            else
            {
                existing.IdOne = incoming.IdOne;
                existing.Name = incoming.Name;
                existing.Kind = incoming.Kind;
                existing.PricePence = incoming.PricePence;
                existing.VatRateBp = incoming.VatRateBp;
                existing.CategoryId = incoming.CategoryId;
                existing.StockUntracked = incoming.StockUntracked;
                existing.BandData = incoming.BandData;
                existing.Removed = incoming.Removed;
                existing.UpdatedAtUtc = incoming.UpdatedAtUtc;
            }
        }
        await _db.SaveChangesAsync(ct);
        await SetMetaAsync(MetaKeys.CatalogueVersion, newVersion.ToString(CultureInfo.InvariantCulture), ct);
    }

    // ── WP5: the sync loop's half of the store (ISyncStore) ──

    public Task<string?> GetCatalogueCursorAsync(CancellationToken ct = default) =>
        GetMetaAsync(MetaKeys.CatalogueVersion, ct);

    /// <summary>
    /// Apply one page of the changes feed and advance the cursor.
    ///
    /// ⚠ ONE TRANSACTION, and the ordering matters in only one direction. If the rows commit and
    /// the cursor does not, the next sync re-applies the same page — harmless, because every row is
    /// an upsert. If the cursor commits and the rows do not, those changes are skipped FOR EVER and
    /// nothing anywhere reports it: the till simply keeps selling at yesterday's prices.
    /// </summary>
    public async Task ApplyCatalogueAsync(
        IReadOnlyList<CatalogueItemDto> items, string? cursor, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        foreach (var dto in items)
        {
            var existing = await _db.CatalogueItems.FirstOrDefaultAsync(i => i.Id == dto.Id, ct);
            if (existing == null)
            {
                _db.CatalogueItems.Add(Map(dto));
                continue;
            }

            var mapped = Map(dto);
            existing.IdOne = mapped.IdOne;
            existing.Name = mapped.Name;
            existing.Kind = mapped.Kind;
            existing.PricePence = mapped.PricePence;
            existing.VatRateBp = mapped.VatRateBp;
            existing.CategoryId = mapped.CategoryId;
            existing.BandData = mapped.BandData;
            // ⚠ MISSED IN PHASE 1 and caught in step 10: the field-by-field update branch did not
            // copy StockUntracked, so an item that BECAME untracked in the portal stayed tracked on
            // every till that already held it — only brand-new items got the flag. A hand-written
            // upsert is exactly where this kind of omission hides; the tombstone below is the only
            // reason the same bug never happened to Removed.
            existing.StockUntracked = mapped.StockUntracked;
            existing.Removed = mapped.Removed;
            existing.UpdatedAtUtc = mapped.UpdatedAtUtc;
        }

        if (cursor != null) await SetMetaAsync(MetaKeys.CatalogueVersion, cursor, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Feed row → local row.
    ///
    /// ⚠ <see cref="CatalogueItem.VatRateBp"/> is derived here from the PRICE PAIR, and it is a
    /// DISPLAY LABEL ONLY. At sale time the line's rate is derived from the pair again, exactly as
    /// the web till does — sending a stored rate instead would make the two tills bucket the same
    /// item's VAT differently, which is the drift till-design Part C exists to prevent.
    ///
    /// <c>BandData</c> carries the legacy tax row so the till can resolve a published VAT band
    /// without a second lookup.
    /// </summary>
    private static CatalogueItem Map(CatalogueItemDto dto) => new()
    {
        Id = dto.Id,
        IdOne = dto.IdOne,
        Name = dto.Name,
        Kind = 0,
        PricePence = dto.PricePence,
        VatRateBp = RateBpFromPair(dto.PricePence, dto.ExPricePence),
        CategoryId = dto.CategoryId,
        // ⚠ FE5: carried by the feed since it shipped and dropped here until 2026-08-09, so an
        // untracked item (carrier bag, service) looked stock-tracked to every screen.
        StockUntracked = dto.StockUntracked,
        // ⚠ BandData carries the legacy tax row AND the effective-dated price timeline, because the
        // local schema has one spare text column and WP2's cutover is what gives prices a table of
        // their own. Ugly, and deliberately so: the alternative was a schema migration on a store
        // that is about to be replaced wholesale.
        BandData = SerialiseBandData(dto),
        Removed = dto.Removed,
        UpdatedAtUtc = dto.UpdatedAtUtc,
    };

    /// <summary>Tax row + price timeline, so an offline till can resolve the price at the SALE's
    /// instant rather than charging whatever it last downloaded.</summary>
    private static string SerialiseBandData(CatalogueItemDto dto) =>
        JsonSerializer.Serialize(new LocalPricing(
            dto.TaxId, dto.PricePolicy, dto.CentralPrices ?? Array.Empty<PricePointDto>(),
            dto.StorePrices ?? Array.Empty<PricePointDto>()));

    private sealed record LocalPricing(
        int TaxId, byte PricePolicy, PricePointDto[] Central, PricePointDto[] Store);

    /// <summary>The web till's derivation (<c>api.ts:978</c>): <c>round((inc/ex − 1) × 10000)</c>.
    /// Wobbled values like 2002bp are CORRECT and expected — the platform groups takings by band,
    /// not by this number.</summary>
    private static int RateBpFromPair(long incPence, long exPence) =>
        exPence <= 0 ? 0 : (int)Math.Round(((double)incPence / exPence - 1d) * 10000d, MidpointRounding.AwayFromZero);

    public Task<int> OutboxDepthAsync(CancellationToken ct = default) =>
        CountAsync(OutboxStatus.Pending, ct);

    public async Task<TimeSpan?> OldestPendingAgeAsync(CancellationToken ct = default)
    {
        var oldest = await OldestPendingAtUtcAsync(ct);
        return oldest is DateTime t ? _utcNow() - t : null;
    }

    // ── the sale commit path (WP3) ──

    /// <summary>
    /// Commit a completed sale locally: allocate the next DeviceSeq and write the row — in ONE
    /// transaction, because the sale and its outbox entry are the same row and must never diverge.
    ///
    /// ⚠ ORDERING (retrofit risk #2, decided): this happens BEFORE the receipt prints. Printing is
    /// physical and irreversible; if it came first and the commit failed, a customer would hold a
    /// receipt for a sale that was never queued. A print failure after commit is a reprint problem
    /// — recoverable. The other way round is money that no longer exists.
    /// </summary>
    public async Task<LocalSale> CommitSaleAsync(IngestSaleRequest sale, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var seq = (long.TryParse(await GetMetaAsync(MetaKeys.DeviceSeq, ct), out var last) ? last : 0) + 1;
        sale.DeviceSeq = seq;

        var row = new LocalSale
        {
            SaleId = sale.SaleId,
            DeviceSeq = seq,
            BusinessDay = sale.BusinessDay.ToString("yyyy-MM-dd"),
            OccurredAtUtc = sale.OccurredAtUtc,
            PayloadJson = JsonSerializer.Serialize(sale, PlutusApiClient.Json),
            Status = (int)OutboxStatus.Pending,
        };
        _db.LocalSales.Add(row);

        // ⚠ IN THE SAME TRANSACTION, from the SAME payload. A refund recorded separately could be
        // lost while the sale survived, and the next refund against that original would be allowed
        // to give the money back twice.
        foreach (var refund in RefundsIn(sale))
            _db.LocalRefunds.Add(refund);

        var meta = await _db.Meta.FirstOrDefaultAsync(m => m.Key == MetaKeys.DeviceSeq, ct);
        if (meta == null) _db.Meta.Add(new MetaEntry { Key = MetaKeys.DeviceSeq, Value = seq.ToString(CultureInfo.InvariantCulture) });
        else meta.Value = seq.ToString(CultureInfo.InvariantCulture);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return row;
    }

    /// <summary>
    /// What this sale gives back, per original sale, derived from its own payload.
    ///
    /// ⚠ Read from `LineMeta.Return.OriginSaleId` — where `SaleAssembler` puts it — rather than
    /// from anything the caller passes, so the recorded refund and the sale that was actually sent
    /// describe the same event.
    /// </summary>
    private static IEnumerable<LocalRefund> RefundsIn(IngestSaleRequest sale)
    {
        var byOrigin = new Dictionary<Guid, long>();

        foreach (var line in sale.Lines ?? Enumerable.Empty<IngestLine>())
        {
            var origin = LineMeta.FromJson(line.DiscountsJson)?.Return?.OriginSaleId;
            if (!Guid.TryParse(origin, out var originId) || originId == Guid.Empty) continue;

            // ⚠ Magnitude. A return's LineGrossPence is NEGATIVE, and a cap compared against a
            // negative running total would let every refund through.
            byOrigin.TryGetValue(originId, out var running);
            byOrigin[originId] = running + Math.Abs(line.LineGrossPence);
        }

        return byOrigin.Select(kv => new LocalRefund
        {
            SaleId = sale.SaleId,
            OriginSaleId = kv.Key,
            RefundedPence = kv.Value,
        });
    }

    /// <summary>
    /// Read a sale back — the payload exactly as it was committed (cutover step 15).
    ///
    /// ⚠ NOTHING COULD DO THIS BEFORE. `GetPendingAsync` filters to Pending, so the moment a sale
    /// was delivered it became unreadable by this till: no reprint, no receipt-led refund, no
    /// offline X/Z. The sale was sitting in the table the whole time.
    ///
    /// Returns null when the till has never seen the sale, or when the window has pruned it.
    /// </summary>
    public async Task<IngestSaleRequest?> FindLocalSaleAsync(Guid saleId, CancellationToken ct = default)
    {
        var row = await _db.LocalSales.AsNoTracking().FirstOrDefaultAsync(s => s.SaleId == saleId, ct);
        if (row == null) return null;

        try
        {
            return JsonSerializer.Deserialize<IngestSaleRequest>(row.PayloadJson, PlutusApiClient.Json);
        }
        catch (JsonException)
        {
            // ⚠ Null, never a half-built sale. A payload this till cannot parse is a payload it
            // must not reason about — refunding against a guess is worse than saying "not found".
            return null;
        }
    }

    /// <summary>
    /// How much of <paramref name="originSaleId"/> has ALREADY been given back, in pence, as a
    /// positive number — summed across every part-refund this till knows about.
    ///
    /// ⚠ THIS IS WHAT STOPS A DOUBLE REFUND. Matt's binding default 12: you cannot refund more than
    /// was paid. A £30 item refunded £20 today and £20 tomorrow is £10 of the shop's money gone,
    /// and each refund looks perfectly reasonable on its own — only the running total says
    /// otherwise.
    ///
    /// ⚠ COUNTS EVERY STATUS, deliberately. A refund still queued in the outbox has already had
    /// cash handed over the counter; excluding it because the server has not confirmed it yet would
    /// let the same sale be refunded again while the first one waits for a network.
    ///
    /// ⚠ It knows only what THIS till has seen. A refund taken on another till, or one pruned past
    /// the window, is invisible here — which is why the server enforces the same cap (step 17) and
    /// this is the offline half, not the authority.
    /// </summary>
    public async Task<long> AlreadyRefundedPenceAsync(Guid originSaleId, CancellationToken ct = default)
    {
        if (originSaleId == Guid.Empty) return 0;

        return await _db.LocalRefunds.AsNoTracking()
            .Where(r => r.OriginSaleId == originSaleId)
            .SumAsync(r => r.RefundedPence, ct);
    }

    // ── parked baskets (cutover step 18) ────────────────────────────────────────────────────
    //
    // ⚠ The `SavedBasket` table has existed, been mapped and been indexed since the store was
    // built, and NOTHING EVER TOUCHED IT. Parking wrote to the LEGACY SQLite database instead —
    // which on a portal-provisioned till is an empty file the app creates on first use, so parked
    // baskets lived somewhere nothing else in the platform knows about.

    /// <summary>
    /// Park a basket. Overwrites a park with the same id, so re-parking is not a leak.
    ///
    /// ⚠ <paramref name="contractJson"/> must be CONTRACT JSON with no .NET type metadata. The
    /// legacy blobs were Newtonsoft `TypeNameHandling.Auto`, carrying `NatApp.Plutus.*` type names
    /// that stop resolving the moment a namespace moves — which is exactly what made a discounted
    /// parked basket crash the app on recall.
    /// </summary>
    public async Task SaveBasketAsync(Guid id, string? name, string contractJson, CancellationToken ct = default)
    {
        var row = await _db.SavedBaskets.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (row == null)
        {
            _db.SavedBaskets.Add(new SavedBasket
            {
                Id = id, Name = name, ContractJson = contractJson, CreatedAtUtc = _utcNow(),
            });
        }
        else
        {
            row.Name = name;
            row.ContractJson = contractJson;
            row.CreatedAtUtc = _utcNow();
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Every parked basket, newest first.</summary>
    public async Task<IReadOnlyList<SavedBasket>> ListBasketsAsync(CancellationToken ct = default) =>
        await _db.SavedBaskets.AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(ct);

    /// <summary>
    /// Remove a parked basket. ⚠ Returns whether it was there: recall DELETES then restores, and a
    /// silent no-op would let two operators recall the same basket and sell it twice.
    /// </summary>
    public async Task<bool> DeleteBasketAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.SavedBaskets.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (row == null) return false;

        _db.SavedBaskets.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Prune delivered sales past the rolling window. NEVER touches Pending, Failed or
    /// Quarantined: those are money still owed, or money a human has to look at.
    ///
    /// ⚠ IT MUST NOT TOUCH `LocalRefunds` EITHER, and there is deliberately no cascade to make it.
    /// Those rows say how much of an ORIGINAL sale has already been given back. Deleting them when
    /// the refund sale ages out would drop the running total back to zero and let the same original
    /// be refunded all over again — the exact double-refund the cap exists to stop. They are a few
    /// bytes each and they outlive the sale that created them on purpose.
    /// </summary>
    public async Task<int> PrunePushedAsync(TimeSpan window, CancellationToken ct = default)
    {
        var cutoff = _utcNow() - window;
        var stale = await _db.LocalSales
            .Where(s => s.Status == (int)OutboxStatus.Pushed && s.PushedAtUtc != null && s.PushedAtUtc < cutoff)
            .ToListAsync(ct);
        _db.LocalSales.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
        return stale.Count;
    }

    // ── IOutboxStore (drives the WP1 pusher unchanged) ──

    public async Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int max, CancellationToken ct = default) =>
        await _db.LocalSales.AsNoTracking()
            .Where(s => s.Status == (int)OutboxStatus.Pending)
            .OrderBy(s => s.DeviceSeq)
            .Take(max)
            .Select(s => new OutboxEntry
            {
                SaleId = s.SaleId, DeviceSeq = s.DeviceSeq, PayloadJson = s.PayloadJson,
                Status = (OutboxStatus)s.Status, PushedAtUtc = s.PushedAtUtc,
                ServerResponseJson = s.ServerResponseJson, Attempts = s.Attempts,
            })
            .ToListAsync(ct);

    public async Task UpdateAsync(OutboxEntry entry, CancellationToken ct = default)
    {
        var row = await _db.LocalSales.FirstOrDefaultAsync(s => s.SaleId == entry.SaleId, ct);
        if (row == null) return;
        row.Status = (int)entry.Status;
        row.PushedAtUtc = entry.PushedAtUtc;
        row.ServerResponseJson = entry.ServerResponseJson;
        row.Attempts = entry.Attempts;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> CountAsync(OutboxStatus status, CancellationToken ct = default) =>
        await _db.LocalSales.CountAsync(s => s.Status == (int)status, ct);

    public async Task<DateTime?> OldestPendingAtUtcAsync(CancellationToken ct = default) =>
        await _db.LocalSales.Where(s => s.Status == (int)OutboxStatus.Pending)
            .OrderBy(s => s.DeviceSeq)
            .Select(s => (DateTime?)s.OccurredAtUtc)
            .FirstOrDefaultAsync(ct);
}
