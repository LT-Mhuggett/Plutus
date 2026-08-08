using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    // ⚠ Twins of Plutus.Contracts.Client.SyncContracts — see the note in HeartbeatController and
    // till-design C2. Change one shape, change both.

    public sealed record PricePointDto(
        long PricePence, long ExPricePence, DateTime EffectiveFromUtc, DateTime CreatedAtUtc);

    public sealed record CatalogueItemDto(
        Guid Id,
        string IdOne,
        string Name,
        long PricePence,
        long ExPricePence,
        int TaxId,
        Guid? CategoryId,
        bool StockUntracked,
        bool Removed,
        DateTime UpdatedAtUtc,
        byte PricePolicy = 0,
        PricePointDto[]? CentralPrices = null,
        PricePointDto[]? StorePrices = null);

    public sealed record CatalogueChangesResult(
        string? Cursor,
        bool HasMore,
        CatalogueItemDto[] Items);

    /// <summary>
    /// WP5 — GET /api/v1/catalogue/changes?since={cursor}&amp;limit={n}
    ///
    /// Everything that has changed since the till last asked, in <c>(ModifiedAt, IdOne)</c> order.
    /// This is what lets a till trade offline against fresh prices instead of a snapshot taken
    /// whenever someone last reinstalled it.
    /// </summary>
    [ApiController]
    [Route("api/v1/catalogue")]
    public sealed class CatalogueChangesController : ControllerBase
    {
        /// <summary>Rows per page. 500 keeps a 20k-item first sync to 40 round trips while staying
        /// small enough that a page over a shop's DSL line is not a visible stall.</summary>
        public const int DefaultLimit = 500;
        public const int MaxLimit = 2000;

        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public CatalogueChangesController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        /// <summary>
        /// ⚠ Gated <c>sales.ingest</c> — a device token reads this. The catalogue is what a till
        /// needs to sell, so requiring an operator token would mean a till could not refresh itself
        /// before someone signs in.
        /// </summary>
        [HttpGet("changes")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Changes([FromQuery] string? since = null, [FromQuery] int? limit = null)
        {
            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

            // Item is tenant-owned (MySqlDbContext.TenantOwned), so the global query filter has
            // already scoped this to the caller's tenant — no explicit predicate, and no way to
            // forget one.
            //
            // ⚠ But note IdTwo: an item's identity is (IdOne barcode, IdTwo BUSINESS id), and the
            // business id is NOT the tenant id. It is what the item GUID is derived from below, and
            // deriving from the tenant id instead produces ids that look fine and are wrong
            // everywhere — stock still moves, because lines key on the barcode.
            var query = _db.Items.AsNoTracking();

            // Keyset seek. An absent or unparseable cursor means "from the beginning" — a full
            // resync, always correct and merely expensive. See CatalogueCursor.
            if (CatalogueCursor.TryDecode(since, out var atUtc, out var afterIdOne))
                query = query.Where(i =>
                    i.ModifiedAt > atUtc || (i.ModifiedAt == atUtc && string.Compare(i.IdOne, afterIdOne) > 0));

            // Fetch one extra to answer HasMore without a second COUNT over a large table.
            var rows = await query
                .OrderBy(i => i.ModifiedAt).ThenBy(i => i.IdOne)
                .Take(take + 1)
                .Select(i => new
                {
                    i.IdOne,
                    i.IdTwo,
                    i.Name,
                    i.Price,
                    i.ExPrice,
                    i.TaxId,
                    i.CatId,
                    i.StockUntracked,
                    i.BinnedAtUtc,
                    i.ModifiedAt,
                })
                .ToListAsync();

            var hasMore = rows.Count > take;
            if (hasMore) rows.RemoveAt(rows.Count - 1);

            // ⚠ PRICES DO NOT LIVE ON THE ITEM. They are effective-dated rows in their own tables,
            // so a till that only received Item.Price would charge the baseline for ever and never
            // see a scheduled reprice. Fetched for exactly this page's items — the whole timeline,
            // future points included, because that is what lets a Sunday-night change activate on
            // an offline till at the boundary.
            var pageIds = rows.Select(r => r.IdOne).ToList();
            var storeId = await StoreIdForCallerAsync();

            var central = (await _db.PriceListEntries.AsNoTracking()
                    .Where(p => pageIds.Contains(p.ItemIdOne))
                    .Select(p => new { p.ItemIdOne, p.PricePence, p.ExPricePence, p.EffectiveFromUtc, p.CreatedAtUtc })
                    .ToListAsync())
                .GroupBy(p => p.ItemIdOne)
                .ToDictionary(g => g.Key, g => g
                    .OrderBy(p => p.EffectiveFromUtc)
                    .Select(p => new PricePointDto(p.PricePence, p.ExPricePence, p.EffectiveFromUtc, p.CreatedAtUtc))
                    .ToArray());

            // ⚠ This till's store only, and live overrides only. Another store's price is not this
            // till's business, and a revoked override is not a price.
            var overrides = storeId is int sid
                ? (await _db.PriceOverrides.AsNoTracking()
                        .Where(o => pageIds.Contains(o.ItemIdOne) && o.StoreId == sid && o.RevokedAtUtc == null)
                        .Select(o => new { o.ItemIdOne, o.PricePence, o.ExPricePence, o.EffectiveFromUtc, o.CreatedAtUtc })
                        .ToListAsync())
                    .GroupBy(o => o.ItemIdOne)
                    .ToDictionary(g => g.Key, g => g
                        .OrderBy(o => o.EffectiveFromUtc)
                        .Select(o => new PricePointDto(o.PricePence, o.ExPricePence, o.EffectiveFromUtc, o.CreatedAtUtc))
                        .ToArray())
                : new Dictionary<string, PricePointDto[]>();

            var policies = await _db.ItemPricePolicies.AsNoTracking()
                .Where(p => pageIds.Contains(p.ItemIdOne))
                .ToDictionaryAsync(p => p.ItemIdOne, p => (byte)p.Policy);

            var items = rows.Select(r => new CatalogueItemDto(
                // DERIVED, never assigned — the same derivation the web till uses, keyed on the
                // legacy BUSINESS id. Both tills therefore agree on an item's id with no mapping
                // table anywhere.
                Id: DeterministicGuid.ForItem(r.IdTwo, r.IdOne),
                IdOne: r.IdOne,
                Name: r.Name ?? string.Empty,
                PricePence: Pence.FromDecimal(r.Price),
                ExPricePence: Pence.FromDecimal(r.ExPrice),
                TaxId: r.TaxId,
                CategoryId: r.CatId,
                StockUntracked: r.StockUntracked,
                // ⚠ The tombstone. A binned item MUST reach the till as a removal, not as an
                // absence: "not in this page" and "withdrawn from sale" are indistinguishable to a
                // client that only ever receives upserts, so without this a binned item stays
                // sellable on every offline till indefinitely.
                Removed: r.BinnedAtUtc != null,
                UpdatedAtUtc: r.ModifiedAt,
                PricePolicy: policies.TryGetValue(r.IdOne, out var pol) ? pol : (byte)0,
                CentralPrices: central.TryGetValue(r.IdOne, out var cp) ? cp : null,
                StorePrices: overrides.TryGetValue(r.IdOne, out var op) ? op : null))
                .ToArray();

            var cursor = rows.Count == 0
                ? since
                : CatalogueCursor.Encode(rows[^1].ModifiedAt, rows[^1].IdOne);

            return Ok(new CatalogueChangesResult(cursor, hasMore, items));
        }

        /// <summary>
        /// Which store's overrides this caller should receive, from the DEVICE in the token.
        ///
        /// ⚠ Derived from the token, never from a query parameter. A till asking for another
        /// store's prices would be a till charging another shop's prices, and the operator would
        /// have no way to tell.
        /// </summary>
        private async Task<int?> StoreIdForCallerAsync()
        {
            if (_tenant.DeviceId is not Guid deviceId) return null;

            return await (from d in _db.Devices.AsNoTracking()
                          join t in _db.Till.AsNoTracking() on d.TillId equals t.Id
                          where d.Id == deviceId
                          select (int?)t.StoreId).FirstOrDefaultAsync();
        }
    }
}
