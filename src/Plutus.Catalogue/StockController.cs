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
    public sealed record MovementBody(
        Guid? StockLocationId, int? StoreId, string ItemIdOne, string Type, int Qty, string Reason);

    public sealed record CreateLocationBody(int StoreId, string Type, string Name);

    /// <summary>
    /// WP5.1 stock ledger API. Reads gated on portal.reports.view (stock views are a portal
    /// reporting surface); manual movements on portal.stock.adjust and audited; rebuild is
    /// platform-admin. Manual movement types here: Receipt | Adjustment | WriteOff — sales
    /// and returns come ONLY from the pipeline consumer, transfers arrive with WP5.2.
    /// </summary>
    [ApiController]
    public sealed class StockController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public StockController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpGet("api/v1/stock/levels")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Levels([FromQuery] Guid? locationId, [FromQuery] string search, [FromQuery] int take = 100)
        {
            take = Math.Clamp(take, 1, 500);
            var q = _db.StockLevels.AsNoTracking()
                .Where(l => locationId == null || l.StockLocationId == locationId)
                .Where(l => search == null || l.ItemIdOne.Contains(search));
            var rows = await q.OrderBy(l => l.ItemIdOne).Take(take).ToListAsync();
            var locations = await _db.StockLocations.AsNoTracking().ToListAsync();

            // item names from the legacy catalogue for display
            var ids = rows.Select(r => r.ItemIdOne).Distinct().ToList();
            var names = await _db.Items.AsNoTracking()
                .Where(i => ids.Contains(i.IdOne))
                .Select(i => new { i.IdOne, i.Name })
                .ToListAsync();

            return Ok(rows.Select(r => new
            {
                stockLocationId = r.StockLocationId,
                location = locations.FirstOrDefault(l => l.Id == r.StockLocationId)?.Name ?? "?",
                itemIdOne = r.ItemIdOne,
                name = names.FirstOrDefault(n => n.IdOne == r.ItemIdOne)?.Name,
                quantity = r.Quantity,
            }));
        }

        [HttpGet("api/v1/stock/movements")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Movements(
            [FromQuery] string itemIdOne, [FromQuery] Guid? locationId, [FromQuery] int take = 100)
        {
            take = Math.Clamp(take, 1, 500);
            var rows = await _db.StockMovements.AsNoTracking()
                .Where(m => itemIdOne == null || m.ItemIdOne == itemIdOne)
                .Where(m => locationId == null || m.StockLocationId == locationId)
                .OrderByDescending(m => m.AtUtc).Take(take)
                .Select(m => new
                {
                    id = m.Id, stockLocationId = m.StockLocationId, itemIdOne = m.ItemIdOne,
                    type = m.Type.ToString(), qtyDelta = m.QtyDelta, reason = m.Reason,
                    refId = m.RefId, actorUserId = m.ActorUserId, atUtc = m.AtUtc,
                })
                .ToListAsync();
            return Ok(rows);
        }

        [HttpGet("api/v1/stock/locations")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Locations() =>
            Ok(await _db.StockLocations.AsNoTracking()
                .Select(l => new { id = l.Id, storeId = l.StoreId, type = l.Type.ToString(), name = l.Name })
                .ToListAsync());

        /// <summary>WP11.3: create a stock location — notably a WAREHOUSE (the type existed but
        /// nothing could create one). Transfers/stock-takes work per-location, so it's usable at once.</summary>
        [HttpPost("api/v1/stock/locations")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateLocation([FromBody] CreateLocationBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.Name)) return BadRequest(new { detail = "name is required." });
            if (!Enum.TryParse<StockLocationType>(body.Type, true, out var type))
                return BadRequest(new { detail = "type must be Store | Warehouse." });
            if (!await _db.Stores.AsNoTracking().AnyAsync(s => s.Id == body.StoreId))
                return BadRequest(new { detail = "Unknown storeId." });

            // Tenant-unique location name (case-insensitive) — checked, like till/store names.
            var lowered = body.Name.Trim().ToLowerInvariant();
            if (await _db.StockLocations.AnyAsync(l => l.TenantId == _tenant.TenantId && l.Name.ToLower() == lowered))
                return Conflict(new { detail = $"A location named '{body.Name.Trim()}' already exists." });

            _db.CurrentUser = Actor.ToString();
            var loc = new StockLocation
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, StoreId = body.StoreId,
                Type = type, Name = body.Name.Trim(),
            };
            _db.StockLocations.Add(loc);
            _db.Audit(_tenant.TenantId, Actor, "stock.location.create", nameof(StockLocation), loc.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/stock/locations/{loc.Id}", new { id = loc.Id, name = loc.Name, type = loc.Type.ToString() });
        }

        /// <summary>Manual movement: Receipt (goods-in until WP5.3), Adjustment (± with
        /// reason), WriteOff (negative). Audited; level updated atomically.</summary>
        [HttpPost("api/v1/stock/movements")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalStockAdjust)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Post([FromBody] MovementBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.ItemIdOne)) return BadRequest(new { detail = "itemIdOne is required." });
            if (body.Qty == 0) return BadRequest(new { detail = "qty must be non-zero." });
            if (!Enum.TryParse<StockMovementType>(body.Type, true, out var type) ||
                type is not (StockMovementType.Receipt or StockMovementType.Adjustment or StockMovementType.WriteOff))
                return BadRequest(new { detail = "type must be Receipt | Adjustment | WriteOff." });
            if (type == StockMovementType.WriteOff && body.Qty > 0)
                return BadRequest(new { detail = "A write-off must have a negative qty." });
            if (string.IsNullOrWhiteSpace(body.Reason) && type != StockMovementType.Receipt)
                return BadRequest(new { detail = "reason is required for adjustments and write-offs." });

            var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.IdOne == body.ItemIdOne);
            if (item == null) return BadRequest(new { detail = "Unknown itemIdOne." });

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
            var movement = await service.ApplyAsync(
                _tenant.TenantId, location, body.ItemIdOne,
                DeterministicGuid.ForItem(item.IdTwo, item.IdOne),
                type, body.Qty, body.Reason?.Trim(), null, Actor);
            _db.Audit(_tenant.TenantId, Actor, "stock.movement", nameof(StockMovement), movement.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/stock/movements/{movement.Id}", new { id = movement.Id });
        }

        [HttpPost("api/v1/stock/rebuild")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Rebuild()
        {
            var tenantId = _tenant.TenantId == Guid.Empty ? WellKnownTenants.Kapow : _tenant.TenantId;
            var levels = await StockRebuilder.RebuildLevelsAsync(_db, tenantId);
            _db.Audit(tenantId, Actor, "stock.rebuild", nameof(StockLevel), "*", new { levels });
            await _db.SaveChangesAsync();
            return Ok(new { levels });
        }
    }
}
