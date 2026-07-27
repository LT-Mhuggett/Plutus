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
    public sealed record SupplierBody(string Name, string Email, string Phone);
    public sealed record CreatePOBody(Guid SupplierId, int? StoreId, string Reference, string Notes, List<POLineBody> Lines);
    public sealed record POLineBody(string ItemIdOne, int Qty, long UnitCostPence);
    public sealed record ReceiveBody(List<ReceiveLine> Lines);
    public sealed record ReceiveLine(Guid POLineId, int Qty, long? UnitCostPence);

    /// <summary>
    /// WP5.3 goods-in (architecture §9.4): suppliers, purchase orders, receiving. A receipt
    /// posts RECEIPT movements into the WP5.1 ledger (RefId = the PO id); partial receipts
    /// are supported (status Open → PartiallyReceived → Received) and the actual unit cost
    /// can be captured at the door. Gated on portal.stock.adjust; every mutation audited.
    /// </summary>
    [ApiController]
    [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust)]
    public sealed class GoodsInController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public GoodsInController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        // ── suppliers ──

        [HttpGet("api/v1/suppliers")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Suppliers() =>
            Ok(await _db.Suppliers.AsNoTracking().OrderBy(s => s.Name)
                .Select(s => new { id = s.Id, name = s.Name, email = s.Email, phone = s.Phone, active = s.Active })
                .ToListAsync());

        [HttpPost("api/v1/suppliers")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateSupplier([FromBody] SupplierBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.Name)) return BadRequest(new { detail = "name is required." });
            _db.CurrentUser = Actor.ToString();
            var supplier = new Supplier
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, Name = body.Name.Trim(),
                Email = body.Email?.Trim(), Phone = body.Phone?.Trim(),
                Active = true, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.Suppliers.Add(supplier);
            _db.Audit(_tenant.TenantId, Actor, "supplier.create", nameof(Supplier), supplier.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/suppliers/{supplier.Id}", new { id = supplier.Id });
        }

        // ── purchase orders ──

        [HttpGet("api/v1/purchase-orders")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] string status)
        {
            var q = _db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).AsQueryable();
            if (Enum.TryParse<PurchaseOrderStatus>(status, true, out var s))
                q = q.Where(p => p.Status == s);
            return Ok(await q.OrderByDescending(p => p.CreatedAtUtc).Take(200)
                .Select(p => new
                {
                    id = p.Id, supplierId = p.SupplierId, storeId = p.StoreId,
                    status = p.Status.ToString(), reference = p.Reference, notes = p.Notes,
                    createdAtUtc = p.CreatedAtUtc,
                    lines = p.Lines.Select(l => new
                    {
                        id = l.Id, itemIdOne = l.ItemIdOne, qtyOrdered = l.QtyOrdered,
                        qtyReceived = l.QtyReceived, unitCostPence = l.UnitCostPence,
                    }),
                })
                .ToListAsync());
        }

        [HttpPost("api/v1/purchase-orders")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CreatePOBody body)
        {
            if (body?.Lines == null || body.Lines.Count == 0)
                return BadRequest(new { detail = "at least one line is required." });
            if (body.Lines.Any(l => l.Qty <= 0 || l.UnitCostPence < 0))
                return BadRequest(new { detail = "line qty must be positive and cost non-negative." });
            if (await _db.Suppliers.AllAsync(s => s.Id != body.SupplierId))
                return BadRequest(new { detail = "Unknown supplierId." });

            var storeId = body.StoreId
                ?? await _db.Stores.AsNoTracking().Select(s => (int?)s.Id).FirstOrDefaultAsync();
            if (storeId == null) return BadRequest(new { detail = "The tenant has no store." });

            var itemIds = body.Lines.Select(l => l.ItemIdOne).Distinct().ToList();
            var items = await _db.Items.AsNoTracking()
                .Where(i => itemIds.Contains(i.IdOne)).ToDictionaryAsync(i => i.IdOne);
            var unknown = itemIds.FirstOrDefault(id => !items.ContainsKey(id));
            if (unknown != null) return BadRequest(new { detail = $"Unknown itemIdOne '{unknown}'." });

            _db.CurrentUser = Actor.ToString();
            var po = new PurchaseOrder
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, SupplierId = body.SupplierId,
                StoreId = storeId.Value, Status = PurchaseOrderStatus.Open,
                Reference = body.Reference?.Trim(), Notes = body.Notes?.Trim(),
                CreatedBy = Actor, CreatedAtUtc = DateTime.UtcNow,
                Lines = body.Lines.Select(l => new POLine
                {
                    Id = Uuid7.New(), TenantId = _tenant.TenantId,
                    ItemIdOne = l.ItemIdOne,
                    ItemId = DeterministicGuid.ForItem(items[l.ItemIdOne].IdTwo, l.ItemIdOne),
                    QtyOrdered = l.Qty, QtyReceived = 0, UnitCostPence = l.UnitCostPence,
                }).ToList(),
            };
            _db.PurchaseOrders.Add(po);
            _db.Audit(_tenant.TenantId, Actor, "po.create", nameof(PurchaseOrder), po.Id.ToString(),
                new { body.SupplierId, storeId, body.Reference, lines = body.Lines.Count });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/purchase-orders/{po.Id}", new { id = po.Id });
        }

        /// <summary>Receive against the PO (partials fine): each line posts a RECEIPT
        /// movement at the destination store's location; the actual unit cost may override
        /// the ordered cost. Over-receiving beyond the outstanding quantity is a 400.</summary>
        [HttpPost("api/v1/purchase-orders/{id}/receive")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Receive([FromRoute] Guid id, [FromBody] ReceiveBody body)
        {
            if (body?.Lines == null || body.Lines.Count == 0)
                return BadRequest(new { detail = "lines are required." });

            var po = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id);
            if (po == null) return NotFound();
            if (po.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
                return Conflict(new { detail = $"Purchase order is {po.Status}." });

            // validate everything before any mutation
            foreach (var line in body.Lines)
            {
                var poLine = po.Lines.FirstOrDefault(l => l.Id == line.POLineId);
                if (poLine == null) return BadRequest(new { detail = $"Unknown poLineId {line.POLineId}." });
                if (line.Qty <= 0) return BadRequest(new { detail = "receive qty must be positive." });
                if (poLine.QtyReceived + line.Qty > poLine.QtyOrdered)
                    return BadRequest(new
                    {
                        detail = $"Line {poLine.ItemIdOne}: receiving {line.Qty} exceeds outstanding " +
                                 $"{poLine.QtyOrdered - poLine.QtyReceived}.",
                    });
            }

            _db.CurrentUser = Actor.ToString();
            var service = new StockLedgerService(_db);
            var location = await service.EnsureStoreLocationAsync(_tenant.TenantId, po.StoreId);

            foreach (var line in body.Lines)
            {
                var poLine = po.Lines.First(l => l.Id == line.POLineId);
                poLine.QtyReceived += line.Qty;
                if (line.UnitCostPence.HasValue) poLine.UnitCostPence = line.UnitCostPence.Value; // cost at the door
                await service.ApplyAsync(_tenant.TenantId, location, poLine.ItemIdOne, poLine.ItemId,
                    StockMovementType.Receipt, line.Qty,
                    $"PO {(string.IsNullOrEmpty(po.Reference) ? po.Id.ToString()[..8] : po.Reference)} receipt",
                    po.Id, Actor);
            }

            po.Status = po.Lines.All(l => l.QtyReceived >= l.QtyOrdered)
                ? PurchaseOrderStatus.Received
                : PurchaseOrderStatus.PartiallyReceived;

            _db.Audit(_tenant.TenantId, Actor, "po.receive", nameof(PurchaseOrder), id.ToString(),
                new { lines = body.Lines.Count, status = po.Status.ToString() });
            await _db.SaveChangesAsync();
            return Ok(new
            {
                id,
                status = po.Status.ToString(),
                lines = po.Lines.Select(l => new { l.ItemIdOne, l.QtyOrdered, l.QtyReceived, l.UnitCostPence }),
            });
        }

        [HttpPost("api/v1/purchase-orders/{id}/cancel")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Cancel([FromRoute] Guid id)
        {
            var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
            if (po == null) return NotFound();
            if (po.Status != PurchaseOrderStatus.Open)
                return Conflict(new { detail = $"Only an Open order can be cancelled (this one is {po.Status})." });
            _db.CurrentUser = Actor.ToString();
            po.Status = PurchaseOrderStatus.Cancelled;
            _db.Audit(_tenant.TenantId, Actor, "po.cancel", nameof(PurchaseOrder), id.ToString());
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
