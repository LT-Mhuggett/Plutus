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
    public sealed record CompanyAdminBody(string Name, string NameAbbr, string VatIN);

    /// <summary>WP-FY — the company year and the VAT periods. ⚠ Its own body, deliberately: folding
    /// these into `CompanyAdminBody` would let the name/abbr form blank them on every save.</summary>
    public sealed record VatPeriodsBody(
        string Basis, int? StaggerEndMonth, int YearStartMonth, int YearStartDay);

    /// <summary>WP3.2 company admin (Company = legacy Business during evolve-in-place).
    /// Tenant isolation comes from the context's global query filters; every mutation writes
    /// an AuditLogs row in the same SaveChanges.</summary>
    [ApiController]
    [Route("api/v1/companies")]
    [Authorize(Policy = "perm:portal.company.manage")]
    public sealed class CompaniesController : ControllerBase
    {
        private readonly MySqlDbContext Db;
        private readonly ITenantContext _tenant;

        public CompaniesController(MySqlDbContext db, ITenantContext tenant)
        {
            Db = db;
            _tenant = tenant;
        }
        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List() =>
            Ok(await Db.Business.AsNoTracking()
                .Select(b => new { id = b.Id, name = b.Name, nameAbbr = b.NameAbbr, vatIN = b.VatIN })
                .ToListAsync());

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CompanyAdminBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.Name)) return BadRequest(new { detail = "name is required." });
            Db.CurrentUser = Actor.ToString();
            var business = new Business
            {
                Id = Uuid7.New(),
                Name = body.Name.Trim(),
                NameAbbr = string.IsNullOrWhiteSpace(body.NameAbbr) ? Abbr(body.Name) : body.NameAbbr.Trim(),
                VatIN = body.VatIN ?? "",
            };
            Db.Business.Add(business);
            Db.Audit(_tenant.TenantId, Actor, "company.create", nameof(Business), business.Id.ToString(), body);
            await Db.SaveChangesAsync();
            return Created($"/api/v1/companies/{business.Id}", new { id = business.Id });
        }

        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] CompanyAdminBody body)
        {
            var business = await Db.Business.FirstOrDefaultAsync(b => b.Id == id);
            if (business == null) return NotFound(); // includes other tenants' rows (filtered)
            Db.CurrentUser = Actor.ToString();
            if (!string.IsNullOrWhiteSpace(body?.Name)) business.Name = body.Name.Trim();
            if (!string.IsNullOrWhiteSpace(body?.NameAbbr)) business.NameAbbr = body.NameAbbr.Trim();
            if (body?.VatIN != null) business.VatIN = body.VatIN;
            Db.Audit(_tenant.TenantId, Actor, "company.update", nameof(Business), id.ToString(), body);
            await Db.SaveChangesAsync();
            return NoContent();
        }


        /// <summary>
        /// WP-FY — GET the resolved VAT period settings for this business, and whether anybody set
        /// them.
        ///
        /// ⚠⚠ IT ANSWERS THE **RESOLVED** SETTINGS, not the raw columns. Every consumer needs the
        /// same defaulting, and three of them deriving it from nulls is three chances to default
        /// differently — the shape of the bug this whole package exists to close.
        ///
        /// ⚠ `configured: false` IS THE INTERESTING FIELD. It is what lets a report say *"the
        /// default, not yet set in the portal"* rather than presenting a guess as a choice.
        ///
        /// ⚠ READABLE BY ANYONE WHO CAN SEE FINANCIALS, not just by whoever may change it: the VAT
        /// return screen needs it to build its period picker, and gating the read on the write
        /// permission would leave an accountant looking at calendar quarters again.
        /// </summary>
        [HttpGet("vat-periods")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage + ","
                          + PermissionCatalogue.PortalFinancialsView + ","
                          + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetVatPeriods([FromQuery] int? year = null)
        {
            var b = await Db.Business.AsNoTracking()
                .Select(x => new
                {
                    x.Id, x.VatBasis, x.VatStaggerEndMonth,
                    x.FinancialYearStartMonth, x.FinancialYearStartDay,
                    x.VatSettingsChangedAtUtc,
                })
                .FirstOrDefaultAsync();

            // ⚠ NO BUSINESS ROW IS NOT AN ERROR — a tenant mid-provisioning has none, and the
            // caller still needs an answer it can build a period picker from.
            var s = FinancialCalendar.Resolve(
                b?.VatBasis, b?.VatStaggerEndMonth, b?.FinancialYearStartMonth, b?.FinancialYearStartDay);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentYear = FinancialCalendar.FinancialYearOf(today, s);

            // ⚠⚠ THE SERVER ANSWERS THE PERIODS, AND THE CLIENT NEVER COMPUTES A BOUNDARY.
            //
            // The obvious alternative — hand the portal a stagger and let it work out the dates —
            // is a **C2 twin over money**: two implementations of "when does this VAT quarter start"
            // in two languages, which is precisely the class of bug `apiTime.ts` was written to end
            // the same week. The picker renders what it is given.
            //
            // ⚠ `year` NAMES THE YEAR THE FINANCIAL YEAR STARTS IN — see `FinancialCalendar`.
            var periods = FinancialCalendar.PeriodsOfYear(s, year ?? currentYear)
                .Select(p => new
                {
                    key = p.Key,
                    from = p.Start.ToString("yyyy-MM-dd"),
                    to = p.End.ToString("yyyy-MM-dd"),

                    // ⚠ THE LABEL IS BUILT HERE so both ends read the same words, and it names the
                    // MONTHS rather than a quarter number — "Q1" meant calendar Jan–Mar on the old
                    // picker and means something else for two of the three staggers.
                    label = p.Start.ToString("MMM yyyy") + " – " + p.End.ToString("MMM yyyy"),

                    // ⚠ Which period TODAY is in, so a picker can land on it rather than on the
                    // first of the year — the one somebody opening this screen almost always wants.
                    current = today >= p.Start && today <= p.End,
                })
                .ToList();

            return Ok(new
            {
                companyId = b?.Id,
                basis = s.Basis,
                staggerEndMonth = s.StaggerEndMonth,
                yearStartMonth = s.YearStartMonth,
                yearStartDay = s.YearStartDay,
                configured = s.Configured,
                describe = s.Describe(),
                changedAtUtc = b?.VatSettingsChangedAtUtc,
                financialYear = year ?? currentYear,
                currentFinancialYear = currentYear,
                periods,
            });
        }

        /// <summary>
        /// WP-FY — set the company year and the VAT periods.
        ///
        /// ⚠⚠ ITS OWN ENDPOINT, NOT FIELDS ON `CompanyAdminBody`, AND THAT IS A CORRECTNESS
        /// DECISION. `Update` is a partial-replace over a body the portal's name/abbr/VAT-number
        /// form already posts; adding these there would mean **every save of that form blanked the
        /// VAT settings** unless the form remembered to echo them back. A separate endpoint cannot
        /// have that bug.
        ///
        /// ⚠ It also earns its own audit action. `company.vat-periods.update` is what somebody
        /// searches for when two VAT reports of the same period disagree, and burying it inside
        /// `company.update` would hide it among name changes.
        ///
        /// ⚠⚠ **CHANGING A STAGGER RE-BUCKETS EVERY HISTORICAL RETURN.** The same takings, filed
        /// against different periods, produce different numbers — so this records who and when on
        /// the row itself as well as in the audit log, and the portal warns before saving.
        /// </summary>
        [HttpPut("vat-periods")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetVatPeriods([FromBody] VatPeriodsBody body)
        {
            if (body is null) return BadRequest(new { detail = "A body is required." });

            var basis = body.Basis?.Trim().ToLowerInvariant();
            if (basis is not ("quarter" or "month"))
                return BadRequest(new { detail = "basis must be quarter or month." });

            // ⚠⚠ VALIDATED HERE AS WELL AS IN `FinancialCalendar.Resolve`, and the duplication is
            // deliberate. `Resolve` treats junk as UNSET so a report never crashes on a bad row;
            // this REFUSES it, so a bad row cannot be written in the first place. Silently storing
            // 13 and reading it back as 3 would be the worst of both.
            if (basis == "quarter" && body.StaggerEndMonth is not (>= 1 and <= 12))
                return BadRequest(new { detail = "staggerEndMonth must be 1-12 (the month a VAT quarter ends)." });

            if (body.YearStartMonth is not (>= 1 and <= 12))
                return BadRequest(new { detail = "yearStartMonth must be 1-12." });

            if (body.YearStartDay is not (>= 1 and <= 31))
                return BadRequest(new { detail = "yearStartDay must be 1-31." });

            var business = await Db.Business.FirstOrDefaultAsync();
            if (business == null) return NotFound(new { detail = "This tenant has no company record yet." });

            Db.CurrentUser = Actor.ToString();

            business.VatBasis = basis;
            // ⚠ A MONTHLY FILER'S STAGGER IS CLEARED, not left behind. A stale stagger under a
            // monthly basis is a value that means nothing and reads as if it means something — and
            // it would come back the moment somebody switched to quarterly.
            business.VatStaggerEndMonth = basis == "quarter" ? body.StaggerEndMonth : null;
            business.FinancialYearStartMonth = body.YearStartMonth;
            business.FinancialYearStartDay = body.YearStartDay;
            business.VatSettingsChangedAtUtc = DateTime.UtcNow;
            business.VatSettingsChangedBy = Actor;

            Db.Audit(_tenant.TenantId, Actor, "company.vat-periods.update",
                nameof(Business), business.Id.ToString(), body);

            await Db.SaveChangesAsync();
            return NoContent();
        }
        private static string Abbr(string name)
        {
            var t = name.Trim();
            return t.Length <= 5 ? t.ToUpperInvariant() : t[..5].ToUpperInvariant();
        }
    }
}
