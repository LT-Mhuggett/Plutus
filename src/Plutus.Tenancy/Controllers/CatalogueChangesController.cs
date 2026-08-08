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
        DateTime UpdatedAtUtc);

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
                UpdatedAtUtc: r.ModifiedAt))
                .ToArray();

            var cursor = rows.Count == 0
                ? since
                : CatalogueCursor.Encode(rows[^1].ModifiedAt, rows[^1].IdOne);

            return Ok(new CatalogueChangesResult(cursor, hasMore, items));
        }
    }
}
