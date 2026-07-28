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

namespace Plutus.Tenancy.Controllers
{
    public sealed record SetBillingConfigBody(string Provider, bool Enabled, Dictionary<string, string> Config);

    /// <summary>
    /// 16.4 billing-provider configuration — operator dashboard surface. One platform-wide
    /// selection (Stripe Billing / Paddle / Chargebee / manual) with its keys (secrets write-only:
    /// redacted on read, preserved on save unless replaced). The concrete IBillingProvider adapter
    /// for the selected provider is wired separately; "manual" means no automation. The dunning
    /// REACTION already exists (the billing webhook applies status changes → PastDue/Suspended,
    /// D16 keeps tills selling) — the adapter's job is only to verify + translate provider events.
    /// </summary>
    [ApiController]
    public sealed class PlatformBillingController : ControllerBase
    {
        private const string SecretSet = "__set__";

        private readonly MySqlDbContext _db;
        public PlatformBillingController(MySqlDbContext db) => _db = db;

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpGet("api/v1/platform/billing/catalogue")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult Catalogue() =>
            Ok(BillingProviderCatalogue.All.Select(p => new
            {
                key = p.Key, label = p.Label, blurb = p.Blurb,
                fields = p.Fields.Select(f => new { f.Name, f.Label, f.Secret, f.Required }),
            }));

        [HttpGet("api/v1/platform/billing/config")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetConfig()
        {
            var row = await _db.BillingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
            var provider = row?.Provider ?? "manual";
            var config = Parse(row?.ConfigJson);
            var info = BillingProviderCatalogue.Find(provider);
            if (info != null)
                foreach (var f in info.Fields.Where(f => f.Secret))
                    if (config.ContainsKey(f.Name) && !string.IsNullOrEmpty(config[f.Name])) config[f.Name] = SecretSet;
            return Ok(new { provider, enabled = row?.Enabled ?? false, config, updatedAtUtc = row?.UpdatedAtUtc });
        }

        /// <summary>Select the billing provider + set its config. Secret fields left at the
        /// redaction sentinel keep their stored value. Audited (without secret values).</summary>
        [HttpPut("api/v1/platform/billing/config")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SetConfig([FromBody] SetBillingConfigBody body)
        {
            var info = body == null ? null : BillingProviderCatalogue.Find(body.Provider);
            if (info == null) return BadRequest(new { detail = "Unknown billing provider." });

            var row = await _db.BillingSettings.FirstOrDefaultAsync(s => s.Id == 1);
            var existing = Parse(row?.ConfigJson);
            var merged = new Dictionary<string, string>(body.Config ?? new());
            foreach (var f in info.Fields.Where(f => f.Secret))
                if (!merged.TryGetValue(f.Name, out var v) || v == SecretSet)
                    { if (existing.TryGetValue(f.Name, out var old)) merged[f.Name] = old; else merged.Remove(f.Name); }

            _db.CurrentUser = Actor.ToString();
            if (row == null) _db.BillingSettings.Add(row = new BillingSettings { Id = 1 });
            row.Provider = body.Provider;
            row.Enabled = body.Enabled;
            row.ConfigJson = JsonSerializer.Serialize(merged);
            row.UpdatedAtUtc = DateTime.UtcNow;
            row.UpdatedBy = Actor.ToString();
            _db.Audit(Guid.Empty, Actor, "billing.config", nameof(BillingSettings), "1",
                new { body.Provider, body.Enabled, fields = merged.Keys });
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
