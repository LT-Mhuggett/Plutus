#nullable disable

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    public sealed record ClientAcceptBody(string Version);

    /// <summary>
    /// **WP-SIGNUP §4.4 (2) — the DPA for tenants that already exist.**
    ///
    /// ⚠⚠ EVERY TENANT PREDATING WP-SIGNUP HAS NO ACCEPTANCE, AND KAPOW IS ONE. Its `dpa-missing`
    /// signal had fired 561 times since 31 July 2026 by the time anybody noticed — which is what a
    /// permanently-red signal trains people to do. This is the route that actually clears it, by the
    /// client's own act rather than the operator's.
    ///
    /// ⚠⚠ NAGGING, NOT BLOCKING — and that is Matt's call left deliberately open in the plan, taken
    /// the safe way here. Nothing in this controller prevents a tenant selling. Blocking an existing
    /// paying customer out of their own till over a document they have not seen is a decision
    /// somebody makes on purpose; it is not a default, and it is not one to take in a commit.
    ///
    /// ⚠ Gated on `perm:portal.company.manage` — accepting a legal agreement on the business's
    /// behalf is not something a cashier does.
    /// </summary>
    [ApiController]
    [Authorize(Policy = "perm:portal.company.manage")]
    public sealed class DpaController : ControllerBase
    {
        private readonly DpaService _dpa;
        private readonly ITenantContext _tenant;

        public DpaController(DpaService dpa, ITenantContext tenant)
        {
            _dpa = dpa;
            _tenant = tenant;
        }

        /// <summary>The current agreement, plus where this tenant stands against it.</summary>
        [HttpGet("api/v1/company/dpa")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var doc = await _dpa.GetCurrentAsync(ct);
            var status = await _dpa.StatusAsync(_tenant.TenantId, ct);

            return Ok(new
            {
                // null when nothing is published — the banner must then say nothing rather than
                // inventing something to agree to.
                document = doc == null ? null : new { doc.Version, doc.Title, body = doc.BodyMarkdown, doc.PublishedAtUtc },
                status.Accepted,
                acceptedVersion = status.Version,
                status.AcceptedAtUtc,
                status.AcceptedByEmail,
                // ⚠ Non-null means an operator recorded it, NOT that the client accepted. The portal
                // renders these two differently and must keep doing so.
                status.RecordedByOperator,
                status.CurrentVersion,
                status.CurrentAccepted,
            });
        }

        /// <summary>
        /// Accept it, as the signed-in client.
        ///
        /// ⚠ The version is echoed back from what was displayed. A mismatch means the document moved
        /// under them, and accepting anyway would record consent to text they never read.
        /// </summary>
        [HttpPost("api/v1/company/dpa/accept")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Accept([FromBody] ClientAcceptBody body, CancellationToken ct)
        {
            try
            {
                var current = await _dpa.GetCurrentAsync(ct);
                if (current == null)
                    return Problem(detail: "There is no published agreement to accept.", statusCode: 409);

                if (!string.IsNullOrWhiteSpace(body?.Version)
                    && !string.Equals(body.Version, current.Version, StringComparison.Ordinal))
                    return Problem(
                        detail: $"The agreement has changed since this page was loaded (now {current.Version}). "
                              + "Reload and read it again.",
                        statusCode: 409);

                Guid? userId = Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var u) ? u : null;
                var email = User?.FindFirst(ClaimTypes.Email)?.Value ?? User?.FindFirst(ClaimTypes.Name)?.Value;

                var row = await _dpa.AcceptAsync(
                    _tenant.TenantId, userId, email, ClientIp(), Request.Headers.UserAgent.ToString(), null, ct);

                return Ok(new { accepted = true, row.Version, row.AcceptedAtUtc });
            }
            catch (EnrolmentException ex) { return Problem(detail: ex.Message, statusCode: ex.StatusCode); }
        }

        /// <summary>⚠ Evidence, so the forwarded address matters — Caddy terminates TLS in front of
        /// this and the connection address would otherwise always be the loopback. First hop only.</summary>
        private string ClientIp()
        {
            var fwd = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fwd))
            {
                var first = fwd.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}
