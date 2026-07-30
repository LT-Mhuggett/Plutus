#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Catalogue
{
    /// <summary>Which items a bulk action applies to: an explicit tick-list, OR the same criteria the
    /// operator is looking at ("everything matching this filter"). Exactly one must be supplied.</summary>
    public sealed record BulkCriteria(string Search, bool MatchAllWords, Guid? CatId, bool Binned);

    public sealed record BulkBody(
        string Action,          // set-category | set-brand | clear-brand | bin | restore | set-untracked | clear-untracked
        string Value,           // brand name for set-brand
        Guid? CategoryId,       // target for set-category
        List<string> Ids,       // explicit selection (item barcodes / IdOne)
        BulkCriteria Criteria); // ...or filter-based selection

    /// <summary>
    /// FE5.3 bulk catalogue edits. Gated on <see cref="PermissionCatalogue.InventoryBulk"/> — one
    /// call here can move thousands of rows, so it is deliberately NOT part of the general
    /// stock-adjust permission.
    ///
    /// Two selection modes, both explicit in the API so the UI can be honest about scope: an id list
    /// (what's ticked on screen) or <see cref="BulkCriteria"/> (everything matching the operator's
    /// current filter, resolved SERVER-side — the client never has to have loaded the rows).
    ///
    /// Deliberate limits:
    ///   • price and VAT band are NOT bulk-editable — the per-item guardrail that keeps VAT sane
    ///     (VAT-Investigation §5.4) would be meaningless applied 5,000 rows at a time;
    ///   • "clear category" is impossible (Item.CatId is required), so it means "move to a
    ///     per-tenant Uncategorised category", created on first use;
    ///   • nothing is ever hard-deleted — "bin" is a reversible soft delete;
    ///   • a single call is capped at <see cref="MaxItems"/> so a mis-built filter can't run away.
    /// Every call is audited with the criteria AND the affected ids.
    /// </summary>
    [ApiController]
    public sealed class InventoryBulkController : ControllerBase
    {
        /// <summary>Refuse absurd batches — refine the filter instead.</summary>
        public const int MaxItems = 10_000;
        public const string UncategorisedName = "Uncategorised";

        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public InventoryBulkController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>How many items a criteria selection covers — the UI shows this in its
        /// confirmation ("Move 412 items to Comics?") BEFORE anything is changed.</summary>
        [HttpPost("api/v1/items/bulk/count")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.InventoryBulk)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Count([FromBody] BulkCriteria criteria, CancellationToken ct)
        {
            var count = await Matching(criteria).CountAsync(ct);
            return Ok(new { count, capped = count > MaxItems, max = MaxItems });
        }

        [HttpPost("api/v1/items/bulk")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.InventoryBulk)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Bulk([FromBody] BulkBody body, CancellationToken ct)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Action))
                return BadRequest(new { detail = "action is required." });
            var hasIds = body.Ids is { Count: > 0 };
            if (hasIds == (body.Criteria != null))
                return BadRequest(new { detail = "supply EITHER ids or criteria, not both." });

            // resolve the target set
            List<Item> items;
            if (hasIds)
            {
                if (body.Ids.Count > MaxItems)
                    return Conflict(new { detail = $"{body.Ids.Count} items exceeds the {MaxItems} per-call limit." });
                items = await _db.Items.Where(i => body.Ids.Contains(i.IdOne)).ToListAsync(ct);
            }
            else
            {
                var total = await Matching(body.Criteria).CountAsync(ct);
                if (total > MaxItems)
                    return Conflict(new { detail = $"That filter matches {total} items, over the {MaxItems} per-call limit. Narrow it." });
                items = await Matching(body.Criteria).ToListAsync(ct);
            }
            if (items.Count == 0) return Ok(new { affected = 0, ids = Array.Empty<string>() });

            _db.CurrentUser = Actor.ToString();
            // Prior values go in the audit payload so a mistaken bulk can be reversed by hand —
            // there is no undo button (FE5 decision), but the trail is complete.
            var before = items.Select(i => new { i.IdOne, i.CatId, i.Brand, i.BinnedAtUtc, i.StockUntracked }).ToList();
            string detail;

            switch (body.Action.Trim().ToLowerInvariant())
            {
                case "set-category":
                {
                    if (body.CategoryId is not Guid target)
                        return BadRequest(new { detail = "categoryId is required for set-category." });
                    if (!await _db.Category.AnyAsync(c => c.IdOne == target, ct))
                        return BadRequest(new { detail = "unknown categoryId." });
                    foreach (var i in items) i.CatId = target;
                    detail = $"category → {target}";
                    break;
                }
                case "clear-category":
                {
                    // Item.CatId is REQUIRED, so "remove from category" = move to Uncategorised.
                    var uncategorised = await EnsureUncategorisedAsync(ct);
                    foreach (var i in items) i.CatId = uncategorised;
                    detail = "category → Uncategorised";
                    break;
                }
                case "set-brand":
                {
                    if (string.IsNullOrWhiteSpace(body.Value))
                        return BadRequest(new { detail = "value (brand) is required for set-brand." });
                    var brand = body.Value.Trim();
                    foreach (var i in items) i.Brand = brand;
                    detail = $"brand → {brand}";
                    break;
                }
                case "clear-brand":
                    // "-" is the codebase's existing empty-brand sentinel (the UI renders it blank).
                    foreach (var i in items) i.Brand = "-";
                    detail = "brand cleared";
                    break;
                case "bin":
                    foreach (var i in items) i.BinnedAtUtc ??= DateTime.UtcNow;
                    detail = "moved to the Bin";
                    break;
                case "restore":
                    foreach (var i in items) i.BinnedAtUtc = null;
                    detail = "restored from the Bin";
                    break;
                case "set-untracked":
                    foreach (var i in items) i.StockUntracked = true;
                    detail = "stock no longer tracked";
                    break;
                case "clear-untracked":
                    foreach (var i in items) i.StockUntracked = false;
                    detail = "stock tracked again";
                    break;
                default:
                    return BadRequest(new { detail = $"unknown action '{body.Action}'." });
            }

            var ids = items.Select(i => i.IdOne).ToList();
            _db.Audit(_tenant.TenantId, Actor, $"inventory.bulk.{body.Action.Trim().ToLowerInvariant()}",
                nameof(Item), $"{ids.Count} items",
                new { detail, criteria = body.Criteria, count = ids.Count, ids, before });
            await _db.SaveChangesAsync(ct);
            return Ok(new { affected = ids.Count, detail, ids });
        }

        /// <summary>The criteria query — the SAME filter the list endpoints use, so "everything
        /// matching what I'm looking at" really does mean that (including the bin rule).</summary>
        private IQueryable<Item> Matching(BulkCriteria c)
        {
            var p = new Plutus.Repository.QueryParameters.ItemParameters
            {
                Search = c?.Search,
                MatchAllWords = c?.MatchAllWords ?? false,
                CatId = c?.CatId,
                Binned = c?.Binned ?? false,
            };
            return _db.Items.Where(p.GetExpression());
        }

        /// <summary>The per-tenant "Uncategorised" bucket, created on first use.</summary>
        private async Task<Guid> EnsureUncategorisedAsync(CancellationToken ct)
        {
            var existing = await _db.Category
                .FirstOrDefaultAsync(c => c.Name == UncategorisedName, ct);
            if (existing != null) return existing.IdOne;

            var businessId = await _db.Items.Select(i => i.IdTwo).FirstOrDefaultAsync(ct);
            if (businessId == Guid.Empty)
                businessId = await _db.Business.Select(b => b.Id).FirstAsync(ct);
            var cat = new Category { IdOne = Uuid7.New(), IdTwo = businessId, Name = UncategorisedName, Description = "Items with no category" };
            _db.Category.Add(cat);
            return cat.IdOne;
        }
    }
}
