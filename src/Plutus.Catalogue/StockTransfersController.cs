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
    public sealed record TransferBody(Guid FromLocationId, Guid ToLocationId, string ItemIdOne, int Qty, string Reason);
    public sealed record StockTakeBody(Guid? StockLocationId, int? StoreId, string Reason, List<StockTakeCount> Counts);
    public sealed record StockTakeCount(string ItemIdOne, int Counted);

    /// <summary>
    /// WP5.2: paired transfers with an in-transit state, and stock takes that post
    /// counted-vs-expected adjustments with reason codes. All mutations audited; every
    /// quantity change goes through the WP5.1 ledger (level == ledger stays invariant).
    /// </summary>
    [ApiController]
    [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust)]
    public sealed class StockTransfersController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public StockTransfersController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpGet("api/v1/stock/transfers")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] string status)
        {
            var q = _db.StockTransfers.AsNoTracking();
            if (Enum.TryParse<StockTransferStatus>(status, true, out var s))
                q = q.Where(t => t.Status == s);
            return Ok(await q.OrderByDescending(t => t.CreatedAtUtc).Take(200)
                .Select(t => new
                {
                    id = t.Id, fromLocationId = t.FromLocationId, toLocationId = t.ToLocationId,
                    itemIdOne = t.ItemIdOne, qty = t.Qty, status = t.Status.ToString(),
                    reason = t.Reason, createdAtUtc = t.CreatedAtUtc, receivedAtUtc = t.ReceivedAtUtc,
                })
                .ToListAsync());
        }

        /// <summary>Dispatch: TRANSFER_OUT at the source; the goods are then in transit —
        /// in NEITHER location's level, so a transfer can never double-count.</summary>
        [HttpPost("api/v1/stock/transfers")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] TransferBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.ItemIdOne)) return BadRequest(new { detail = "itemIdOne is required." });
            if (body.Qty <= 0) return BadRequest(new { detail = "qty must be positive." });
            if (body.FromLocationId == body.ToLocationId) return BadRequest(new { detail = "from and to must differ." });
            var from = await _db.StockLocations.FirstOrDefaultAsync(l => l.Id == body.FromLocationId);
            var to = await _db.StockLocations.FirstOrDefaultAsync(l => l.Id == body.ToLocationId);
            if (from == null || to == null) return BadRequest(new { detail = "Unknown from/to location." });
            var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.IdOne == body.ItemIdOne);
            if (item == null) return BadRequest(new { detail = "Unknown itemIdOne." });

            _db.CurrentUser = Actor.ToString();
            var transfer = new StockTransfer
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId,
                FromLocationId = from.Id, ToLocationId = to.Id,
                ItemIdOne = body.ItemIdOne, ItemId = DeterministicGuid.ForItem(item.IdTwo, item.IdOne),
                Qty = body.Qty, Status = StockTransferStatus.InTransit,
                Reason = body.Reason?.Trim(), CreatedBy = Actor, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.StockTransfers.Add(transfer);

            var service = new StockLedgerService(_db);
            await service.ApplyAsync(_tenant.TenantId, from, transfer.ItemIdOne, transfer.ItemId,
                StockMovementType.TransferOut, -body.Qty, body.Reason?.Trim(), transfer.Id, Actor);

            _db.Audit(_tenant.TenantId, Actor, "stock.transfer.create", nameof(StockTransfer), transfer.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/stock/transfers/{transfer.Id}", new { id = transfer.Id });
        }

        /// <summary>Arrival: TRANSFER_IN at the destination; transfer completed.</summary>
        [HttpPost("api/v1/stock/transfers/{id}/receive")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Receive([FromRoute] Guid id)
        {
            var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == id);
            if (transfer == null) return NotFound();
            if (transfer.Status != StockTransferStatus.InTransit)
                return Conflict(new { detail = $"Transfer is {transfer.Status}." });
            var to = await _db.StockLocations.FirstAsync(l => l.Id == transfer.ToLocationId);

            _db.CurrentUser = Actor.ToString();
            transfer.Status = StockTransferStatus.Received;
            transfer.ReceivedBy = Actor;
            transfer.ReceivedAtUtc = DateTime.UtcNow;

            var service = new StockLedgerService(_db);
            await service.ApplyAsync(_tenant.TenantId, to, transfer.ItemIdOne, transfer.ItemId,
                StockMovementType.TransferIn, transfer.Qty, transfer.Reason, transfer.Id, Actor);

            _db.Audit(_tenant.TenantId, Actor, "stock.transfer.receive", nameof(StockTransfer), id.ToString());
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Cancel while in transit: the goods go back to the SOURCE.</summary>
        [HttpPost("api/v1/stock/transfers/{id}/cancel")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Cancel([FromRoute] Guid id)
        {
            var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.Id == id);
            if (transfer == null) return NotFound();
            if (transfer.Status != StockTransferStatus.InTransit)
                return Conflict(new { detail = $"Transfer is {transfer.Status}." });
            var from = await _db.StockLocations.FirstAsync(l => l.Id == transfer.FromLocationId);

            _db.CurrentUser = Actor.ToString();
            transfer.Status = StockTransferStatus.Cancelled;

            var service = new StockLedgerService(_db);
            await service.ApplyAsync(_tenant.TenantId, from, transfer.ItemIdOne, transfer.ItemId,
                StockMovementType.TransferIn, transfer.Qty, "transfer cancelled", transfer.Id, Actor);

            _db.Audit(_tenant.TenantId, Actor, "stock.transfer.cancel", nameof(StockTransfer), id.ToString());
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Stock take: for each counted item post counted−expected as a reasoned
        /// ADJUSTMENT (zero deltas produce no movement). Returns the variance report.</summary>
        [HttpPost("api/v1/stock/takes")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> StockTake([FromBody] StockTakeBody body)
        {
            if (body?.Counts == null || body.Counts.Count == 0)
                return BadRequest(new { detail = "counts are required." });

            var service = new StockLedgerService(_db);
            StockLocation location;
            if (body.StockLocationId.HasValue)
            {
                location = await _db.StockLocations.FirstOrDefaultAsync(l => l.Id == body.StockLocationId);
                if (location == null) return BadRequest(new { detail = "Unknown stockLocationId." });
            }
            else
            {
                var storeId = body.StoreId
                    ?? await _db.Stores.AsNoTracking().Select(s => (int?)s.Id).FirstOrDefaultAsync()
                    ?? 0;
                location = await service.EnsureStoreLocationAsync(_tenant.TenantId, storeId);
            }

            _db.CurrentUser = Actor.ToString();
            var results = new List<object>();
            foreach (var count in body.Counts.Where(c => !string.IsNullOrWhiteSpace(c.ItemIdOne)))
            {
                var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.IdOne == count.ItemIdOne);
                if (item == null)
                {
                    results.Add(new { itemIdOne = count.ItemIdOne, error = "unknown item" });
                    continue;
                }
                var expected = await _db.StockLevels
                    .Where(l => l.StockLocationId == location.Id && l.ItemIdOne == count.ItemIdOne)
                    .Select(l => (int?)l.Quantity).FirstOrDefaultAsync() ?? 0;
                var delta = count.Counted - expected;
                if (delta != 0)
                    await service.ApplyAsync(_tenant.TenantId, location, count.ItemIdOne,
                        DeterministicGuid.ForItem(item.IdTwo, item.IdOne),
                        StockMovementType.Adjustment, delta,
                        $"stock take: counted {count.Counted}, expected {expected}" +
                        (string.IsNullOrWhiteSpace(body.Reason) ? "" : $" — {body.Reason.Trim()}"),
                        null, Actor);
                results.Add(new { itemIdOne = count.ItemIdOne, expected, counted = count.Counted, delta });
            }

            _db.Audit(_tenant.TenantId, Actor, "stock.take", nameof(StockLocation), location.Id.ToString(),
                new { items = body.Counts.Count, body.Reason });
            await _db.SaveChangesAsync();
            return Ok(new { stockLocationId = location.Id, results });
        }
    }
}
