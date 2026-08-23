#nullable disable

using System;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities.Models
{
    /// <summary>Where an application has got to. ⚠ Ordered by progression, and the numbers are
    /// persisted — insert new states at the END, never in the middle.</summary>
    public enum TenantApplicationStatus : byte
    {
        Pending = 0,
        EmailVerified = 1,
        Approved = 2,
        Provisioned = 3,
        Rejected = 4,
    }

    /// <summary>
    /// **Somebody has asked for a tenancy. They have not got one.**
    ///
    /// ⚠⚠ THE SINGLE MOST IMPORTANT DECISION IN WP-SIGNUP, and the reason this table exists rather
    /// than an anonymous call to provisioning: a signup writes ONE row here and nothing else. No
    /// tenant, no company, no store, no login, no row in any table a live tenant touches.
    ///
    /// Everything that makes self-serve dangerous — abuse, duplicates, spam, a business name someone
    /// else wanted — becomes a row an operator can delete, instead of a tenant somebody has to unpick.
    /// Provisioning is a DECISION, taken against an application that has already earned it.
    ///
    /// ⚠ GLOBAL / UNSCOPED, deliberately, and the precedent is `EnrolmentCode`: the anonymous signup
    /// and verify endpoints reach this before any tenant exists, so there is no tenant to scope it to.
    /// `ProvisionedTenantId` is carried as DATA once one does.
    /// </summary>
    public class TenantApplication
    {
        public Guid Id { get; set; }

        [Required, MaxLength(200)] public string BusinessName { get; set; }

        /// <summary>⚠ The name folded for comparison — lower-cased, punctuation and spacing removed.
        /// UNIQUE, and it is what reserves the name: "Kapow Comics" and "kapow-comics" are the same
        /// claim, and two applications for one name resolved by whoever got provisioned first is the
        /// race this prevents.</summary>
        [Required, MaxLength(200)] public string BusinessNameFolded { get; set; }

        [Required, MaxLength(200)] public string ContactName { get; set; }
        [Required, MaxLength(320)] public string ContactEmail { get; set; }

        /// <summary>⚠ Lower-cased, for the per-email rate limit and duplicate detection. An address
        /// is not case-sensitive in practice and treating it as such is a free bypass.</summary>
        [Required, MaxLength(320)] public string ContactEmailFolded { get; set; }

        [MaxLength(50)] public string Phone { get; set; }

        /// <summary>Feeds `Tenant.DataRegion` at provisioning. "UK" today.</summary>
        [Required, MaxLength(10)] public string RequestedRegion { get; set; } = "UK";

        public TenantApplicationStatus Status { get; set; } = TenantApplicationStatus.Pending;

        /// <summary>
        /// ⚠⚠ THE HASH, NEVER THE TOKEN. A leaked applications table must not be a set of working
        /// verification links — the same rule as `PasswordResetToken` and `EnrolmentCode`.
        /// </summary>
        [MaxLength(64)] public byte[] EmailVerifyTokenHash { get; set; }

        public DateTime? EmailVerifyExpiresAtUtc { get; set; }
        public DateTime? EmailVerifiedAtUtc { get; set; }

        /// <summary>⚠ Counts verification emails sent, so a resend is rate-limited by more than the
        /// per-IP window — an attacker with one application id could otherwise mail-bomb one address.</summary>
        public int VerifySendCount { get; set; }

        // ── the DPA, accepted against the APPLICATION and copied onto the tenant at provisioning ──

        /// <summary>The version string accepted, e.g. "2026-01". ⚠ Null means not accepted, and an
        /// application with a null here cannot be approved.</summary>
        [MaxLength(40)] public string DpaVersionAccepted { get; set; }

        public DateTime? DpaAcceptedAtUtc { get; set; }
        [MaxLength(320)] public string DpaAcceptedByEmail { get; set; }

        /// <summary>⚠ Evidence. A dispute about whether a DPA was accepted is settled by who, when,
        /// and from where.</summary>
        [MaxLength(45)] public string DpaAcceptedIp { get; set; }
        [MaxLength(400)] public string DpaAcceptedUserAgent { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        [MaxLength(45)] public string CreatedFromIp { get; set; }

        public Guid? ProvisionedTenantId { get; set; }
        public DateTime? DecidedAtUtc { get; set; }
        [MaxLength(200)] public string DecidedBy { get; set; }
        [MaxLength(1000)] public string RejectedReason { get; set; }
    }

    /// <summary>
    /// **A version of the Data Processing Agreement.**
    ///
    /// ⚠⚠ THE TEXT IS DATA, NOT CODE, AND THAT IS THE WHOLE POINT. A DPA is a legal instrument that
    /// gets revised; each revision needs its own acceptance records, and a solicitor's wording must
    /// be publishable without a redeploy. Hard-coding the body would make every future revision an
    /// engineering task and would tie the evidence trail to a git history nobody will read.
    ///
    /// ⚠ DRAFT UNTIL DEPLOYED, DELIBERATELY. A row with `PublishedAtUtc == null` cannot be accepted
    /// by anybody. WP-signup §4.3 is explicit that *"shipping a signup that records acceptance of
    /// placeholder text would be worse than having no DPA at all — it manufactures evidence that a
    /// client agreed to something nobody wrote"*, so the mechanism refuses rather than trusts.
    /// </summary>
    public class DpaDocument
    {
        public Guid Id { get; set; }

        /// <summary>⚠ UNIQUE, and it is what an acceptance names. Accepting "2026-01" is not
        /// accepting "2027-04".</summary>
        [Required, MaxLength(40)] public string Version { get; set; }

        [Required, MaxLength(300)] public string Title { get; set; }

        /// <summary>The agreement itself, markdown. ⚠ Rendered as TEXT on both surfaces, never as
        /// HTML — an operator-supplied document that can inject script into a client's browser is a
        /// stored XSS with a legal document as the payload.</summary>
        [Required] public string BodyMarkdown { get; set; }

        /// <summary>⚠ SHA-256 of the body at publish. What an acceptance is really evidence OF: a
        /// version string can be reused by mistake, bytes cannot. Compared on every acceptance.</summary>
        [MaxLength(64)] public byte[] BodySha256 { get; set; }

        /// <summary>Null = DRAFT. ⚠ An unpublished document cannot be accepted and is not served to
        /// applicants.</summary>
        public DateTime? PublishedAtUtc { get; set; }

        /// <summary>⚠ Exactly one document may be current. Publishing a new one clears the old, and
        /// re-raises `dpa-missing` for every tenant that has not accepted THIS version — otherwise a
        /// re-issued DPA is silently treated as already agreed, which is worse than never asking.</summary>
        public bool IsCurrent { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        [MaxLength(200)] public string CreatedBy { get; set; }
        [MaxLength(200)] public string PublishedBy { get; set; }

        /// <summary>⚠ Why this draft exists / what changed. Shown to the operator, never to clients.</summary>
        [MaxLength(1000)] public string InternalNote { get; set; }
    }

    /// <summary>
    /// **A tenant accepted a specific version of the DPA.** The evidence row.
    ///
    /// ⚠⚠ TWO ROUTES, AND THEY MUST NEVER LOOK THE SAME. A client clicking accept sets
    /// `AcceptedByEmail`; an operator recording a paper signature sets `RecordedByOperator` and
    /// leaves `AcceptedByEmail` null. The portal shows the second as *"recorded manually by …"*,
    /// never as *"accepted by the client"* — because an operator-entered date is evidence that Matt
    /// believes a DPA was signed, not that the client agreed to anything. That distinction is the
    /// whole reason WP-signup exists.
    /// </summary>
    public class DpaAcceptance
    {
        public Guid Id { get; set; }

        /// <summary>⚠ Real column, and the row is GLOBAL rather than tenant-filtered: the operator
        /// console reads acceptances across every tenant, and the compliance sweep runs unscoped.</summary>
        public Guid TenantId { get; set; }

        [Required, MaxLength(40)] public string Version { get; set; }

        /// <summary>⚠ The body hash as accepted. If a published document is ever edited in place,
        /// this is what proves what the client actually saw.</summary>
        [MaxLength(64)] public byte[] BodySha256 { get; set; }

        public DateTime AcceptedAtUtc { get; set; }

        // ── route 1: the client accepted it themselves ──
        public Guid? AcceptedByUserId { get; set; }
        [MaxLength(320)] public string AcceptedByEmail { get; set; }
        [MaxLength(45)] public string AcceptedIp { get; set; }
        [MaxLength(400)] public string UserAgent { get; set; }

        // ── route 2: an operator recorded a signature made elsewhere ──
        /// <summary>⚠ Non-null means NOBODY CLICKED ACCEPT IN PLUTUS. Must be surfaced differently.</summary>
        [MaxLength(200)] public string RecordedByOperator { get; set; }
        [MaxLength(1000)] public string OperatorNote { get; set; }

        /// <summary>Set when the acceptance came in through signup rather than the portal, so the
        /// evidence chain back to the application survives provisioning.</summary>
        public Guid? SourceApplicationId { get; set; }
    }
}
