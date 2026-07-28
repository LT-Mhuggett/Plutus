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

namespace Plutus.Tenancy.Controllers
{
    public sealed record PlanBody(string Name, long PricePenceMonthly, string[] Entitlements, bool Active);
    public sealed record AssignPlanBody(Guid? PlanId);

    /// <summary>
    /// OP2 subscription plans — the operator's named price list (platform-admin). A plan bundles a
    /// name, monthly price and an entitlement set; assigning it to a tenant copies the name into
    /// <c>Tenant.Plan</c> and the entitlements into <c>Tenant.Entitlements</c> so every existing
    /// entitlement read keeps working. A tenant's negotiated <see cref="TenantContract"/> price (if
    /// any) still overrides the plan's list price for margin/billing.
    /// </summary>
    [ApiController]
    public sealed class PlatformPlansController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        public PlatformPlansController(MySqlDbContext db) => _db = db;

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;
        private static string ToJson(string[] e) => System.Text.Json.JsonSerializer.Serialize(e ?? Array.Empty<string>());

        [HttpGet("api/v1/platform/plans")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List()
        {
            var plans = await _db.SubscriptionPlans.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            // tenants-per-plan in one grouped pass (unscoped: platform-admin sees all tenants)
            var counts = (await _db.Tenants.AsNoTracking().Where(t => t.PlanId != null)
                    .GroupBy(t => t.PlanId).Select(g => new { g.Key, C = g.Count() }).ToListAsync())
                .ToDictionary(x => x.Key.Value, x => x.C);
            return Ok(plans.Select(p => new
            {
                p.Id, p.Name, p.PricePenceMonthly,
                entitlements = EntitlementService.Parse(p.EntitlementsJson), p.Active,
                tenantCount = counts.TryGetValue(p.Id, out var c) ? c : 0, p.UpdatedAtUtc,
            }));
        }

        [HttpPost("api/v1/platform/plans")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] PlanBody body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Name)) return BadRequest(new { detail = "Name is required." });
            var name = body.Name.Trim();
            if (await _db.SubscriptionPlans.AnyAsync(p => p.Name == name)) return Conflict(new { detail = "A plan with that name already exists." });
            _db.CurrentUser = Actor.ToString();
            var plan = new SubscriptionPlan
            {
                Id = Uuid7.New(), Name = name, PricePenceMonthly = body.PricePenceMonthly,
                EntitlementsJson = ToJson(body.Entitlements), Active = body.Active,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, UpdatedBy = Actor.ToString(),
            };
            _db.SubscriptionPlans.Add(plan);
            _db.Audit(Guid.Empty, Actor, "plan.create", nameof(SubscriptionPlan), plan.Id.ToString(), new { plan.Name, plan.PricePenceMonthly });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/platform/plans/{plan.Id}", new { plan.Id });
        }

        [HttpPut("api/v1/platform/plans/{id}")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] PlanBody body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Name)) return BadRequest(new { detail = "Name is required." });
            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null) return NotFound();
            var name = body.Name.Trim();
            if (await _db.SubscriptionPlans.AnyAsync(p => p.Name == name && p.Id != id)) return Conflict(new { detail = "A plan with that name already exists." });
            _db.CurrentUser = Actor.ToString();
            plan.Name = name;
            plan.PricePenceMonthly = body.PricePenceMonthly;
            plan.EntitlementsJson = ToJson(body.Entitlements);
            plan.Active = body.Active;
            plan.UpdatedAtUtc = DateTime.UtcNow;
            plan.UpdatedBy = Actor.ToString();
            _db.Audit(Guid.Empty, Actor, "plan.update", nameof(SubscriptionPlan), id.ToString(), new { plan.Name, plan.PricePenceMonthly });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("api/v1/platform/plans/{id}")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null) return NoContent();
            if (await _db.Tenants.AnyAsync(t => t.PlanId == id))
                return Conflict(new { detail = "Plan is assigned to one or more tenants; reassign them first." });
            _db.CurrentUser = Actor.ToString();
            _db.SubscriptionPlans.Remove(plan);
            _db.Audit(Guid.Empty, Actor, "plan.delete", nameof(SubscriptionPlan), id.ToString(), new { plan.Name });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Assign (or clear, planId=null) a tenant's plan. On assign, copies the plan's
        /// name + entitlement bundle onto the tenant so existing entitlement reads reflect it.</summary>
        [HttpPut("api/v1/tenants/{id}/plan")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AssignPlan([FromRoute] Guid id, [FromBody] AssignPlanBody body)
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound();
            _db.CurrentUser = Actor.ToString();
            if (body?.PlanId is Guid planId)
            {
                var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId);
                if (plan == null) return NotFound(new { detail = "Plan not found." });
                tenant.PlanId = plan.Id;
                tenant.Plan = plan.Name;                                  // keep the legacy display field in sync
                tenant.Entitlements = plan.EntitlementsJson ?? "[]";      // base entitlements from the plan (overrides still win)
            }
            else
            {
                tenant.PlanId = null; // unassign — leave the current Plan/Entitlements strings as-is
            }
            _db.Audit(id, Actor, "tenant.plan", nameof(Tenant), id.ToString(), new { body?.PlanId });
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
