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
    public sealed record StoreAdminBody(
        Guid? CompanyId, string AdLine1, string AdLine2, string City, string PostCode,
        string Country, string ContactNumber, string OpeningHoursJson);

    /// <summary>WP3.2 store admin, incl. opening hours (held in the server-side StoreDetails
    /// table so the shared legacy Store POCO — mapped by the MAUI Sqlite context — stays
    /// untouched). Every mutation writes an AuditLogs row in the same SaveChanges.</summary>
    [ApiController]
    [Route("api/v1/stores")]
    // Per-method policies (NOT class-level, which would AND with every action): management is
    // portal.company.manage; the receipt-template READ is sales.ingest so a till (operator or
    // device) can fetch the template it prints with.
    public sealed class StoresController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public StoresController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpGet]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] Guid? companyId)
        {
            var stores = await _db.Stores.AsNoTracking()
                .Where(s => companyId == null || s.BusinessId == companyId)
                .Select(s => new
                {
                    id = s.Id, companyId = s.BusinessId, adLine1 = s.AdLine1, adLine2 = s.AdLine2,
                    city = s.City, postCode = s.PostCode, country = s.Country, contactNumber = s.ContactNumber,
                })
                .ToListAsync();
            var details = await _db.StoreDetails.AsNoTracking().ToDictionaryAsync(d => d.StoreId);
            return Ok(stores.Select(s => new
            {
                s.id, s.companyId, s.adLine1, s.adLine2, s.city, s.postCode, s.country, s.contactNumber,
                openingHoursJson = details.TryGetValue(s.id, out var d) ? d.OpeningHoursJson : null,
                receiptTemplateJson = details.TryGetValue(s.id, out var d2) ? d2.ReceiptTemplateJson : null,
            }));
        }

        [HttpPost]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] StoreAdminBody body)
        {
            var companyId = body?.CompanyId
                ?? await _db.Business.AsNoTracking().Select(b => (Guid?)b.Id).FirstOrDefaultAsync();
            if (companyId == null || await _db.Business.AsNoTracking().AllAsync(b => b.Id != companyId))
                return BadRequest(new { detail = "companyId must reference one of the tenant's companies." });

            _db.CurrentUser = Actor.ToString();
            var store = new Store
            {
                BusinessId = companyId.Value,
                AdLine1 = Or(body.AdLine1, "N/A"),
                AdLine2 = body.AdLine2 ?? "",
                City = body.City ?? "",
                PostCode = Or(body.PostCode, "N/A"),
                Country = body.Country ?? "",
                ContactNumber = Or(body.ContactNumber, "N/A"),
            };
            _db.Stores.Add(store);
            await _db.SaveChangesAsync(); // identity id

            if (!string.IsNullOrWhiteSpace(body.OpeningHoursJson))
                _db.StoreDetails.Add(new StoreDetails
                {
                    StoreId = store.Id, TenantId = _tenant.TenantId, OpeningHoursJson = body.OpeningHoursJson,
                });
            _db.Audit(_tenant.TenantId, Actor, "store.create", nameof(Store), store.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/stores/{store.Id}", new { id = store.Id });
        }

        [HttpPut("{id}")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update([FromRoute] int id, [FromBody] StoreAdminBody body)
        {
            var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == id);
            if (store == null) return NotFound();

            _db.CurrentUser = Actor.ToString();
            if (body?.AdLine1 != null) store.AdLine1 = Or(body.AdLine1, "N/A");
            if (body?.AdLine2 != null) store.AdLine2 = body.AdLine2;
            if (body?.City != null) store.City = body.City;
            if (body?.PostCode != null) store.PostCode = Or(body.PostCode, "N/A");
            if (body?.Country != null) store.Country = body.Country;
            if (body?.ContactNumber != null) store.ContactNumber = Or(body.ContactNumber, "N/A");

            if (body?.OpeningHoursJson != null)
            {
                var details = await _db.StoreDetails.FirstOrDefaultAsync(d => d.StoreId == id);
                if (details == null)
                    _db.StoreDetails.Add(new StoreDetails
                    {
                        StoreId = id, TenantId = _tenant.TenantId, OpeningHoursJson = body.OpeningHoursJson,
                    });
                else
                    details.OpeningHoursJson = body.OpeningHoursJson;
            }

            _db.Audit(_tenant.TenantId, Actor, "store.update", nameof(Store), id.ToString(), body);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ── WP11.2 receipt template (per store) ──

        [HttpGet("{id}/receipt-template")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)] // till (operator or device) reads its template
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetReceiptTemplate([FromRoute] int id)
        {
            var json = await _db.StoreDetails.AsNoTracking()
                .Where(d => d.StoreId == id).Select(d => d.ReceiptTemplateJson).FirstOrDefaultAsync();
            return Ok(new { storeId = id, receiptTemplateJson = json });
        }

        [HttpPut("{id}/receipt-template")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PutReceiptTemplate([FromRoute] int id, [FromBody] ReceiptTemplateBody body)
        {
            if (!await _db.Stores.AnyAsync(s => s.Id == id)) return NotFound();

            _db.CurrentUser = Actor.ToString();
            var details = await _db.StoreDetails.FirstOrDefaultAsync(d => d.StoreId == id);
            if (details == null)
                _db.StoreDetails.Add(new StoreDetails
                {
                    StoreId = id, TenantId = _tenant.TenantId, ReceiptTemplateJson = body?.ReceiptTemplateJson,
                });
            else
                details.ReceiptTemplateJson = body?.ReceiptTemplateJson;

            _db.Audit(_tenant.TenantId, Actor, "store.receipt-template", nameof(Store), id.ToString(), body);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static string Or(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public sealed record ReceiptTemplateBody(string ReceiptTemplateJson);
}
