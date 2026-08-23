#nullable disable

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    public sealed record ApproveBody(string AdminPassword);
    public sealed record RejectBody(string Reason);
    public sealed record DpaDraftBody(string Version, string Title, string BodyMarkdown, string Note);
    public sealed record DpaManualBody(Guid TenantId, string Version, string Note);

    /// <summary>
    /// **WP-SIGNUP §6 — what the operator sees, and the only route by which an application becomes
    /// a tenant.**
    ///
    /// ⚠⚠ THE OPERATOR QUEUE IS THE BACKSTOP FOR EVERY OTHER ABUSE CONTROL. Rate limits, the
    /// disposable-domain list and email verification each stop a category of junk; none of them
    /// stops a determined, plausible-looking application that should not be accepted. *"No
    /// self-serve tenant goes live unseen."*
    ///
    /// ⚠ Modelled on Platform → Quarantine: a list, a reason, an action, and a note recorded against
    /// whoever took it.
    /// </summary>
    [ApiController]
    [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
    public sealed class PlatformApplicationsController : ControllerBase
    {
        private readonly TenantApplicationService _applications;
        private readonly DpaService _dpa;
        private readonly ProvisioningService _provisioning;
        private readonly IMessageSender _mail;
        private readonly MySqlDbContext _db;

        public PlatformApplicationsController(
            TenantApplicationService applications, DpaService dpa, ProvisioningService provisioning,
            IMessageSender mail, MySqlDbContext db)
        {
            _applications = applications;
            _dpa = dpa;
            _provisioning = provisioning;
            _mail = mail;
            _db = db;
        }

        private string Actor =>
            User?.FindFirst(ClaimTypes.Name)?.Value
            ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "platform-admin";

        // ── the queue ────────────────────────────────────────────────────────────────────────────

        [HttpGet("api/v1/platform/applications")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] string status, CancellationToken ct)
            => Ok(await _applications.ListAsync(status, ct));

        /// <summary>Re-send a verification email for an application whose first one bounced.</summary>
        [HttpPost("api/v1/platform/applications/{id:guid}/resend")]
        public async Task<IActionResult> Resend([FromRoute] Guid id, CancellationToken ct)
        {
            try
            {
                var token = await _applications.ResendVerifyAsync(id, ct);
                var app = await _db.TenantApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
                await SignupMail.SendVerifyAsync(_mail, _applications, app?.ContactEmail, app?.ContactName, token, Request, ct);
                return Ok(new { sent = true });
            }
            catch (EnrolmentException ex) { return Problem(detail: ex.Message, statusCode: ex.StatusCode); }
        }

        /// <summary>
        /// Approve → provision, sandbox-first.
        ///
        /// ⚠⚠ IDEMPOTENT ON THE APPLICATION ID, because the queue is exactly where a double click
        /// happens. A second call returns the tenant the first one made, with `created: false`.
        /// </summary>
        [HttpPost("api/v1/platform/applications/{id:guid}/approve")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Approve([FromRoute] Guid id, [FromBody] ApproveBody body, CancellationToken ct)
        {
            try
            {
                var (tenantId, created) = await _applications.ApproveAsync(
                    id, _provisioning, _dpa, Actor, body?.AdminPassword, ct);
                return Ok(new { tenantId, created, isSandbox = true });
            }
            catch (EnrolmentException ex) { return Problem(detail: ex.Message, statusCode: ex.StatusCode); }
        }

        [HttpPost("api/v1/platform/applications/{id:guid}/reject")]
        public async Task<IActionResult> Reject([FromRoute] Guid id, [FromBody] RejectBody body, CancellationToken ct)
        {
            try
            {
                var app = await _applications.RejectAsync(id, body?.Reason, Actor, ct);
                await SignupMail.SendRejectionAsync(_mail, app.ContactEmail, app.ContactName, app.BusinessName, app.RejectedReason, ct);
                return Ok(new { rejected = true });
            }
            catch (EnrolmentException ex) { return Problem(detail: ex.Message, statusCode: ex.StatusCode); }
        }

        // ── the DPA documents ────────────────────────────────────────────────────────────────────

        [HttpGet("api/v1/platform/dpa")]
        public async Task<IActionResult> ListDpa(CancellationToken ct) => Ok(await _dpa.ListAsync(ct));

        /// <summary>Create or edit a DRAFT. ⚠ A published version is immutable — see the service.</summary>
        [HttpPut("api/v1/platform/dpa/{version}")]
        public async Task<IActionResult> SaveDraft([FromRoute] string version, [FromBody] DpaDraftBody body, CancellationToken ct)
        {
            try
            {
                var d = await _dpa.SaveDraftAsync(version, body?.Title, body?.BodyMarkdown, body?.Note, Actor, ct);
                return Ok(new { d.Version, d.Title, d.PublishedAtUtc, d.IsCurrent });
            }
            catch (EnrolmentException ex) { return Problem(detail: ex.Message, statusCode: ex.StatusCode); }
        }

        /// <summary>
        /// ⚠⚠ PUBLISHING IS THE ACT THAT MAKES A DOCUMENT ACCEPTABLE, and it re-raises `dpa-missing`
        /// for every tenant that has not accepted THIS version. That is deliberate: a re-issued DPA
        /// silently treated as already agreed is worse than never having asked.
        /// </summary>
        [HttpPost("api/v1/platform/dpa/{version}/publish")]
        public async Task<IActionResult> Publish([FromRoute] string version, CancellationToken ct)
        {
            try
            {
                var d = await _dpa.PublishAsync(version, Actor, ct);
                return Ok(new { d.Version, d.PublishedAtUtc, d.IsCurrent });
            }
            catch (EnrolmentException ex) { return Problem(detail: ex.Message, statusCode: ex.StatusCode); }
        }

        /// <summary>
        /// Record a DPA signed on paper.
        ///
        /// ⚠⚠ THIS IS THE OPERATOR ROUTE AND IT IS LABELLED AS SUCH EVERYWHERE. It sets
        /// `RecordedByOperator` and leaves `AcceptedByEmail` null, so no surface can render it as
        /// "accepted by the client". Matt's objection — *"So that I am not 'Ticking it for them?'"* —
        /// is answered by keeping this route honest, not by deleting it: some clients sign on paper.
        /// </summary>
        [HttpPost("api/v1/platform/dpa/record-manual")]
        public async Task<IActionResult> RecordManual([FromBody] DpaManualBody body, CancellationToken ct)
        {
            try
            {
                var a = await _dpa.RecordManuallyAsync(body?.TenantId ?? Guid.Empty, body?.Version, Actor, body?.Note, ct);
                return Ok(new { recorded = true, a.Version, a.AcceptedAtUtc, recordedByOperator = a.RecordedByOperator });
            }
            catch (EnrolmentException ex) { return Problem(detail: ex.Message, statusCode: ex.StatusCode); }
        }

        [HttpGet("api/v1/platform/dpa/status/{tenantId:guid}")]
        public async Task<IActionResult> Status([FromRoute] Guid tenantId, CancellationToken ct)
            => Ok(await _dpa.StatusAsync(tenantId, ct));
    }
}
