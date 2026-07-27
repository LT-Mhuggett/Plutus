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
    public sealed record PolicyBody(string Policy);
    public sealed record CentralPriceBody(long PricePence, long ExPricePence, DateTime? EffectiveFromUtc);
    public sealed record OverridePriceBody(int StoreId, long PricePence, long ExPricePence, DateTime? EffectiveFromUtc);
    public sealed record ForceResetBody(int? StoreId);

    /// <summary>
    /// WP5.4 pricing surface (architecture §7.4). Writes gated on portal.prices.manage and
    /// audited (who, when, old→new, scope); the effective-price read is open to any
    /// authenticated principal (tills consume it with device tokens on catalogue sync).
    /// </summary>
    [ApiController]
    public sealed class PricesController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public PricesController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>Effective prices for a batch of items at a store, now (or at= for
        /// previewing a scheduled change). items = comma-separated ItemIdOne list.</summary>
        [HttpGet("api/v1/prices/effective")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Effective([FromQuery] string items, [FromQuery] int? storeId, [FromQuery] DateTime? at)
        {
            if (string.IsNullOrWhiteSpace(items)) return BadRequest(new { detail = "items is required (comma-separated)." });
            var ids = items.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ids.Length > 500) return BadRequest(new { detail = "max 500 items per call." });
            var store = storeId
                ?? await _db.Stores.AsNoTracking().Select(s => (int?)s.Id).FirstOrDefaultAsync() ?? 0;
            var resolved = await new PricingService(_db).ResolveManyAsync(ids, store, at ?? DateTime.UtcNow);
            return Ok(resolved.Select(p => new
            {
                itemIdOne = p.ItemIdOne, pricePence = p.PricePence, exPricePence = p.ExPricePence,
                source = p.Source, policy = p.Policy.ToString(),
            }));
        }

        /// <summary>Editor view: policy, central history, live overrides for one item.</summary>
        [HttpGet("api/v1/prices/{itemIdOne}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Detail([FromRoute] string itemIdOne)
        {
            var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.IdOne == itemIdOne);
            if (item == null) return NotFound();
            var service = new PricingService(_db);
            return Ok(new
            {
                itemIdOne,
                name = item.Name,
                legacyPricePence = (long)Math.Round(item.Price * 100),
                policy = (await service.PolicyForAsync(itemIdOne)).ToString(),
                central = await _db.PriceListEntries.AsNoTracking()
                    .Where(p => p.ItemIdOne == itemIdOne)
                    .OrderByDescending(p => p.EffectiveFromUtc)
                    .Select(p => new { p.PricePence, p.ExPricePence, p.EffectiveFromUtc, p.CreatedBy })
                    .Take(20).ToListAsync(),
                overrides = await _db.PriceOverrides.AsNoTracking()
                    .Where(o => o.ItemIdOne == itemIdOne && o.RevokedAtUtc == null)
                    .Select(o => new { o.StoreId, o.PricePence, o.ExPricePence, o.EffectiveFromUtc })
                    .ToListAsync(),
            });
        }

        [HttpPut("api/v1/prices/{itemIdOne}/policy")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SetPolicy([FromRoute] string itemIdOne, [FromBody] PolicyBody body)
        {
            if (!Enum.TryParse<PricePolicy>(body?.Policy, true, out var policy))
                return BadRequest(new { detail = "policy must be Central | CentralWithOverride | Local." });
            if (await _db.Items.AsNoTracking().AllAsync(i => i.IdOne != itemIdOne))
                return BadRequest(new { detail = "Unknown itemIdOne." });

            _db.CurrentUser = Actor.ToString();
            var row = await _db.ItemPricePolicies.FirstOrDefaultAsync(p => p.ItemIdOne == itemIdOne);
            var old = row?.Policy ?? PricePolicy.Central;
            if (row == null)
                _db.ItemPricePolicies.Add(new ItemPricePolicy
                {
                    Id = Uuid7.New(), TenantId = _tenant.TenantId, ItemIdOne = itemIdOne, Policy = policy,
                });
            else
                row.Policy = policy;
            _db.Audit(_tenant.TenantId, Actor, "price.policy", "ItemPricePolicy", itemIdOne,
                new { from = old.ToString(), to = policy.ToString() });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>HQ (re)price — appends an effective-dated entry (default: now).</summary>
        [HttpPost("api/v1/prices/{itemIdOne}/central")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SetCentral([FromRoute] string itemIdOne, [FromBody] CentralPriceBody body)
        {
            if (body == null || body.PricePence < 0 || body.ExPricePence < 0 || body.ExPricePence > body.PricePence)
                return BadRequest(new { detail = "pricePence/exPricePence must be non-negative and ex <= inc." });
            if (await _db.Items.AsNoTracking().AllAsync(i => i.IdOne != itemIdOne))
                return BadRequest(new { detail = "Unknown itemIdOne." });

            _db.CurrentUser = Actor.ToString();
            var old = await new PricingService(_db).ResolveAsync(itemIdOne, 0, DateTime.UtcNow);
            var entry = new PriceListEntry
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, ItemIdOne = itemIdOne,
                PricePence = body.PricePence, ExPricePence = body.ExPricePence,
                EffectiveFromUtc = body.EffectiveFromUtc ?? DateTime.UtcNow,
                CreatedBy = Actor, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.PriceListEntries.Add(entry);
            _db.Audit(_tenant.TenantId, Actor, "price.central", nameof(PriceListEntry), itemIdOne,
                new { oldPence = old?.PricePence, newPence = body.PricePence, effectiveFromUtc = entry.EffectiveFromUtc });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/prices/{itemIdOne}", new { id = entry.Id, entry.EffectiveFromUtc });
        }

        /// <summary>Store price. 409 under CENTRAL (stores are read-only there).</summary>
        [HttpPost("api/v1/prices/{itemIdOne}/override")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> SetOverride([FromRoute] string itemIdOne, [FromBody] OverridePriceBody body)
        {
            if (body == null || body.PricePence < 0 || body.ExPricePence < 0 || body.ExPricePence > body.PricePence)
                return BadRequest(new { detail = "pricePence/exPricePence must be non-negative and ex <= inc." });
            if (await _db.Items.AsNoTracking().AllAsync(i => i.IdOne != itemIdOne))
                return BadRequest(new { detail = "Unknown itemIdOne." });
            if (await _db.Stores.AsNoTracking().AllAsync(s => s.Id != body.StoreId))
                return BadRequest(new { detail = "Unknown storeId." });

            var service = new PricingService(_db);
            var policy = await service.PolicyForAsync(itemIdOne);
            if (policy == PricePolicy.Central)
                return Conflict(new { detail = "Policy is CENTRAL — stores are read-only for this item." });

            _db.CurrentUser = Actor.ToString();
            var old = await service.ResolveAsync(itemIdOne, body.StoreId, DateTime.UtcNow);
            var entry = new PriceOverride
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, StoreId = body.StoreId, ItemIdOne = itemIdOne,
                PricePence = body.PricePence, ExPricePence = body.ExPricePence,
                EffectiveFromUtc = body.EffectiveFromUtc ?? DateTime.UtcNow,
                CreatedBy = Actor, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.PriceOverrides.Add(entry);
            _db.Audit(_tenant.TenantId, Actor, "price.override", nameof(PriceOverride), itemIdOne,
                new { body.StoreId, oldPence = old?.PricePence, newPence = body.PricePence, effectiveFromUtc = entry.EffectiveFromUtc });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/prices/{itemIdOne}", new { id = entry.Id });
        }

        /// <summary>HQ force-reset: revoke a store's (or every store's) overrides so the
        /// central price applies again. 409 under LOCAL — HQ sees but doesn't set.</summary>
        [HttpPost("api/v1/prices/{itemIdOne}/force-reset")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ForceReset([FromRoute] string itemIdOne, [FromBody] ForceResetBody body)
        {
            var policy = await new PricingService(_db).PolicyForAsync(itemIdOne);
            if (policy == PricePolicy.Local)
                return Conflict(new { detail = "Policy is LOCAL — the store owns this price; HQ cannot reset it." });

            _db.CurrentUser = Actor.ToString();
            var live = await _db.PriceOverrides
                .Where(o => o.ItemIdOne == itemIdOne && o.RevokedAtUtc == null)
                .Where(o => body == null || body.StoreId == null || o.StoreId == body.StoreId)
                .ToListAsync();
            foreach (var o in live)
            {
                o.RevokedAtUtc = DateTime.UtcNow;
                o.RevokedBy = Actor;
            }
            _db.Audit(_tenant.TenantId, Actor, "price.force-reset", nameof(PriceOverride), itemIdOne,
                new { storeId = body?.StoreId, revoked = live.Count });
            await _db.SaveChangesAsync();
            return Ok(new { revoked = live.Count });
        }

        /// <summary>Variance view: items whose store price differs from the central/HQ price.</summary>
        [HttpGet("api/v1/prices/variance")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Variance([FromQuery] int? storeId)
        {
            var service = new PricingService(_db);
            var now = DateTime.UtcNow;
            var live = await _db.PriceOverrides.AsNoTracking()
                .Where(o => o.RevokedAtUtc == null && o.EffectiveFromUtc <= now)
                .Where(o => storeId == null || o.StoreId == storeId)
                .ToListAsync();

            var rows = new List<object>();
            foreach (var g in live.GroupBy(o => (o.StoreId, o.ItemIdOne)))
            {
                var current = g.OrderByDescending(o => o.EffectiveFromUtc).First();
                var policy = await service.PolicyForAsync(g.Key.ItemIdOne);
                if (policy == PricePolicy.Central) continue; // ignored by resolution anyway
                var central = await _db.PriceListEntries.AsNoTracking()
                    .Where(p => p.ItemIdOne == g.Key.ItemIdOne && p.EffectiveFromUtc <= now)
                    .OrderByDescending(p => p.EffectiveFromUtc)
                    .Select(p => (long?)p.PricePence).FirstOrDefaultAsync();
                var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.IdOne == g.Key.ItemIdOne);
                var hq = central ?? (item == null ? 0 : (long)Math.Round(item.Price * 100));
                if (current.PricePence == hq) continue;
                rows.Add(new
                {
                    storeId = g.Key.StoreId,
                    itemIdOne = g.Key.ItemIdOne,
                    name = item?.Name,
                    policy = policy.ToString(),
                    hqPence = hq,
                    storePence = current.PricePence,
                    deltaPence = current.PricePence - hq,
                });
            }
            return Ok(rows);
        }
    }
}
