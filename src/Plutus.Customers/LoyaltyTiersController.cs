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

namespace Plutus.Customers
{
    public sealed record TierBody(string Name, decimal AutoDiscountRate, int? DurationMonths, int? SortOrder, bool? Active);

    /// <summary>
    /// FE1 loyalty tier catalogue: the tenant's pre-defined membership levels. The READ is open to
    /// any authenticated principal (the till's assign-tier dropdown needs it, same openness as
    /// customer lookup); writes are gated on customers.manage and audited. There is deliberately
    /// NO delete — a tier is deactivated, because memberships reference it and history must stay
    /// readable.
    /// </summary>
    [ApiController]
    public sealed class LoyaltyTiersController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public LoyaltyTiersController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>The catalogue. Active only by default; the portal's manager passes
        /// includeInactive=true to show (and re-activate) retired tiers. Each row carries its
        /// live member count so the manager can warn before deactivating a tier in use.</summary>
        [HttpGet("api/v1/loyalty/tiers")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
        {
            var tiers = await _db.LoyaltyTiers.AsNoTracking()
                .Where(t => includeInactive || t.Active)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
                .ToListAsync();

            var counts = (await _db.Memberships.AsNoTracking()
                    .Where(m => m.Active && m.TierId != null)
                    .GroupBy(m => m.TierId).Select(g => new { TierId = g.Key, Count = g.Count() })
                    .ToListAsync())
                .ToDictionary(x => x.TierId.Value, x => x.Count);

            return Ok(tiers.Select(t => new
            {
                id = t.Id, name = t.Name, autoDiscountRate = t.AutoDiscountRate,
                durationMonths = t.DurationMonths, active = t.Active, sortOrder = t.SortOrder,
                memberCount = counts.TryGetValue(t.Id, out var c) ? c : 0,
            }));
        }

        [HttpPost("api/v1/loyalty/tiers")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.CustomersManage)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] TierBody body)
        {
            var problem = Validate(body);
            if (problem != null) return BadRequest(new { detail = problem });

            var name = body.Name.Trim();
            if (await _db.LoyaltyTiers.AnyAsync(t => t.Name.ToLower() == name.ToLower()))
                return Conflict(new { detail = $"A tier called '{name}' already exists." });

            _db.CurrentUser = Actor.ToString();
            var tier = new LoyaltyTier
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, Name = name,
                AutoDiscountRate = body.AutoDiscountRate,
                DurationMonths = body.DurationMonths ?? 12,
                SortOrder = body.SortOrder ?? 0,
                Active = body.Active ?? true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _db.LoyaltyTiers.Add(tier);
            _db.Audit(_tenant.TenantId, Actor, "loyalty.tier.create", nameof(LoyaltyTier), tier.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/loyalty/tiers/{tier.Id}", new { id = tier.Id });
        }

        /// <summary>Rename / re-rate / re-order / (de)activate. Re-rating applies to every member
        /// of the tier immediately — that is the point of the catalogue (recorded sales are
        /// immutable, so history is unaffected).</summary>
        [HttpPut("api/v1/loyalty/tiers/{id}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.CustomersManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] TierBody body)
        {
            var problem = Validate(body);
            if (problem != null) return BadRequest(new { detail = problem });

            var tier = await _db.LoyaltyTiers.FirstOrDefaultAsync(t => t.Id == id);
            if (tier == null) return NotFound();

            var name = body.Name.Trim();
            if (await _db.LoyaltyTiers.AnyAsync(t => t.Id != id && t.Name.ToLower() == name.ToLower()))
                return Conflict(new { detail = $"A tier called '{name}' already exists." });

            _db.CurrentUser = Actor.ToString();
            tier.Name = name;
            tier.AutoDiscountRate = body.AutoDiscountRate;
            if (body.DurationMonths.HasValue) tier.DurationMonths = body.DurationMonths.Value;
            if (body.SortOrder.HasValue) tier.SortOrder = body.SortOrder.Value;
            if (body.Active.HasValue) tier.Active = body.Active.Value;
            _db.Audit(_tenant.TenantId, Actor, "loyalty.tier.update", nameof(LoyaltyTier), tier.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Ok(new
            {
                id = tier.Id, name = tier.Name, autoDiscountRate = tier.AutoDiscountRate,
                durationMonths = tier.DurationMonths, active = tier.Active, sortOrder = tier.SortOrder,
            });
        }

        private static string Validate(TierBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.Name)) return "name is required.";
            if (body.Name.Trim().Length > 50) return "name must be 50 characters or fewer.";
            if (body.AutoDiscountRate < 0 || body.AutoDiscountRate > 1)
                return "autoDiscountRate must be between 0 and 1.";
            if (body.DurationMonths is <= 0 or > 600) return "durationMonths must be between 1 and 600.";
            return null;
        }
    }
}
