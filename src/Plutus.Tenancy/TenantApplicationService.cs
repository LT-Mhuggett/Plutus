#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    public sealed record ApplyRequest(
        string BusinessName, string ContactName, string ContactEmail, string Phone, string RequestedRegion);

    /// <summary>What an applicant is told. ⚠ Deliberately the SAME shape whether the application is
    /// new or a duplicate — see ApplyAsync.</summary>
    public sealed record ApplyResult(Guid ApplicationId, string VerifyToken, bool IsNew);

    public sealed record ApplicationView(
        Guid Id, string BusinessName, string ContactName, string ContactEmail, string Phone,
        string RequestedRegion, string Status, bool EmailVerified, DateTime? EmailVerifiedAtUtc,
        string DpaVersionAccepted, DateTime? DpaAcceptedAtUtc, string DpaAcceptedByEmail,
        DateTime CreatedAtUtc, string CreatedFromIp, Guid? ProvisionedTenantId,
        DateTime? DecidedAtUtc, string DecidedBy, string RejectedReason);

    /// <summary>
    /// **WP-SIGNUP stages 1–3 and 5 — an application, proving a person, and the decision to provision.**
    ///
    /// ⚠⚠ AN UNAUTHENTICATED ENDPOINT THAT CREATES A TENANT IS AN OPEN DOOR. It writes rows, sends
    /// mail, and consumes a name in a shared namespace, all before anyone has proved they are a
    /// person. So a signup writes ONE `TenantApplication` and nothing else; provisioning is a
    /// separate, operator-taken decision (stage 5) against an application that has earned it.
    /// </summary>
    public sealed class TenantApplicationService
    {
        /// <summary>⚠ 48h, matching `PasswordResetService.Lifetime`. One number for "how long does a
        /// link in an inbox work" is one number to reason about.</summary>
        public static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(48);

        /// <summary>⚠ Caps verification emails per application, so knowing an application id is not
        /// a way to mail-bomb the address on it. The per-IP limiter cannot see that attack.</summary>
        public const int MaxVerifySends = 5;

        private readonly MySqlDbContext _db;
        private readonly IDisposableEmailDomains _disposable;

        public TenantApplicationService(MySqlDbContext db, IDisposableEmailDomains disposable)
        {
            _db = db;
            _disposable = disposable;
        }

        /// <summary>
        /// ⚠⚠ EVERY SAVE NEEDS AN AUDIT USER, AND THE ANONYMOUS PATHS HAVE NONE.
        /// `RepositoryContext.SaveMethods` throws `ObjectIdMissingException("CurrentUser not
        /// defined!")` before writing anything, so signup 500'd the first time it was called on the
        /// live server.
        ///
        /// ⚠ THE UNIT TESTS DID NOT CATCH IT, because their context helper sets `CurrentUser =
        /// "test"` — the double was better configured than production. There is now a test that
        /// builds the context WITHOUT one, which is what an anonymous request actually looks like.
        ///
        /// ⚠ "signup" rather than a person's name: nobody is signed in, and inventing a name in an
        /// audit column is worse than recording plainly that this came through the front door.
        /// ⚠ `??=` so an operator-initiated call (the queue's Resend) keeps its own actor.
        /// </summary>
        private void StampActor(string actor = "signup")
        {
            if (string.IsNullOrEmpty(_db.CurrentUser)) _db.CurrentUser = actor;
        }

        /// <summary>
        /// ⚠⚠ THE AUDIT WRITE MUST SHARE THE MUTATION'S SaveChanges. `AuditExtensions.Audit` only
        /// ADDS the row to the change tracker, deliberately — *"callers save it in the SAME
        /// SaveChanges as the mutation, so the trail can never disagree with the data."* Auditing
        /// from the controller after the service returned would leave the row unsaved, which is a
        /// worse outcome than not auditing: the mutation lands and its record does not.
        ///
        /// ⚠ The actor arrives as a STRING (a name or a subject id, depending on the token), so it
        /// is parsed when it is a Guid and carried in the detail either way. An unattributed audit
        /// row is still better than none, and Guid.Empty is the honest value for "the front door".
        /// </summary>
        private void AuditRow(Guid tenantId, string actor, string action, string entityType, string entityId, object detail = null)
        {
            var actorId = Guid.TryParse(actor, out var g) ? g : Guid.Empty;
            _db.Audit(tenantId, actorId, action, entityType, entityId,
                      new { actor = actor ?? "signup", detail });
        }

        // ── stage 1: an application is not a tenant ──────────────────────────────────────────────

        /// <summary>
        /// Record an application. ⚠ Returns the plaintext verify token for the caller to EMAIL — it
        /// is never stored, only its hash.
        /// </summary>
        public async Task<ApplyResult> ApplyAsync(ApplyRequest req, string ip, CancellationToken ct = default)
        {
            if (req == null) throw new EnrolmentException(400, "A request body is required.");
            StampActor();
            var name = (req.BusinessName ?? "").Trim();
            var contact = (req.ContactName ?? "").Trim();
            var email = (req.ContactEmail ?? "").Trim();

            if (name.Length < 2) throw new EnrolmentException(400, "A business name is required.");
            if (contact.Length < 2) throw new EnrolmentException(400, "A contact name is required.");
            if (!LooksLikeEmail(email)) throw new EnrolmentException(400, "A valid email address is required.");

            var emailFolded = email.ToLowerInvariant();

            // ⚠ A LIST, NOT A REGEX, and it is config rather than code — the plan says so, because a
            // disposable-domain list is wrong the day after it ships and must be updatable without a
            // deploy. Verification is a formality if the inbox is throwaway.
            if (_disposable.IsDisposable(emailFolded))
                throw new EnrolmentException(400,
                    "That looks like a temporary email address. Please use an address you will still "
                    + "have access to — we send the account's password reset there.");

            var folded = FoldName(name);
            if (folded.Length < 2) throw new EnrolmentException(400, "A business name is required.");

            var existing = await _db.TenantApplications.FirstOrDefaultAsync(a => a.BusinessNameFolded == folded, ct);
            if (existing != null)
            {
                // ⚠⚠ THE SAME ANSWER WHETHER THE NAME IS TAKEN BY THIS PERSON OR SOMEBODY ELSE, when
                // the emails differ. Telling a stranger "that business already applied" turns this
                // endpoint into an oracle for who is a Plutus customer. Same shape, same status.
                if (!string.Equals(existing.ContactEmailFolded, emailFolded, StringComparison.Ordinal))
                    return new ApplyResult(existing.Id, null, false);

                // The same person re-applying: reuse their row and re-issue a token.
                var reissued = IssueVerifyToken(existing);
                await _db.SaveChangesAsync(ct);
                return new ApplyResult(existing.Id, reissued, false);
            }

            var row = new TenantApplication
            {
                Id = Uuid7.New(),
                BusinessName = name,
                BusinessNameFolded = folded,
                ContactName = contact,
                ContactEmail = email,
                ContactEmailFolded = emailFolded,
                Phone = (req.Phone ?? "").Trim(),
                RequestedRegion = string.IsNullOrWhiteSpace(req.RequestedRegion) ? "UK" : req.RequestedRegion.Trim(),
                Status = TenantApplicationStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedFromIp = Truncate(ip, 45),
            };
            var token = IssueVerifyToken(row);
            _db.TenantApplications.Add(row);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // ⚠ The unique index on the folded name is the real reservation, so a race lands here
                // rather than producing two claims. Same non-committal answer as above.
                var winner = await _db.TenantApplications.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.BusinessNameFolded == folded, ct);
                if (winner != null) return new ApplyResult(winner.Id, null, false);
                throw;
            }

            return new ApplyResult(row.Id, token, true);
        }

        // ── stage 2: prove the email ─────────────────────────────────────────────────────────────

        public enum VerifyOutcome { Ok, UnknownToken, Expired, AlreadyVerified }

        /// <summary>
        /// Consume a verification token. ⚠ An application is INERT until this succeeds: it cannot be
        /// approved, cannot be provisioned, and does not appear in the operator's default queue.
        /// </summary>
        public async Task<(VerifyOutcome Outcome, TenantApplication App)> VerifyAsync(string token, CancellationToken ct = default)
        {
            StampActor();
            if (string.IsNullOrWhiteSpace(token)) return (VerifyOutcome.UnknownToken, null);

            var hash = CompactToken.Sha256(Crockford32.Normalise(token));
            // ⚠ Compared in the database on an indexed column. The hash is what is stored, so there
            // is nothing to compare in constant time here — the secret never reaches this method.
            var app = await _db.TenantApplications.FirstOrDefaultAsync(a => a.EmailVerifyTokenHash == hash, ct);
            if (app == null) return (VerifyOutcome.UnknownToken, null);

            if (app.EmailVerifiedAtUtc != null) return (VerifyOutcome.AlreadyVerified, app);
            if (app.EmailVerifyExpiresAtUtc == null || app.EmailVerifyExpiresAtUtc < DateTime.UtcNow)
                return (VerifyOutcome.Expired, app);

            app.EmailVerifiedAtUtc = DateTime.UtcNow;
            if (app.Status == TenantApplicationStatus.Pending) app.Status = TenantApplicationStatus.EmailVerified;
            // ⚠ Single use — burn the hash so the link in the inbox stops working.
            app.EmailVerifyTokenHash = null;
            app.EmailVerifyExpiresAtUtc = null;
            await _db.SaveChangesAsync(ct);
            return (VerifyOutcome.Ok, app);
        }

        /// <summary>Re-issue a verification token. ⚠ Capped by `MaxVerifySends`.</summary>
        public async Task<string> ResendVerifyAsync(Guid applicationId, CancellationToken ct = default)
        {
            StampActor();
            var app = await _db.TenantApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct)
                ?? throw new EnrolmentException(404, "No such application.");
            if (app.EmailVerifiedAtUtc != null) throw new EnrolmentException(409, "That email is already verified.");
            if (app.VerifySendCount >= MaxVerifySends)
                throw new EnrolmentException(429, "Too many verification emails have been sent for this application.");

            var token = IssueVerifyToken(app);
            await _db.SaveChangesAsync(ct);
            return token;
        }

        // ── stage 4's half of the application: accepting the DPA before approval ─────────────────

        /// <summary>
        /// Record that the applicant accepted the current DPA. ⚠ Requires a VERIFIED email — an
        /// acceptance from an address nobody has proved they own is not evidence of anything.
        /// </summary>
        public async Task<TenantApplication> AcceptDpaAsync(
            Guid applicationId, string version, string ip, string userAgent, CancellationToken ct = default)
        {
            StampActor();
            var app = await _db.TenantApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct)
                ?? throw new EnrolmentException(404, "No such application.");

            if (app.EmailVerifiedAtUtc == null)
                throw new EnrolmentException(409, "Verify the email address before accepting the agreement.");

            var current = await _db.DpaDocuments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.IsCurrent && d.PublishedAtUtc != null, ct)
                ?? throw new EnrolmentException(409, "There is no published agreement to accept.");

            // ⚠ The client echoes the version they were SHOWN. If it is not the current one they read
            // an older page, and accepting on their behalf would record consent to text they never saw.
            if (!string.Equals(version, current.Version, StringComparison.Ordinal))
                throw new EnrolmentException(409,
                    $"The agreement has changed since that page was loaded (now {current.Version}). Reload and read it again.");

            app.DpaVersionAccepted = current.Version;
            app.DpaAcceptedAtUtc = DateTime.UtcNow;
            app.DpaAcceptedByEmail = app.ContactEmail;
            app.DpaAcceptedIp = Truncate(ip, 45);
            app.DpaAcceptedUserAgent = Truncate(userAgent, 400);
            AuditRow(Guid.Empty, app.ContactEmail, "signup.dpa.accepted", "TenantApplication", app.Id.ToString(),
                     new { version = current.Version, ip = app.DpaAcceptedIp });
            await _db.SaveChangesAsync(ct);
            return app;
        }

        // ── the operator queue ───────────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<ApplicationView>> ListAsync(string status, CancellationToken ct = default)
        {
            var q = _db.TenantApplications.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TenantApplicationStatus>(status, true, out var s))
                q = q.Where(a => a.Status == s);
            var rows = await q.OrderByDescending(a => a.CreatedAtUtc).Take(500).ToListAsync(ct);
            return rows.Select(View).ToList();
        }

        // ── stage 5: provisioning, sandbox-first ─────────────────────────────────────────────────

        /// <summary>
        /// Approve an application and provision its tenant.
        ///
        /// ⚠⚠ IDEMPOTENT ON THE APPLICATION ID. The operator queue is exactly where a double click
        /// happens, and two tenants for one application is not something anybody unpicks cleanly. An
        /// already-provisioned application returns its existing tenant.
        ///
        /// ⚠⚠ `IsSandbox = true`, ALWAYS. A self-serve tenant that starts live is one taking real
        /// money before anyone has looked at it. Promotion out of sandbox stays an operator action.
        /// </summary>
        public async Task<(Guid TenantId, bool Created)> ApproveAsync(
            Guid applicationId, ProvisioningService provisioning, DpaService dpa, string actor,
            string adminPassword, CancellationToken ct = default)
        {
            // ⚠ The operator's own name here, not "signup" — this write is theirs.
            StampActor(string.IsNullOrWhiteSpace(actor) ? "platform-admin" : actor);
            var app = await _db.TenantApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct)
                ?? throw new EnrolmentException(404, "No such application.");

            if (app.ProvisionedTenantId is Guid already) return (already, false);

            if (app.Status == TenantApplicationStatus.Rejected)
                throw new EnrolmentException(409, "That application was rejected.");
            if (app.EmailVerifiedAtUtc == null)
                throw new EnrolmentException(409, "The email address has not been verified.");
            if (string.IsNullOrWhiteSpace(app.DpaVersionAccepted))
                throw new EnrolmentException(409,
                    "The applicant has not accepted the data processing agreement. Approving without it "
                    + "would create a tenant whose first act is a compliance gap.");
            if (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 8)
                throw new EnrolmentException(400, "An initial admin password of at least 8 characters is required.");

            var result = await provisioning.ProvisionAsync(
                new ProvisionRequest(app.BusinessName, "standard", app.ContactEmail, adminPassword, IsSandbox: true),
                actor, ct);

            // ⚠ The acceptance made against the APPLICATION is copied onto the tenant, so the evidence
            // chain survives provisioning and the compliance signal sees it.
            await CopyAcceptanceToTenantAsync(app, result.TenantId, ct);

            var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == result.TenantId, ct);
            if (tenant != null) tenant.DataRegion = app.RequestedRegion;

            app.ProvisionedTenantId = result.TenantId;
            app.Status = TenantApplicationStatus.Provisioned;
            app.DecidedAtUtc = DateTime.UtcNow;
            app.DecidedBy = actor;
            AuditRow(result.TenantId, actor, "signup.application.approved", "TenantApplication", app.Id.ToString(),
                     new { app.BusinessName, app.ContactEmail, tenantId = result.TenantId, isSandbox = true });
            await _db.SaveChangesAsync(ct);

            return (result.TenantId, true);
        }

        /// <summary>
        /// Reject an application.
        ///
        /// ⚠ THE ROW IS KEPT, NOT DELETED — the reason is emailed and a rejected application that
        /// vanishes cannot be explained to whoever asks. ⚠⚠ BUT THE NAME RESERVATION IS RELEASED, by
        /// blanking the folded name: a rejected applicant must not hold "Kapow Comics" for ever
        /// against the real one. The unique index makes the blank collide, so it is made unique with
        /// the row's own id.
        /// </summary>
        public async Task<TenantApplication> RejectAsync(Guid applicationId, string reason, string actor, CancellationToken ct = default)
        {
            StampActor(string.IsNullOrWhiteSpace(actor) ? "platform-admin" : actor);
            var app = await _db.TenantApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct)
                ?? throw new EnrolmentException(404, "No such application.");
            if (app.ProvisionedTenantId != null)
                throw new EnrolmentException(409, "That application has already been provisioned.");
            if (string.IsNullOrWhiteSpace(reason))
                throw new EnrolmentException(400, "A reason is required — it is sent to the applicant.");

            app.Status = TenantApplicationStatus.Rejected;
            app.RejectedReason = reason.Trim();
            app.DecidedAtUtc = DateTime.UtcNow;
            app.DecidedBy = actor;
            app.BusinessNameFolded = $"rejected:{app.Id:N}";
            app.EmailVerifyTokenHash = null;
            app.EmailVerifyExpiresAtUtc = null;
            AuditRow(Guid.Empty, actor, "signup.application.rejected", "TenantApplication", app.Id.ToString(),
                     new { app.BusinessName, app.ContactEmail, reason = app.RejectedReason });
            await _db.SaveChangesAsync(ct);
            return app;
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────

        private async Task CopyAcceptanceToTenantAsync(TenantApplication app, Guid tenantId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(app.DpaVersionAccepted)) return;
            if (await _db.DpaAcceptances.AnyAsync(a => a.TenantId == tenantId && a.Version == app.DpaVersionAccepted, ct)) return;

            var doc = await _db.DpaDocuments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Version == app.DpaVersionAccepted, ct);

            _db.DpaAcceptances.Add(new DpaAcceptance
            {
                Id = Uuid7.New(),
                TenantId = tenantId,
                Version = app.DpaVersionAccepted,
                BodySha256 = doc?.BodySha256,
                AcceptedAtUtc = app.DpaAcceptedAtUtc ?? DateTime.UtcNow,
                AcceptedByEmail = app.DpaAcceptedByEmail,
                AcceptedIp = app.DpaAcceptedIp,
                UserAgent = app.DpaAcceptedUserAgent,
                SourceApplicationId = app.Id,
            });

            var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (tenant != null)
            {
                tenant.DpaSignedAtUtc = app.DpaAcceptedAtUtc;
                tenant.DpaRef = app.DpaVersionAccepted;
            }
        }

        private static string IssueVerifyToken(TenantApplication app)
        {
            var token = Crockford32.NewCode(32);          // ≈160 bits, as PasswordResetService
            app.EmailVerifyTokenHash = CompactToken.Sha256(Crockford32.Normalise(token));
            app.EmailVerifyExpiresAtUtc = DateTime.UtcNow.Add(VerifyLifetime);
            app.VerifySendCount++;
            return token;
        }

        /// <summary>
        /// ⚠ THE RESERVATION KEY. Lower-cased, accents stripped, and everything that is not a letter
        /// or digit removed — so "Kapow Comics", "kapow-comics" and "KAPOW  COMICS!" are one claim.
        /// A reservation that "Kapow Comics Ltd" slips past is not a reservation.
        /// </summary>
        public static string FoldName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var decomposed = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool LooksLikeEmail(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length > 320) return false;
            var at = s.IndexOf('@');
            if (at <= 0 || at != s.LastIndexOf('@') || at == s.Length - 1) return false;
            var domain = s[(at + 1)..];
            return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !s.Contains(' ');
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

        private static ApplicationView View(TenantApplication a) => new(
            a.Id, a.BusinessName, a.ContactName, a.ContactEmail, a.Phone, a.RequestedRegion,
            a.Status.ToString(), a.EmailVerifiedAtUtc != null, a.EmailVerifiedAtUtc,
            a.DpaVersionAccepted, a.DpaAcceptedAtUtc, a.DpaAcceptedByEmail,
            a.CreatedAtUtc, a.CreatedFromIp, a.ProvisionedTenantId,
            a.DecidedAtUtc, a.DecidedBy, a.RejectedReason);
    }
}
