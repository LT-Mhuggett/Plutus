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
    public sealed record SetNotificationConfigBody(int Channel, string Provider, bool Enabled, Dictionary<string, string> Config);
    public sealed record SendTestBody(int Channel, Guid TenantId, string To);
    public sealed record SetSendingIdentityBody(int Channel, string FromAddress, string Domain, bool Verified);

    /// <summary>
    /// Notification framework (17.3 config layer) — operator dashboard surface. Pick a provider per
    /// channel and fill in its fields (secrets are write-only: redacted on read, preserved on save
    /// unless replaced), manage per-tenant sending identities, fire a test send, and read the
    /// delivery ledger. Platform-admin. The actual provider adapters are wired separately; a
    /// selected-but-unwired provider sends in SIMULATED mode so the whole flow is exercisable now.
    /// </summary>
    [ApiController]
    public sealed class PlatformNotificationsController : ControllerBase
    {
        private const string SecretSet = "__set__"; // sentinel returned for a stored secret; sent back unchanged = keep

        private readonly MySqlDbContext _db;
        private readonly IMessageSender _sender;
        public PlatformNotificationsController(MySqlDbContext db, IMessageSender sender) { _db = db; _sender = sender; }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>The providers the operator can choose from + their config field schemas.</summary>
        [HttpGet("api/v1/platform/notifications/catalogue")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult Catalogue() =>
            Ok(NotificationProviderCatalogue.All.Select(p => new
            {
                key = p.Key, label = p.Label, channel = (int)p.Channel,
                fields = p.Fields.Select(f => new { f.Name, f.Label, f.Secret, f.Required }),
            }));

        /// <summary>Current settings per channel, with secret field values redacted to a sentinel.</summary>
        [HttpGet("api/v1/platform/notifications/config")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetConfig()
        {
            var rows = await _db.NotificationSettings.AsNoTracking().ToListAsync();
            return Ok(rows.Select(r =>
            {
                var config = Parse(r.ConfigJson);
                var info = NotificationProviderCatalogue.Find(r.Provider);
                if (info != null)
                    foreach (var f in info.Fields.Where(f => f.Secret))
                        if (config.ContainsKey(f.Name) && !string.IsNullOrEmpty(config[f.Name])) config[f.Name] = SecretSet;
                return new { channel = (int)r.Channel, provider = r.Provider, enabled = r.Enabled, config, updatedAtUtc = r.UpdatedAtUtc };
            }));
        }

        /// <summary>Select a provider + set its config for a channel. Secret fields left at the
        /// redaction sentinel keep their stored value; anything else overwrites. Audited.</summary>
        [HttpPut("api/v1/platform/notifications/config")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SetConfig([FromBody] SetNotificationConfigBody body)
        {
            if (body == null || NotificationProviderCatalogue.Find(body.Provider) == null)
                return BadRequest(new { detail = "Unknown provider." });
            var channel = (byte)body.Channel;
            var row = await _db.NotificationSettings.FirstOrDefaultAsync(s => s.Channel == channel);
            var existing = Parse(row?.ConfigJson);
            var merged = new Dictionary<string, string>(body.Config ?? new());
            var info = NotificationProviderCatalogue.Find(body.Provider);
            foreach (var f in info.Fields.Where(f => f.Secret))
                if (!merged.TryGetValue(f.Name, out var v) || v == SecretSet)   // unchanged secret → keep stored
                    { if (existing.TryGetValue(f.Name, out var old)) merged[f.Name] = old; else merged.Remove(f.Name); }

            _db.CurrentUser = Actor.ToString();
            if (row == null) _db.NotificationSettings.Add(row = new NotificationSettings { Channel = channel });
            row.Provider = body.Provider;
            row.Enabled = body.Enabled;
            row.ConfigJson = JsonSerializer.Serialize(merged);
            row.UpdatedAtUtc = DateTime.UtcNow;
            row.UpdatedBy = Actor.ToString();
            // Audit WITHOUT the secret values.
            _db.Audit(Guid.Empty, Actor, "notifications.config", nameof(NotificationSettings), body.Channel.ToString(),
                new { body.Provider, body.Enabled, fields = merged.Keys });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Fire a test message through the configured provider (SIMULATED if no adapter is
        /// wired). Records a MessageEvent so the operator sees the whole path end to end.</summary>
        [HttpPost("api/v1/platform/notifications/test")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> SendTest([FromBody] SendTestBody body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.To)) return BadRequest();
            var channel = (MessageChannel)(byte)body.Channel;
            var from = await _db.TenantSendingIdentities.AsNoTracking()
                .Where(i => i.TenantId == body.TenantId && i.Channel == (byte)channel)
                .Select(i => i.FromAddress).FirstOrDefaultAsync() ?? "no-reply@plutus.local";
            var result = await _sender.SendAsync(new OutboundMessage(body.TenantId, channel, body.To, from, "Plutus test", "This is a Plutus notification test."));
            return Ok(new { result.Accepted, result.ProviderMessageId, result.Detail });
        }

        /// <summary>Recent delivery ledger entries (most recent first).</summary>
        [HttpGet("api/v1/platform/notifications/events")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Events([FromQuery] int take = 50)
        {
            var rows = await _db.MessageEvents.AsNoTracking()
                .OrderByDescending(e => e.AtUtc).Take(Math.Clamp(take, 1, 200))
                .Select(e => new { e.TenantId, channel = (int)e.Channel, e.ToAddress, e.FromAddress, status = (int)e.Status, e.ProviderMessageId, e.Detail, e.AtUtc })
                .ToListAsync();
            return Ok(rows);
        }

        // ── per-tenant sending identity ──

        [HttpGet("api/v1/platform/tenants/{id}/sending-identity")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSendingIdentities([FromRoute] Guid id) =>
            Ok(await _db.TenantSendingIdentities.AsNoTracking().Where(i => i.TenantId == id)
                .Select(i => new { channel = (int)i.Channel, i.FromAddress, i.Domain, i.Verified }).ToListAsync());

        [HttpPut("api/v1/platform/tenants/{id}/sending-identity")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> SetSendingIdentity([FromRoute] Guid id, [FromBody] SetSendingIdentityBody body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.FromAddress)) return BadRequest();
            var channel = (byte)body.Channel;
            _db.CurrentUser = Actor.ToString();
            var row = await _db.TenantSendingIdentities.FirstOrDefaultAsync(i => i.TenantId == id && i.Channel == channel);
            if (row == null) _db.TenantSendingIdentities.Add(row = new TenantSendingIdentity { Id = Uuid7.New(), TenantId = id, Channel = channel, CreatedAtUtc = DateTime.UtcNow });
            row.FromAddress = body.FromAddress.Trim();
            row.Domain = body.Domain?.Trim();
            row.Verified = body.Verified;
            _db.Audit(id, Actor, "notifications.sending-identity", nameof(TenantSendingIdentity), id.ToString(), new { body.Channel, body.FromAddress, body.Domain, body.Verified });
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
