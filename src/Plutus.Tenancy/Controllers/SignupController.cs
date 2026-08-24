#nullable disable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    public sealed record SignupBody(string BusinessName, string ContactName, string ContactEmail, string Phone, string Region);
    public sealed record AcceptDpaBody(Guid ApplicationId, string Version);

    /// <summary>
    /// **WP-SIGNUP — the front door. Everything here is anonymous, and that is the whole risk.**
    ///
    /// ⚠⚠ AN UNAUTHENTICATED ENDPOINT THAT CREATES A TENANT IS AN OPEN DOOR, so none of these create
    /// one. They create and advance a `TenantApplication`; provisioning is an operator decision taken
    /// elsewhere (`PlatformApplicationsController`). That separation is stage 1 of the plan and the
    /// reason the rest of it is safe.
    ///
    /// ⚠ EVERY ROUTE IS RATE-LIMITED on the "enrol" policy — a fixed window keyed by remote IP, the
    /// same one the till enrolment endpoints use. ⚠ It is per-IP only, so the service ALSO caps
    /// verification sends per application (`MaxVerifySends`): one IP with many emails and one email
    /// hit from many IPs are different attacks and the limiter only sees the first.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [EnableRateLimiting("enrol")]
    public sealed class SignupController : ControllerBase
    {
        private readonly TenantApplicationService _applications;
        private readonly DpaService _dpa;
        private readonly IMessageSender _mail;
        private readonly SignupGate _gate;

        public SignupController(
            TenantApplicationService applications, DpaService dpa, IMessageSender mail, SignupGate gate)
        {
            _applications = applications;
            _dpa = dpa;
            _mail = mail;
            _gate = gate;
        }

        /// <summary>
        /// ⚠⚠ EVERY ROUTE HERE IS BEHIND THE `signup.public` FLAG, AND IT DEFAULTS TO CLOSED.
        /// The API was reachable from the internet the moment it shipped — Caddy proxies `/api/*` on
        /// the till host and these routes are anonymous — so a POST from outside created an
        /// application on 2026-08-23 with no landing page in existence. See <see cref="SignupGate"/>.
        ///
        /// ⚠ 404, not 403: a 403 advertises that a signup API exists and is merely switched off.
        /// </summary>
        private async Task<IActionResult> ClosedAsync(CancellationToken ct) =>
            await _gate.IsOpenAsync(ct) ? null : NotFound();

        /// <summary>
        /// Apply for a tenancy.
        ///
        /// ⚠⚠ THE RESPONSE IS THE SAME SHAPE WHETHER THE APPLICATION IS NEW OR THE NAME IS TAKEN.
        /// Distinguishing them turns this into an oracle for "is this business a Plutus customer?",
        /// which is a disclosure an unauthenticated endpoint has no business making.
        /// </summary>
        [HttpPost("api/v1/signup")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> Apply([FromBody] SignupBody body, CancellationToken ct)
        {
            if (await ClosedAsync(ct) is {} closed) return closed;
            try
            {
                var result = await _applications.ApplyAsync(
                    new ApplyRequest(body?.BusinessName, body?.ContactName, body?.ContactEmail, body?.Phone, body?.Region),
                    ClientIp(), ct);

                // ⚠ MAIL FAILURE MUST NOT FAIL THE CALLER — WP-signup's DoD says a bounced
                // verification email leaves a RECOVERABLE application, not a dead row. The row is
                // saved; an operator can resend from the queue.
                if (result.VerifyToken != null)
                    await SignupMail.SendVerifyAsync(_mail, _applications, body?.ContactEmail, body?.ContactName,
                                                     result.VerifyToken, Request, ct);

                return Accepted(new
                {
                    applicationId = result.ApplicationId,
                    // Deliberately vague, and identical either way.
                    detail = "Thanks — check your email for a link to confirm the address.",
                });
            }
            catch (EnrolmentException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        }

        /// <summary>Confirm the email address. ⚠ Single-use and 48h — the token is burned on success.</summary>
        [HttpPost("api/v1/signup/verify")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Verify([FromQuery] string token, CancellationToken ct)
        {
            if (await ClosedAsync(ct) is {} closed) return closed;
            var (outcome, app) = await _applications.VerifyAsync(token, ct);
            return outcome switch
            {
                TenantApplicationService.VerifyOutcome.Ok =>
                    Ok(new { applicationId = app.Id, businessName = app.BusinessName, verified = true }),
                TenantApplicationService.VerifyOutcome.AlreadyVerified =>
                    Ok(new { applicationId = app.Id, businessName = app.BusinessName, verified = true, detail = "Already confirmed." }),
                TenantApplicationService.VerifyOutcome.Expired =>
                    Problem(detail: "That link has expired. Request another from the signup page.", statusCode: 410),
                _ => Problem(detail: "That link is not valid.", statusCode: 404),
            };
        }

        /// <summary>
        /// The agreement an applicant must read.
        ///
        /// ⚠⚠ 409 WHEN THERE IS NO PUBLISHED DPA, NEVER A PLACEHOLDER. WP-signup §4.3: *"shipping a
        /// signup that records acceptance of placeholder text would be worse than having no DPA at
        /// all — it manufactures evidence that a client agreed to something nobody wrote."*
        /// </summary>
        [HttpGet("api/v1/signup/dpa")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CurrentDpa(CancellationToken ct)
        {
            if (await ClosedAsync(ct) is {} closed) return closed;
            var doc = await _dpa.GetCurrentAsync(ct);
            if (doc == null)
                return Problem(
                    detail: "No data processing agreement has been published yet, so signup cannot be completed. "
                          + "This is deliberate: recording acceptance of unpublished wording would be evidence of nothing.",
                    statusCode: 409);

            return Ok(new { version = doc.Version, title = doc.Title, body = doc.BodyMarkdown, publishedAtUtc = doc.PublishedAtUtc });
        }

        /// <summary>Accept it. ⚠ Requires a verified email and the version the applicant was SHOWN.</summary>
        [HttpPost("api/v1/signup/dpa/accept")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AcceptDpa([FromBody] AcceptDpaBody body, CancellationToken ct)
        {
            if (await ClosedAsync(ct) is {} closed) return closed;
            try
            {
                var app = await _applications.AcceptDpaAsync(
                    body?.ApplicationId ?? Guid.Empty, body?.Version,
                    ClientIp(), Request.Headers.UserAgent.ToString(), ct);

                return Ok(new { accepted = true, version = app.DpaVersionAccepted, atUtc = app.DpaAcceptedAtUtc });
            }
            catch (EnrolmentException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        }

        /// <summary>
        /// ⚠ The client's address as the server sees it, and it is EVIDENCE — recorded against a DPA
        /// acceptance. `X-Forwarded-For` is honoured because Caddy terminates TLS in front of this;
        /// without it every acceptance would record the loopback address and be worth nothing.
        /// ⚠ First hop only: the rest of the header is attacker-supplied.
        /// </summary>
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
