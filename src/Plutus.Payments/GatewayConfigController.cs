#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Payments
{
    public sealed record SetGatewayBody(
        string Provider, Dictionary<string, string> Config,
        int SurchargeBp = 0, long SurchargeFlatPence = 0);

    /// <summary>
    /// 17.2 per-tenant payment-gateway configuration — the CLIENT-facing surface (each tenant picks
    /// their own gateway; requirements differ per client). "standalone" is the default and means
    /// exactly today's flow: take payment on an external chip &amp; pin terminal, confirm approval
    /// on the till — no integration. Config writes need portal.company.manage; the till reads only
    /// `/active` (provider key + label + integration status, never the config). All rows are
    /// tenant-owned, so the ambient tenant context scopes everything.
    /// </summary>
    [ApiController]
    [Route("api/v1/payments/gateway")]
    public sealed class GatewayConfigController : ControllerBase
    {
        private const string SecretSet = "__set__";

        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        public GatewayConfigController(MySqlDbContext db, ITenantContext tenant) { _db = db; _tenant = tenant; }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>The gateways a tenant can choose from (+ field schemas). Any authenticated user.</summary>
        [HttpGet("catalogue")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult Catalogue() =>
            Ok(PaymentProviderCatalogue.All.Select(p => new
            {
                key = p.Key, label = p.Label, blurb = p.Blurb,
                fields = p.Fields.Select(f => new { f.Name, f.Label, f.Secret, f.Required }),
            }));

        /// <summary>What the TILL needs: the tenant's selected provider + whether an integration is
        /// actually wired (none are yet — every non-standalone selection falls back to the
        /// standalone flow so selling never blocks). Never returns config.</summary>
        [HttpGet("active")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Active()
        {
            var row = await _db.PaymentGatewaySettings.AsNoTracking().FirstOrDefaultAsync();
            var key = row?.Provider ?? PaymentProviderCatalogue.Standalone;
            var info = PaymentProviderCatalogue.Find(key) ?? PaymentProviderCatalogue.Find(PaymentProviderCatalogue.Standalone);
            return Ok(new
            {
                provider = info.Key,
                label = info.Label,
                // true once a concrete terminal integration is wired for this provider; the till
                // uses the standalone confirm flow whenever this is false.
                integrated = false,
                // The tenant's card surcharge (percent bp + flat pence; both zero = none). The till
                // applies it as a CARD-SURCHARGE line whose VAT FOLLOWS THE BASKET
                // (SharedKernel.CardSurchargeVat — Bookit C-607/14 / NEC C-130/15), never a
                // hardcoded rate.
                surchargeBp = row?.SurchargeBp ?? 0,
                surchargeFlatPence = row?.SurchargeFlatPence ?? 0,
            });
        }

        /// <summary>Current gateway config for the client portal (secrets redacted).</summary>
        [HttpGet("")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Get()
        {
            var row = await _db.PaymentGatewaySettings.AsNoTracking().FirstOrDefaultAsync();
            var provider = row?.Provider ?? PaymentProviderCatalogue.Standalone;
            var config = Parse(row?.ConfigJson);
            var info = PaymentProviderCatalogue.Find(provider);
            if (info != null)
                foreach (var f in info.Fields.Where(f => f.Secret))
                    if (config.ContainsKey(f.Name) && !string.IsNullOrEmpty(config[f.Name])) config[f.Name] = SecretSet;
            return Ok(new
            {
                provider, config, updatedAtUtc = row?.UpdatedAtUtc,
                surchargeBp = row?.SurchargeBp ?? 0,
                surchargeFlatPence = row?.SurchargeFlatPence ?? 0,
            });
        }

        /// <summary>Select this tenant's gateway + set its config. Secrets left at the redaction
        /// sentinel keep their stored value. Audited (without secret values).</summary>
        [HttpPut("")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Set([FromBody] SetGatewayBody body)
        {
            var info = body == null ? null : PaymentProviderCatalogue.Find(body.Provider);
            if (info == null) return BadRequest(new { detail = "Unknown payment provider." });

            // ⚠ Typo guards, not policy. The lawful ceiling (where surcharging is lawful at all) is
            // the merchant's own cost of acceptance, which the platform cannot know — but no
            // acquirer on earth charges 10% + £5, so anything past these is a slipped digit that
            // would surcharge every customer until somebody noticed.
            if (body.SurchargeBp is < 0 or > 1000)
                return BadRequest(new { detail = "The surcharge percentage must be between 0 and 10% (0–1000 basis points)." });
            if (body.SurchargeFlatPence is < 0 or > 500)
                return BadRequest(new { detail = "The flat surcharge must be between 0 and £5.00." });

            var row = await _db.PaymentGatewaySettings.FirstOrDefaultAsync();
            var existing = Parse(row?.ConfigJson);
            var merged = new Dictionary<string, string>(body.Config ?? new());
            foreach (var f in info.Fields.Where(f => f.Secret))
                if (!merged.TryGetValue(f.Name, out var v) || v == SecretSet)
                    { if (existing.TryGetValue(f.Name, out var old)) merged[f.Name] = old; else merged.Remove(f.Name); }

            _db.CurrentUser = Actor.ToString();
            if (row == null)
                _db.PaymentGatewaySettings.Add(row = new PaymentGatewaySettings { Id = Uuid7.New(), TenantId = _tenant.TenantId });
            row.Provider = info.Key;
            row.ConfigJson = JsonSerializer.Serialize(merged);
            row.SurchargeBp = body.SurchargeBp;
            row.SurchargeFlatPence = body.SurchargeFlatPence;
            row.UpdatedAtUtc = DateTime.UtcNow;
            row.UpdatedBy = Actor.ToString();
            // ⚠ The surcharge is IN the audit record: it is money charged to every card customer,
            // and "who turned this on, when" is the first question after a complaint.
            _db.Audit(_tenant.TenantId, Actor, "payments.gateway", nameof(PaymentGatewaySettings), row.Id.ToString(),
                new { info.Key, fields = merged.Keys, body.SurchargeBp, body.SurchargeFlatPence });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private static Dictionary<string, string> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
            catch { return new(); }
        }
    }
}
