#nullable disable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    public sealed record ResetRequestBody(string Email);
    public sealed record ResetCompleteBody(string Token, string NewPassword);

    /// <summary>
    /// FE9.1 self-service password reset — the login page's "Forgot password?" and the completion
    /// form behind the emailed link. ANONYMOUS by necessity (the user cannot sign in), so both
    /// endpoints are rate-limited on the shared "enrol" policy.
    ///
    /// The request endpoint always returns 204, whether or not the email matches an account: a
    /// different answer would turn this into an account-enumeration oracle.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [EnableRateLimiting("enrol")]
    public sealed class PasswordResetController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly PasswordResetService _reset;
        private readonly IMessageSender _mail;

        public PasswordResetController(MySqlDbContext db, PasswordResetService reset, IMessageSender mail)
        {
            _db = db;
            _reset = reset;
            _mail = mail;
        }

        /// <summary>"Forgot password?" — emails a link when the address has an account. Always 204.</summary>
        [HttpPost("api/auth/password-reset/request")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RequestReset([FromBody] ResetRequestBody body, CancellationToken ct)
        {
            var email = body?.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email)) return NoContent();

            // cross-tenant lookup: an anonymous caller has no tenant context
            var cred = await _db.WebCredentials.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Email == email, ct);
            if (cred == null) return NoContent();                      // silent — no enumeration
            var user = await _db.Employees.IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == cred.EmployeeId, ct);
            if (user == null || !user.Active) return NoContent();

            // Person (Employee's TPT root) carries TenantId as a SHADOW property — read it that way.
            var tenantId = await _db.Employees.IgnoreQueryFilters()
                .Where(e => e.Id == user.Id)
                .Select(e => EF.Property<Guid>(e, "TenantId"))
                .FirstOrDefaultAsync(ct);

            _db.CurrentUser = user.Id.ToString();
            await _reset.InvalidateOutstandingAsync(user.Id, ct);
            var (token, row) = _reset.Mint(tenantId, user.Id, email, isInvite: false, requestedBy: null);
            _db.PasswordResetTokens.Add(row);
            _db.Audit(tenantId, user.Id, "user.password.reset.request", nameof(Employee), user.Id.ToString(),
                new { email, selfService = true });
            await _db.SaveChangesAsync(ct);

            await PasswordResetMail.SendAsync(_db, _mail, tenantId, email,
                $"{user.FName} {user.LName}".Trim(), token, isInvite: false, Request, ct);
            return NoContent();
        }

        /// <summary>Complete a reset with the emailed token. 410 for a used/expired/unknown token —
        /// deliberately one status for all three so it can't be probed for valid tokens.</summary>
        [HttpPost("api/auth/password-reset/complete")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status410Gone)]
        public async Task<IActionResult> CompleteReset([FromBody] ResetCompleteBody body, CancellationToken ct)
        {
            var outcome = await _reset.CompleteAsync(body?.Token, body?.NewPassword, ct);
            return outcome switch
            {
                PasswordResetService.CompleteOutcome.Ok => NoContent(),
                PasswordResetService.CompleteOutcome.WeakPassword =>
                    BadRequest(new { detail = $"Choose a password of at least {PasswordResetService.MinPasswordLength} characters." }),
                _ => StatusCode(StatusCodes.Status410Gone,
                    new { detail = "This link has expired or has already been used. Ask for a new one." }),
            };
        }
    }
}
