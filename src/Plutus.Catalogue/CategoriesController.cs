#nullable disable

using System;
using System.Linq;
using System.Security.Claims;
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
    public sealed record CategoryWriteBody(string Name, string Description);
    public sealed record ReassignBody(Guid ToId);

    /// <summary>
    /// WP4.4 category manager (webstore-critical). A tenant-scoped, GUARDED alternative to the raw
    /// legacy api/Category CRUD — whose DELETE is configured OnDelete(Cascade) on Item→Category, so
    /// deleting a category there silently deletes every item in it (and their sale lines/stock). That
    /// is unacceptable for a live webstore, so category management goes through here instead:
    ///   • DELETE refuses (409) while any item still references the category, and refuses the LAST
    ///     category (Item.CatId is [Required] — an item must always have one). Reassign first.
    ///   • POST .../reassign bulk-moves items between categories so a delete can proceed.
    /// Reads are gated on portal.reports.view (the Inventory page already needs it); writes on
    /// portal.stock.adjust (the inventory-management permission). Tenant scoping is by the shadow
    /// TenantId column (auto-filtered + auto-stamped); IdTwo is the legacy businessId every item in
    /// the tenant shares, resolved once from the tenant's Business.
    /// </summary>
    [ApiController]
    public sealed class CategoriesController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public CategoriesController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        // The legacy businessId = the Category/Item composite-key IdTwo. Tenant scoping is a separate
        // shadow column; IdTwo is legacy data every row in this tenant shares, so read it off Business.
        private Task<Guid> BusinessIdAsync() =>
            _db.Business.AsNoTracking().Select(b => b.Id).FirstOrDefaultAsync();

        [HttpGet("api/v1/categories")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List()
        {
            var cats = await _db.Category.AsNoTracking()
                .OrderBy(c => c.Name).Select(c => new { c.IdOne, c.Name, c.Description }).ToListAsync();
            var counts = (await _db.Items.AsNoTracking()
                    .GroupBy(i => i.CatId).Select(g => new { CatId = g.Key, N = g.Count() }).ToListAsync())
                .ToDictionary(x => x.CatId, x => x.N);
            return Ok(cats.Select(c => new
            {
                id = c.IdOne,
                name = c.Name,
                description = c.Description,
                itemCount = counts.TryGetValue(c.IdOne, out var n) ? n : 0,
            }));
        }

        [HttpPost("api/v1/categories")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] CategoryWriteBody body)
        {
            var name = body?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { detail = "name is required." });
            var businessId = await BusinessIdAsync();
            if (businessId == Guid.Empty) return BadRequest(new { detail = "No business is configured for this tenant." });
            if (await _db.Category.AnyAsync(c => c.Name == name))
                return Conflict(new { detail = $"A category named '{name}' already exists." });

            _db.CurrentUser = Actor.ToString();
            var cat = new Category
            {
                IdOne = Uuid7.New(),
                IdTwo = businessId,
                Name = name,
                // Description is [Required] and validated on save (RepositoryContext.SaveMethods) — an
                // empty string fails, so default it to the name when the caller leaves it blank.
                Description = string.IsNullOrWhiteSpace(body.Description) ? name : body.Description.Trim(),
            };
            _db.Category.Add(cat); // shadow TenantId auto-stamped from the ambient tenant on save
            _db.Audit(_tenant.TenantId, Actor, "category.create", nameof(Category), cat.IdOne.ToString(), new { name });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/categories/{cat.IdOne}", new { id = cat.IdOne, name = cat.Name });
        }

        [HttpPut("api/v1/categories/{id}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Rename([FromRoute] Guid id, [FromBody] CategoryWriteBody body)
        {
            var name = body?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { detail = "name is required." });
            var cat = await _db.Category.FirstOrDefaultAsync(c => c.IdOne == id);
            if (cat == null) return NotFound();
            if (await _db.Category.AnyAsync(c => c.Name == name && c.IdOne != id))
                return Conflict(new { detail = $"A category named '{name}' already exists." });

            _db.CurrentUser = Actor.ToString();
            var old = cat.Name;
            cat.Name = name;
            if (!string.IsNullOrWhiteSpace(body.Description)) cat.Description = body.Description.Trim();
            _db.Audit(_tenant.TenantId, Actor, "category.rename", nameof(Category), id.ToString(), new { from = old, to = name });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Bulk-move every item from one category to another (so the source can be deleted).</summary>
        [HttpPost("api/v1/categories/{id}/reassign")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Reassign([FromRoute] Guid id, [FromBody] ReassignBody body)
        {
            if (body == null || body.ToId == Guid.Empty || body.ToId == id)
                return BadRequest(new { detail = "toId must be a different category." });
            var from = await _db.Category.FirstOrDefaultAsync(c => c.IdOne == id);
            var to = await _db.Category.FirstOrDefaultAsync(c => c.IdOne == body.ToId);
            if (from == null || to == null) return NotFound();

            _db.CurrentUser = Actor.ToString();
            var items = await _db.Items.Where(i => i.CatId == id).ToListAsync();
            foreach (var it in items) it.CatId = body.ToId;
            _db.Audit(_tenant.TenantId, Actor, "category.reassign", nameof(Category), id.ToString(),
                new { toId = body.ToId, moved = items.Count });
            await _db.SaveChangesAsync();
            return Ok(new { moved = items.Count });
        }

        [HttpDelete("api/v1/categories/{id}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var cat = await _db.Category.FirstOrDefaultAsync(c => c.IdOne == id);
            if (cat == null) return NotFound();
            // GUARD the legacy cascade: never let a delete take items (and their sale lines/stock) with it.
            var itemCount = await _db.Items.CountAsync(i => i.CatId == id);
            if (itemCount > 0)
                return Conflict(new { detail = $"{itemCount} item(s) are still in this category — reassign them first." });
            if (await _db.Category.CountAsync() <= 1)
                return Conflict(new { detail = "This is the last category; items must always have one. Create another first." });

            _db.CurrentUser = Actor.ToString();
            _db.Category.Remove(cat);
            _db.Audit(_tenant.TenantId, Actor, "category.delete", nameof(Category), id.ToString(), new { cat.Name });
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
