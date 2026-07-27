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

        private static string Abbr(string name)
        {
            var t = name.Trim();
            return t.Length <= 5 ? t.ToUpperInvariant() : t[..5].ToUpperInvariant();
        }
    }
}
