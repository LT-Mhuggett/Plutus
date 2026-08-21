#nullable disable

using System;
using System.Collections.Generic;
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
    public sealed record ItemBarcodeBody(string Code);

    /// <summary>
    /// Additional barcodes for an item — the multi-barcode feature
    /// (`Build/archive/Multi-barcode plan.md`, MB2).
    ///
    /// ⚠⚠ MATT, 2026-08-20: *"I know the till currently uses the barcode as a unique entry at the
    /// moment, but I need to move to having multiple barcodes."* This reverses the 2026-08-09 ruling
    /// recorded in `TillStore`/`LocalSchema`, whose own note said what real support would need
    /// first: *"a server entity, a feed field and a portal UI."* This is that entity.
    ///
    /// ⚠⚠ AN ALIAS RESOLVES TO AN ITEM; IT IS NEVER AN IDENTITY (plan D1/D2). `Item.IdOne` stays the
    /// item's identity, and a resolver hands on the item's OWN `IdOne` — never the scanned alias.
    /// Two silent faults punish a leak: an unrecognised id makes `StockProjectionConsumer` create a
    /// phantom `StockLevel` with no error, and makes `VatBandStamp` leave the line's VAT band null.
    ///
    /// ⚠ WRITES ARE PORTAL-ONLY (plan D4): `portal.stock.adjust`, matching the closest precedent —
    /// category create/edit/delete on `CategoriesController`. A till never writes master data
    /// (single-writer, catalogue-sync design §1.2). ⚠ The LIST read is `[Authorize]` only, because
    /// the WEB TILL pulls it with an operator token during its catalogue sync — the same reasoning as
    /// `carrier-bags` and `payments/gateway/active`.
    ///
    /// ⚠⚠ EVERY ALIAS WRITE TOUCHES THE PARENT ITEM'S `ModifiedAt`, or no till ever hears about it:
    /// the catalogue feed's cursor is over `Item`, so an alias table gets NO feed coverage for free.
    /// That is exactly the lesson `PricingService.TouchItemForSyncAsync` exists to carry, and
    /// `An_alias_write_reaches_the_till_feed` is the test that keeps it true.
    /// </summary>
    [ApiController]
    public sealed class ItemBarcodesController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public ItemBarcodesController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>
        /// Every alias in this tenant — what the WEB TILL caches during its catalogue sync.
        ///
        /// ⚠ The whole set in one call, deliberately: the web till does a full catalogue re-pull on
        /// every boot (it holds no cursor), so a per-item endpoint would mean one request per item.
        /// The portal filters this same list client-side for one item's editor rather than adding a
        /// second endpoint for one consumer.
        ///
        /// ⚠ `[Authorize]` only — a till reads this. It carries no customer data and no money; it is
        /// the shop's own barcodes, which are printed on the products.
        /// </summary>
        [HttpGet("api/v1/items/barcodes")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List()
        {
            var rows = await _db.ItemBarcodes.AsNoTracking()
                .OrderBy(b => b.Code)
                .Select(b => new { code = b.Code, itemIdOne = b.ItemIdOne })
                .ToListAsync();

            return Ok(rows);
        }

        /// <summary>
        /// Give an item another barcode.
        ///
        /// ⚠⚠ THE WRITER IS STRICT SO THE READERS CAN BE SIMPLE — the opening-hours lesson applied
        /// deliberately rather than learned again. Every refusal below is a sentence a person can act
        /// on, because the alternative (saving something a till cannot use) looks identical to
        /// "the feature is broken".
        ///
        /// ⚠ Re-adding the same alias to the same item is a 204 NO-OP, not a conflict: the portal's
        /// natural retry after a dropped response must not read as an error.
        /// </summary>
        [HttpPost("api/v1/items/{itemIdOne}/barcodes")]
        // ⚠⚠ WIDENED 2026-08-21 (WP10). `portal.stock.adjust` ALONE made this unreachable from a till:
        // a **Supervisor holds no portal permission at all**, so the moment MAUI grew a barcode
        // section every supervisor would have met a 403 — and Matt had just ruled that editing an
        // item is theirs. ⚠ `pos.items.manage` is the right till code because **a barcode is the
        // item's IDENTITY**, not its quantity: it decides what scans to it in every shop on the
        // estate. ⚠ It is also what the "Add/edit stock" role carries, so an individual granted
        // that role can correct a barcode — which is the point of the role.
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust + "," + PermissionCatalogue.PosItemsManage)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Add([FromRoute] string itemIdOne, [FromBody] ItemBarcodeBody body)
        {
            var code = ItemBarcodeRules.Normalise(body?.Code);

            // ⚠ The shared rule first — it owns "what may be a barcode at all", including the
            // reserved shapes a scan could never reach (member cards, gift cards, bags, and the two
            // platform ids). Its sentences are the ones the portal shows.
            if (ItemBarcodeRules.WhyRefused(code) is string refusal)
                return BadRequest(new { detail = refusal });

            var item = await _db.Items.AsNoTracking()
                .Where(i => i.IdOne == itemIdOne)
                .Select(i => new { i.IdOne, i.IdTwo, i.Name, i.BinnedAtUtc })
                .FirstOrDefaultAsync();

            if (item is null)
                return NotFound(new { detail = "That item no longer exists." });

            // ⚠ Plan D10: a binned item's barcodes do not scan, so adding one is configuring
            // something that cannot work. Say what to do instead.
            if (item.BinnedAtUtc != null)
                return BadRequest(new
                {
                    detail = "That item is in the Bin, so nothing scans to it. Restore it first, "
                           + "then add the barcode.",
                });

            if (string.Equals(code, item.IdOne, StringComparison.Ordinal))
                return BadRequest(new { detail = "That is already this item's own barcode." });

            // ⚠⚠ THE OTHER NAMESPACE. The unique index covers alias-vs-alias; it cannot see
            // `Items.IdOne`. Without this check an alias could shadow a real item's own barcode and
            // two rows would answer one scan — unresolvable at a counter.
            var clashingItem = await _db.Items.AsNoTracking()
                .Where(i => i.IdOne == code)
                .Select(i => i.Name)
                .FirstOrDefaultAsync();

            if (clashingItem != null)
                return Conflict(new
                {
                    detail = $"That is already the barcode of '{clashingItem}'.",
                });

            var existing = await _db.ItemBarcodes
                .FirstOrDefaultAsync(b => b.Code == code);

            if (existing != null)
            {
                // Same item, same code — the caller already has what they asked for.
                if (string.Equals(existing.ItemIdOne, item.IdOne, StringComparison.Ordinal))
                    return NoContent();

                var ownerName = await _db.Items.AsNoTracking()
                    .Where(i => i.IdOne == existing.ItemIdOne)
                    .Select(i => i.Name)
                    .FirstOrDefaultAsync();

                return Conflict(new
                {
                    detail = $"That barcode already points at '{ownerName ?? existing.ItemIdOne}'. "
                           + "Remove it from that item first.",
                });
            }

            _db.CurrentUser = Actor.ToString();

            _db.ItemBarcodes.Add(new ItemBarcode
            {
                Id = Uuid7.New(),
                TenantId = _tenant.TenantId,
                Code = code,
                ItemIdOne = item.IdOne,
                BusinessId = item.IdTwo,
                CreatedAtUtc = DateTime.UtcNow,
            });

            await TouchItemForSyncAsync(item.IdOne);

            _db.Audit(_tenant.TenantId, Actor, "catalogue.item-barcode", nameof(ItemBarcode), code,
                new { itemIdOne = item.IdOne });

            await _db.SaveChangesAsync();
            return Created($"/api/v1/items/{item.IdOne}/barcodes/{Uri.EscapeDataString(code)}",
                new { code, itemIdOne = item.IdOne });
        }

        /// <summary>
        /// Correct a mistyped barcode — rename it in place.
        ///
        /// ⚠⚠ ONE CALL, NOT DELETE-THEN-ADD, and that is the whole reason this endpoint exists. Matt
        /// asked for editable barcodes *"incase you misstype it"*; done client-side as two calls, a
        /// failure between them (a dropped connection, a refusal on the new code) would leave the item
        /// with NEITHER code — the old one deleted and the new one never added. Renaming inside one
        /// transaction cannot half-happen.
        ///
        /// ⚠ Validated exactly as an add is: same shared rule, same clash checks, same sentences.
        /// </summary>
        [HttpPut("api/v1/items/{itemIdOne}/barcodes/{code}")]
        // ⚠⚠ WIDENED 2026-08-21 (WP10). `portal.stock.adjust` ALONE made this unreachable from a till:
        // a **Supervisor holds no portal permission at all**, so the moment MAUI grew a barcode
        // section every supervisor would have met a 403 — and Matt had just ruled that editing an
        // item is theirs. ⚠ `pos.items.manage` is the right till code because **a barcode is the
        // item's IDENTITY**, not its quantity: it decides what scans to it in every shop on the
        // estate. ⚠ It is also what the "Add/edit stock" role carries, so an individual granted
        // that role can correct a barcode — which is the point of the role.
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust + "," + PermissionCatalogue.PosItemsManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Rename(
            [FromRoute] string itemIdOne, [FromRoute] string code, [FromBody] ItemBarcodeBody body)
        {
            var from = ItemBarcodeRules.Normalise(code);
            var to = ItemBarcodeRules.Normalise(body?.Code);

            if (ItemBarcodeRules.WhyRefused(to) is string refusal)
                return BadRequest(new { detail = refusal });

            var row = await _db.ItemBarcodes.FirstOrDefaultAsync(b => b.Code == from && b.ItemIdOne == itemIdOne);
            if (row is null) return NotFound(new { detail = "That barcode is no longer on this item." });

            // Unchanged — nothing to do, and not an error.
            if (string.Equals(from, to, StringComparison.Ordinal)) return NoContent();

            var item = await _db.Items.AsNoTracking()
                .Where(i => i.IdOne == itemIdOne)
                .Select(i => new { i.IdOne, i.Name })
                .FirstOrDefaultAsync();

            if (item is null) return NotFound(new { detail = "That item no longer exists." });

            if (string.Equals(to, item.IdOne, StringComparison.Ordinal))
                return BadRequest(new { detail = "That is already this item's own barcode." });

            var clashingItem = await _db.Items.AsNoTracking()
                .Where(i => i.IdOne == to).Select(i => i.Name).FirstOrDefaultAsync();
            if (clashingItem != null)
                return Conflict(new { detail = $"That is already the barcode of '{clashingItem}'." });

            var clashingAlias = await _db.ItemBarcodes.AsNoTracking()
                .Where(b => b.Code == to).Select(b => b.ItemIdOne).FirstOrDefaultAsync();
            if (clashingAlias != null)
            {
                var ownerName = await _db.Items.AsNoTracking()
                    .Where(i => i.IdOne == clashingAlias).Select(i => i.Name).FirstOrDefaultAsync();
                return Conflict(new
                {
                    detail = $"That barcode already points at '{ownerName ?? clashingAlias}'. "
                           + "Remove it from that item first.",
                });
            }

            _db.CurrentUser = Actor.ToString();
            row.Code = to;

            await TouchItemForSyncAsync(itemIdOne);

            _db.Audit(_tenant.TenantId, Actor, "catalogue.item-barcode.rename", nameof(ItemBarcode), to,
                new { itemIdOne, from, to });

            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Take a barcode off an item.
        ///
        /// ⚠ HARD DELETE, not a tombstone, and that is a decision (plan D5). Nothing in history
        /// references an alias row — a sale line stores the scanned code as a STRING snapshot, with
        /// no foreign key — so there is nothing to orphan. Removal reaches tills as a smaller set on
        /// the item, which is how the feed already carries "this changed".
        ///
        /// ⚠ Idempotent: removing a code that is not there is 204, not 404. The state asked for is
        /// the state that exists.
        /// </summary>
        [HttpDelete("api/v1/items/{itemIdOne}/barcodes/{code}")]
        // ⚠⚠ WIDENED 2026-08-21 (WP10). `portal.stock.adjust` ALONE made this unreachable from a till:
        // a **Supervisor holds no portal permission at all**, so the moment MAUI grew a barcode
        // section every supervisor would have met a 403 — and Matt had just ruled that editing an
        // item is theirs. ⚠ `pos.items.manage` is the right till code because **a barcode is the
        // item's IDENTITY**, not its quantity: it decides what scans to it in every shop on the
        // estate. ⚠ It is also what the "Add/edit stock" role carries, so an individual granted
        // that role can correct a barcode — which is the point of the role.
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust + "," + PermissionCatalogue.PosItemsManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Remove([FromRoute] string itemIdOne, [FromRoute] string code)
        {
            var normalised = ItemBarcodeRules.Normalise(code);
            if (normalised is null) return NoContent();

            var row = await _db.ItemBarcodes
                .FirstOrDefaultAsync(b => b.Code == normalised && b.ItemIdOne == itemIdOne);

            if (row is null) return NoContent();

            _db.CurrentUser = Actor.ToString();
            _db.ItemBarcodes.Remove(row);

            await TouchItemForSyncAsync(itemIdOne);

            _db.Audit(_tenant.TenantId, Actor, "catalogue.item-barcode.remove", nameof(ItemBarcode),
                normalised, new { itemIdOne });

            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Move the item's sync cursor so an alias change REACHES A TILL.
        ///
        /// ⚠⚠ WITHOUT THIS, MULTI-BARCODE IS INVISIBLE TO EVERY TILL. The catalogue feed pages by
        /// `Item.ModifiedAt` and aliases live in their own table, so a row inserted here changes
        /// nothing the feed can see. This is the identical trap `PricingService.TouchItemForSyncAsync`
        /// documents for prices, and the identical fix.
        ///
        /// ⚠ Deliberately does NOT call SaveChanges — the caller batches it with the alias row, so
        /// the cursor move and the change it advertises commit together or not at all.
        /// </summary>
        private async Task TouchItemForSyncAsync(string itemIdOne)
        {
            var item = await _db.Items.FirstOrDefaultAsync(i => i.IdOne == itemIdOne);
            if (item == null) return;

            // `RepositoryContext.SaveMethods()` stamps ModifiedAt for every IAuditable on save;
            // marking the entity Modified is what makes it do so for a row we did not otherwise edit.
            _db.Entry(item).State = EntityState.Modified;
        }
    }
}
