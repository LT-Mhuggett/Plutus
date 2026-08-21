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
    /// <summary>
    /// Scheduled discounts — set in the portal, applied by every till. "Wednesday Warhammer".
    ///
    /// ⚠⚠ MATT, 2026-08-20: *"I have Wednesday Warhammer discount that should flag items in the
    /// warhammer catergory on a Wednesday as 'Should have 10%'."*
    ///
    /// ⚠⚠ IT EXTENDS THE EXISTING `Discounts` TABLE RATHER THAN ADDING A RIVAL ENTITY, and that is the
    /// load-bearing design decision. The legacy row has a REAL int id that
    /// <c>LegacySaleBridgeConsumer</c> projects into <c>Transaction_Discount</c>; a new entity would
    /// have needed either a bridge change or the members'-discount exclusion dance for every rule
    /// discount ever taken. Both tills already fetch, cache and render this catalogue, so the manual
    /// picker kept working throughout. And `Discount_Category` / `Discount_Item` already said "this
    /// discount targets that category/item" — building a second join would have been drift by
    /// construction.
    ///
    /// ⚠ `AutoApply` stops being a dead column here: TRUE is what makes a rule apply itself at a till,
    /// FALSE leaves it a manual-picker entry. It has been on the entity, unread, since NatApp.
    ///
    /// ⚠ THE SCHEDULE TRAVELS RAW. See <c>DiscountRuleDto</c> and
    /// <c>SharedKernel.ScheduledDiscount.IsLiveAt</c> — the till decides "is it Wednesday" against its
    /// own clock, so a till that has been offline since Monday still discounts correctly on Wednesday.
    /// </summary>
    [Route("api/v1/discounts/rules")]
    [ApiController]
    public sealed class DiscountRulesController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public DiscountRulesController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor =>
            Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>
        /// The rules a till should be applying — ACTIVE ones only.
        ///
        /// ⚠ Any authenticated caller, exactly like `carrier-bags` and `payments/gateway/active`: a
        /// till has to draw its own conclusions from this list, and gating it behind a portal
        /// permission would stop a device token ever reading it. It carries no customer data and no
        /// money the shop has not already published to its own shop floor.
        ///
        /// ⚠ A PAUSED RULE IS SIMPLY ABSENT, rather than present-and-flagged. A till that received
        /// inactive rules would have to filter them, which is a second place for the answer to be
        /// wrong; the server already knows.
        /// </summary>
        [HttpGet("")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List()
        {
            var rules = await RulesAsync(includeInactive: false);
            return Ok(new { asOfUtc = DateTime.UtcNow, rules });
        }

        /// <summary>
        /// Every rule including the paused ones — what the portal's Discounts screen lists.
        ///
        /// ⚠ A separate route rather than a query flag on the till feed, so the two audiences cannot be
        /// confused: this one needs a portal permission, and a till must never be able to reach it by
        /// adding a parameter.
        /// </summary>
        [HttpGet("manage")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListForManagement()
        {
            var rules = await RulesAsync(includeInactive: true);
            return Ok(new { asOfUtc = DateTime.UtcNow, rules });
        }

        /// <summary>
        /// The categories a rule can target, so the portal never asks anybody to type a Guid.
        ///
        /// ⚠ Here rather than reusing `/api/Category/Index` because that endpoint pages and this list
        /// has to be complete to be a picker — a category on page two that cannot be selected is a
        /// promotion the shop cannot create.
        /// </summary>
        [HttpGet("categories")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Categories()
        {
            var cats = await _db.Category.AsNoTracking()
                .Select(c => new { id = c.IdOne, name = c.Name })
                .ToListAsync();

            // ⚠ Ordered through the SHARED table rule, not by SQL: `TableSort.Compare` is numeric-aware
            // and case-insensitive, so "Warhammer 10" sorts after "Warhammer 2" here exactly as it does
            // in every other Plutus table. A raw `ORDER BY name` would disagree with the grid it feeds.
            return Ok(cats.OrderBy(c => c.name, Comparer<string>.Create(TableSort.Compare)));
        }

        /// <summary>
        /// Create a rule, or replace one wholesale.
        ///
        /// ⚠⚠ THE WRITER IS STRICT SO THE READERS CAN BE SIMPLE — the opening-hours lesson, verbatim:
        /// tills are tolerant of what is already stored, the portal refuses to create anything a till
        /// would have to guess at. Every refusal below is a sentence a person can act on, because the
        /// alternative (saving something unreadable) looks identical to "not set" on every till.
        ///
        /// ⚠ TARGETS ARE REPLACED, NOT MERGED. A rule edited from "Warhammer" to "Paint" must stop
        /// applying to Warhammer; merging would leave a promotion running that the screen says has
        /// ended, and nobody would find it.
        /// </summary>
        [HttpPut("")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Upsert([FromBody] DiscountRuleBody body)
        {
            if (body is null) return BadRequest(new { detail = "No rule was sent." });

            var name = (body.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                return BadRequest(new { detail = "Give the discount a name — it is what the customer sees on the receipt." });
            if (name.Length > 100)
                return BadRequest(new { detail = "That name is too long for a receipt line — keep it under 100 characters." });

            if (body.Type != DiscountKinds.FixedAmount && body.Type != DiscountKinds.Percentage)
                return BadRequest(new { detail = "A discount is either a percentage or a fixed amount." });

            // ⚠⚠ THE ONE THAT MATTERS. A percentage is a FRACTION, so 0.10 is 10% and anything above 1
            // is more than the item is worth. The legacy MAUI till multiplied the price by a typed "10"
            // and charged ten times — `LineDiscounts.Percentage` throws on it now, and this refuses it
            // ever reaching a till at all.
            if (body.Type == DiscountKinds.Percentage && (body.PercentFraction <= 0m || body.PercentFraction > 1m))
                return BadRequest(new { detail = "A percentage is a fraction: enter 0.1 for 10%. Anything above 1 would be more than the item costs." });

            if (body.Type == DiscountKinds.FixedAmount && body.FixedAmountPence <= 0)
                return BadRequest(new { detail = "A discount of nothing is not a discount — enter an amount above zero." });

            // ⚠ A TYPO CAP on the fixed side, the same reasoning as the carrier bag's £5 guard: no shop
            // means to take £500 off a line, and a slipped digit would do it on every basket.
            if (body.Type == DiscountKinds.FixedAmount && body.FixedAmountPence > 50_000)
                return BadRequest(new { detail = "That is more than £500 off a single unit — check the amount." });

            var categoryIds = (body.CategoryIds ?? Array.Empty<Guid>()).Distinct().ToList();
            var itemIdOnes = (body.ItemIdOnes ?? Array.Empty<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // ⚠⚠ THE MONEY REFUSAL. A rule that targets nothing must never be saveable, because the
            // shape it would take at a till is ambiguous and the dangerous reading is "everything".
            // The tills already refuse to act on one (`ScheduledDiscount.IsWellFormed`); refusing it
            // here as well is what stops somebody creating a promotion that silently does nothing and
            // wondering why. "Everything" has to be asked for explicitly.
            if (!body.AllApplicable && categoryIds.Count == 0 && itemIdOnes.Count == 0)
                return BadRequest(new
                {
                    detail = "Choose what this applies to — a category, some items, or tick "
                        + "\"everything in the basket\". A discount that targets nothing would never apply.",
                });

            // ⚠ A midnight-wrapping window matches NOTHING on every till (mirrored from the permission
            // rule, deliberately unhandled rather than half-handled). Saving one would create a rule
            // that looks live on this screen and never fires — so it is refused where somebody can
            // still fix it, rather than left to puzzle over at a counter.
            if (body.WindowStartLocal is TimeOnly ws && body.WindowEndLocal is TimeOnly we && we < ws)
                return BadRequest(new
                {
                    detail = "The end time is before the start time. A window that runs over midnight "
                        + "is not supported — use two rules, one each side of midnight.",
                });

            if (body.ValidFromUtc is DateTime vf && body.ValidToUtc is DateTime vt && vt < vf)
                return BadRequest(new { detail = "The end date is before the start date." });

            if (body.DaysOfWeekMask is byte mask && mask > 0b0111_1111)
                return BadRequest(new { detail = "That day selection is not a valid set of days." });

            // ⚠ A mask of ZERO means "no days at all", which is a rule that can never fire. Null is how
            // "every day" is expressed — the two are different states and collapsing them would make
            // "always" impossible to say.
            if (body.DaysOfWeekMask is 0)
                return BadRequest(new { detail = "Pick at least one day, or leave the days blank for every day." });

            Discount rule;
            _db.CurrentUser = Actor.ToString();

            if (body.Id is int id && id > 0)
            {
                rule = await _db.Discounts
                    .Include(d => d.DisCategoryList)
                    .Include(d => d.DisItemList)
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (rule is null) return NotFound(new { detail = "That discount no longer exists." });

                // ⚠ Replaced, not merged — see the summary.
                if (rule.DisCategoryList is { Count: > 0 }) _db.RemoveRange(rule.DisCategoryList);
                if (rule.DisItemList is { Count: > 0 }) _db.RemoveRange(rule.DisItemList);
            }
            else
            {
                // ⚠⚠ ONE BUSINESS, OR A REFUSAL. `Discount.BusinessId` is required and the legacy row is
                // per-business, so a tenant with two businesses raises a real question — which shop is
                // this promotion for? — that this screen has no way to ask. Guessing would attach it to
                // one of them silently. Every tenant today has exactly one business, so this is a
                // refusal nobody will meet, and it is the honest alternative to a wrong default.
                var businesses = await _db.Business.AsNoTracking().Select(b => b.Id).ToListAsync();
                if (businesses.Count == 0)
                    return Conflict(new { detail = "This tenant has no business yet, so a discount has nothing to belong to." });
                if (businesses.Count > 1)
                    return Conflict(new
                    {
                        detail = "This tenant has more than one business, and a discount belongs to one "
                            + "of them. Choosing which needs a picker this screen does not have yet.",
                    });

                rule = new Discount { BusinessId = businesses[0] };
                _db.Discounts.Add(rule);
            }

            rule.Name = name;
            rule.Type = body.Type;
            // ⚠⚠ THE OTHER SIDE OF THE SAME SEAM. The API carries a fraction and integer pence, split,
            // because one decimal meaning two units is a unit error waiting to happen — see the read
            // path below. The legacy COLUMN is a single decimal that means a fraction for a percentage
            // and POUNDS for a fixed amount, so the collapse happens here, once, at the boundary.
            rule.Amount = body.Type == DiscountKinds.Percentage
                ? body.PercentFraction
                : body.FixedAmountPence / 100m;
            rule.AutoApply = body.AutoApply;
            rule.AllApplicable = body.AllApplicable;
            rule.Active = body.Active;
            rule.DaysOfWeekMask = body.DaysOfWeekMask;
            rule.WindowStartLocal = body.WindowStartLocal;
            rule.WindowEndLocal = body.WindowEndLocal;
            rule.ValidFromUtc = AsUtc(body.ValidFromUtc);
            rule.ValidToUtc = AsUtc(body.ValidToUtc);

            // ⚠ These legacy columns are NOT touched and NOT read (see the plan, decision D7):
            // `CanUseWithOtherDiscounts` is superseded by one-discount-per-line, and
            // `UsesPerTransaction`/`RequiredNumOfItems` are mix-and-match, a different feature. Half of
            // a multi-buy engine beside a working discount is worse than none, because the gaps look
            // like rules.

            await _db.SaveChangesAsync();   // ⚠ needed before the joins: a new rule has no Id until now

            foreach (var catId in categoryIds)
            {
                // ⚠ `Discount_Category` is keyed on the category's COMPOSITE id, and `CatIdTwo` is the
                // business — the same pair `Item.CatId` resolves against. Writing only `CatIdOne` would
                // leave a join row EF cannot navigate.
                _db.DiscountCats.Add(new Discount_Category
                {
                    CatIdOne = catId,
                    CatIdTwo = rule.BusinessId,
                    Discount = rule,
                });
            }

            foreach (var itemIdOne in itemIdOnes)
            {
                _db.DiscountItems.Add(new Discount_Item
                {
                    ItemIdOne = itemIdOne,
                    ItemIdTwo = rule.BusinessId,
                    Discount = rule,
                });
            }

            _db.Audit(_tenant.TenantId, Actor, "catalogue.discount-rule", nameof(Discount), rule.Id.ToString(),
                $"name={name} type={body.Type} fraction={body.PercentFraction} pence={body.FixedAmountPence} auto={body.AutoApply} "
                + $"active={body.Active} days={body.DaysOfWeekMask} cats={categoryIds.Count} items={itemIdOnes.Count}");

            await _db.SaveChangesAsync();

            return Ok(new { id = rule.Id });
        }

        /// <summary>
        /// Stop a rule.
        ///
        /// ⚠⚠ IT IS DEACTIVATED, NEVER DELETED, and here that is not a preference — it is a foreign key.
        /// Every sale that ever took this discount has a <c>Transaction_Discount</c> row pointing at
        /// this id, so a DELETE would either fail or orphan the history of what the shop charged. Same
        /// answer as the Bin for items and the withdrawal for carrier bags, for the same reason.
        /// </summary>
        [HttpDelete("{id:int}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalPricesManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Deactivate(int id)
        {
            var rule = await _db.Discounts.FirstOrDefaultAsync(d => d.Id == id);
            if (rule is null) return NoContent();   // ⚠ idempotent: gone is the state that was asked for

            _db.CurrentUser = Actor.ToString();
            rule.Active = false;
            rule.AutoApply = false;   // ⚠ belt and braces: a paused rule must not auto-apply anywhere

            _db.Audit(_tenant.TenantId, Actor, "catalogue.discount-rule.deactivate", nameof(Discount),
                id.ToString(), $"name={rule.Name}");

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠ MySQL hands back a <c>DateTime</c> with <c>Kind.Unspecified</c>, and
        /// <c>System.Text.Json</c> then serialises it WITHOUT a `Z`. `Date.parse` in a browser reads
        /// that as LOCAL time, so every promotion boundary would shift by the till's offset — an hour
        /// of wrong prices at each end, twice a year. Stamped UTC on the way out; the web till also
        /// defends itself, because two guards are what this class of bug deserves.
        /// </summary>
        private static DateTime? AsUtc(DateTime? v) =>
            v is null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc);

        private async Task<List<object>> RulesAsync(bool includeInactive)
        {
            var query = _db.Discounts.AsNoTracking();
            if (!includeInactive) query = query.Where(d => d.Active);

            var rows = await query
                .Select(d => new
                {
                    d.Id, d.Name, d.Type, d.Amount, d.AutoApply, d.AllApplicable, d.Active,
                    d.DaysOfWeekMask, d.WindowStartLocal, d.WindowEndLocal, d.ValidFromUtc, d.ValidToUtc,
                    CategoryIds = d.DisCategoryList.Select(c => c.CatIdOne).ToList(),
                    ItemIdOnes = d.DisItemList.Select(i => i.ItemIdOne).ToList(),
                })
                .ToListAsync();

            return rows
                .OrderBy(r => r.Id)
                .Select(r => (object)new
                {
                    id = r.Id,
                    name = r.Name,
                    type = r.Type,
                    // ⚠⚠ THE ONE PLACE THE LEGACY DECIMAL BECOMES PLATFORM MONEY. `Discounts.Amount` is
                    // a decimal that means POUNDS for a fixed discount and a FRACTION for a percentage
                    // — one column, two units, which is exactly what the architecture guard
                    // `No_module_declares_decimal_or_double_money_members` refuses to let travel. It is
                    // split here so no client, and no shared rule, ever has to ask which it is.
                    //
                    // ⚠ `Pence.FromDecimal` is used for its stated purpose: "the one place decimals
                    // legitimately still arrive" — reading a legacy column.
                    percentFraction = r.Type == DiscountKinds.Percentage ? r.Amount : 0m,
                    fixedAmountPence = r.Type == DiscountKinds.FixedAmount ? Pence.FromDecimal(r.Amount) : 0L,
                    autoApply = r.AutoApply,
                    allApplicable = r.AllApplicable,
                    active = r.Active,
                    daysOfWeekMask = r.DaysOfWeekMask,
                    windowStartLocal = r.WindowStartLocal,
                    windowEndLocal = r.WindowEndLocal,
                    validFromUtc = AsUtc(r.ValidFromUtc),
                    validToUtc = AsUtc(r.ValidToUtc),
                    categoryIds = r.CategoryIds,
                    itemIdOnes = r.ItemIdOnes,
                })
                .ToList();
        }

        /// <summary>What the portal sends. ⚠ <see cref="Id"/> absent or 0 = create.</summary>
        public sealed class DiscountRuleBody
        {
            public int? Id { get; set; }
            public string Name { get; set; }
            public int Type { get; set; }
            /// <summary>A FRACTION when <see cref="Type"/> is a percentage — 0.10 is 10%. A ratio, not
            /// money.</summary>
            public decimal PercentFraction { get; set; }
            /// <summary>⚠ INTEGER PENCE, per unit, when <see cref="Type"/> is a fixed amount.</summary>
            public long FixedAmountPence { get; set; }
            public bool AutoApply { get; set; }
            public bool AllApplicable { get; set; }
            public bool Active { get; set; } = true;
            public byte? DaysOfWeekMask { get; set; }
            public TimeOnly? WindowStartLocal { get; set; }
            public TimeOnly? WindowEndLocal { get; set; }
            public DateTime? ValidFromUtc { get; set; }
            public DateTime? ValidToUtc { get; set; }
            public Guid[] CategoryIds { get; set; }
            public string[] ItemIdOnes { get; set; }
        }
    }
}
