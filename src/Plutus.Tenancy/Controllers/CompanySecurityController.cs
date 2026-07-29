#nullable disable

using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Threading;
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
    public sealed record SetMfaBody(bool MfaRequired);

    /// <summary>
    /// Client-facing tenant security settings (portal → Company → Security). Today: the MFA/SSO
    /// requirement toggle. When ON, this tenant's users are routed to the IdP (Keycloak) by the
    /// email-first portal landing and forced to enrol an authenticator; when OFF they use the
    /// password login. Turning it ON also emails this tenant's login users a heads-up (via the
    /// notification seam — simulated until an email provider is configured in Platform →
    /// Notifications). Reads/writes only the CURRENT tenant's row (ambient tenant context), gated on
    /// portal.company.manage. NOTE: full client MFA also needs per-tenant IdP provisioning (only
    /// operators are IdP users today) — this stays a forward-looking control until that ships.
    /// </summary>
    [ApiController]
    [Route("api/v1/company/security")]
    public sealed class CompanySecurityController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly IMessageSender _mail;
        public CompanySecurityController(MySqlDbContext db, ITenantContext tenant, IMessageSender mail)
        { _db = db; _tenant = tenant; _mail = mail; }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>This tenant's security settings for the client portal.</summary>
        [HttpGet("")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Get()
        {
            var t = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == _tenant.TenantId);
            if (t == null) return NotFound();
            return Ok(new { mfaRequired = t.MfaRequired });
        }

        /// <summary>Turn this tenant's MFA/SSO requirement on or off. On the OFF→ON transition, emails
        /// the tenant's login users a heads-up. Audited.</summary>
        [HttpPut("")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Set([FromBody] SetMfaBody body, CancellationToken ct)
        {
            if (body == null) return BadRequest();
            var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == _tenant.TenantId, ct);
            if (t == null) return NotFound();

            var turnedOn = body.MfaRequired && !t.MfaRequired; // notify only on the OFF→ON transition

            _db.CurrentUser = Actor.ToString();
            t.MfaRequired = body.MfaRequired;
            _db.Audit(_tenant.TenantId, Actor, "company.security.mfa", nameof(Tenant), t.Id.ToString(),
                new { body.MfaRequired });
            await _db.SaveChangesAsync(ct);

            if (turnedOn) await NotifyUsersMfaEnabledAsync(ct);
            return NoContent();
        }

        /// <summary>Best-effort heads-up email to every login user of this tenant. Uses the tenant's
        /// own sending identity (isolation rule) and the notification seam (simulated + logged until a
        /// real email provider is configured). A mail failure must NEVER fail the toggle.</summary>
        private async Task NotifyUsersMfaEnabledAsync(CancellationToken ct)
        {
            try
            {
                var identity = await _db.TenantSendingIdentities.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == _tenant.TenantId && x.Channel == (byte)MessageChannel.Email, ct);
                var from = identity?.FromAddress ?? string.Empty;

                // Login users of this tenant. WebCredentials is unscoped and has no TenantId, so join
                // People (the user's tenant shadow) — raw SQL on the EF connection (no People DbSet).
                var emails = new List<string>();
                var conn = _db.Database.GetDbConnection();
                var wasClosed = conn.State != ConnectionState.Open;
                if (wasClosed) await conn.OpenAsync(ct);
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"SELECT DISTINCT w.Email
                                        FROM WebCredentials w
                                        JOIN People p ON p.Id = w.EmployeeId
                                        WHERE p.TenantId = @tid AND w.Email IS NOT NULL AND w.Email <> ''";
                    var pr = cmd.CreateParameter(); pr.ParameterName = "@tid"; pr.Value = _tenant.TenantId.ToString();
                    cmd.Parameters.Add(pr);
                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct)) emails.Add(reader.GetString(0));
                }
                finally { if (wasClosed) await conn.CloseAsync(); }

                const string subject = "Action needed: multi-factor authentication is now on";
                const string bodyText =
                    "Your organisation has turned on multi-factor authentication (MFA) for Plutus.\n\n" +
                    "The next time you sign in to the Plutus portal you'll be guided to set up an " +
                    "authenticator app (for example Google Authenticator, Microsoft Authenticator or " +
                    "1Password) — you scan a QR code once. After that, each sign-in needs your password " +
                    "plus a 6-digit code from that app.\n\n" +
                    "There's nothing to do before your next sign-in.";

                foreach (var email in emails)
                    await _mail.SendAsync(new OutboundMessage(_tenant.TenantId, MessageChannel.Email, email, from, subject, bodyText), ct);
            }
            catch { /* best-effort: a mail hiccup must never fail the MFA toggle */ }
        }
    }
}
