using System;

namespace Plutus.SharedKernel;

/// <summary>Why a discount cannot be recorded. The caller turns this into a sentence — same split as
/// <see cref="DiscountDecision"/>, and for the same reasons.</summary>
public enum DiscountAuditVerdict : byte
{
    Recordable = 0,

    /// <summary>No reason was given. ⚠ This REFUSES the discount rather than recording a blank,
    /// because binding default 22(c) makes the reason mandatory — see <see cref="DiscountAudit"/>.</summary>
    NoReason = 1,

    /// <summary>A step-up was needed and nobody authorised it. ⚠ Distinct from
    /// <see cref="NoReason"/> because the operator's next action is different: find a supervisor,
    /// not think of a reason.</summary>
    NoAuthoriser = 2,

    /// <summary>The authoriser and the operator are the same person. ⚠ `OperatorLogin` already
    /// refuses this at sign-in; this is the second guard, on the RECORD rather than the act —
    /// see <see cref="DiscountAudit"/>.</summary>
    SelfAuthorised = 3,
}

/// <summary>
/// Who authorised a discount, and why — the record binding default 22(c) requires.
///
/// ⚠ MONEY AND IDENTITY ONLY, NO SENTENCES. <see cref="Reason"/> is the operator's own words and is
/// the one free-text field; everything else is an id, a name or pence, so a report can group by them.
/// </summary>
/// <param name="Reason">Why the discount was given, normalised by
/// <see cref="DiscountAudit.NormaliseReason"/>. ⚠ Never null on a recordable authority.</param>
/// <param name="AmountPence">What came off, positive. ⚠ Recorded even though the line already
/// carries it: a line can attract TWO discounts (a member's tier rate and a manual one), and
/// `IngestLine.DiscountPence` is their sum. Without this, an auditor can see that £5 came off and
/// never learn that £3 of it was authorised by a supervisor and £2 was not.</param>
/// <param name="RequestedByUserId">The signed-in operator who applied it — Matt's "logged-in
/// employee". ⚠ Also on the sale header as `OperatorUserId`; kept here because a discount can
/// outlive the header's meaning once one sale carries several, and a report that joins them wants
/// the pair side by side.</param>
/// <param name="AuthorisedByUserId">The supervisor who stepped up, or null when the discount was
/// within the operator's OWN ceiling. ⚠ NULL IS A REAL ANSWER, not missing data — it means "no
/// step-up was required", which is itself the audit answer to "who approved this?".</param>
/// <param name="AuthorisedByName">The supervisor's name as the roster had it AT THE TIME. ⚠ Stored,
/// not resolved later: staff leave, and an audit trail that renders "(deleted user)" answers nothing.</param>
public readonly record struct DiscountAuthority(
    string Reason,
    long AmountPence,
    Guid RequestedByUserId,
    Guid? AuthorisedByUserId,
    string? AuthorisedByName);

/// <summary>
/// What a discount must record before it is allowed to happen.
///
/// ⚠⚠ MATT, 2026-08-13: *"All discounts need to be tracked — till, logged-in employee and reason."*
/// Binding default 22(c). Of the three, the till and the employee were already on the sale header
/// (`DeviceId` from the token, `OperatorUserId`); **the reason was collected nowhere on any till**,
/// and the supervisor who authorised an over-ceiling discount was written to the till's LOCAL LOG
/// and nowhere else. So *"who approved this discount, and why?"* could only be answered by walking
/// to that till and reading a file — and a re-imaged till has none.
///
/// ⚠ THE REASON IS MANDATORY, AND THAT IS THE WHOLE POINT. An optional reason is an empty column:
/// the one discount anybody ever asks about is the one where nobody typed anything. So a discount
/// with no reason is REFUSED, on both tills, rather than recorded blank. That is a deliberate cost —
/// it is one more thing to type at a counter with a queue — and it is what "all discounts need to be
/// tracked" means if it means anything.
///
/// ⚠ SELF-AUTHORISATION IS REFUSED TWICE. `OperatorLogin.AuthoriseOverrideAsync` already refuses it
/// when the credentials are entered; this refuses it again when the record is built. Two guards on
/// one rule is right here for the same reason `LineDiscounts.Percentage` independently re-guards
/// returns: the first protects the ACT, this protects the RECORD, and a record that says a cashier
/// approved their own £50 discount is worse than no record at all — it is an audit trail that lies.
/// ⚠ Do not "simplify" either away on the grounds that the other exists.
///
/// ⚠ THIS DOES NOT REPLACE THE CEILING. <see cref="TillGate"/>/`PermissionResolution.Can` decides
/// whether the discount may happen at that amount; <see cref="BasketDiscounts"/> decides whether the
/// basket has room. This decides only whether what happened can be WRITTEN DOWN properly. All three
/// gates run, in that order, and none subsumes another.
/// </summary>
public static class DiscountAudit
{
    /// <summary>
    /// The longest reason that will be stored.
    ///
    /// ⚠ It is a cap, not a validation: a reason is TRUNCATED to this, never refused for length.
    /// Refusing at a counter because somebody typed too much would lose the whole discount over
    /// something nobody can fix quickly. ⚠ It exists because the reason rides in
    /// `SaleLine.DiscountsJson`, which is repeated on EVERY line a basket-wide discount apportions
    /// onto — an unbounded paste would be stored once per line, on every sale, for ever.
    /// </summary>
    public const int MaxReasonLength = 200;

    /// <summary>
    /// The operator's words, in the form they are stored in.
    ///
    /// ⚠ Returns null for anything that is not a reason — empty, whitespace, or nothing at all — so
    /// callers have ONE test for "is there a reason" and cannot accidentally store `"   "`, which
    /// looks present to a query and is blank to a human.
    ///
    /// ⚠ Interior whitespace is collapsed, so "damaged    box" and "damaged box" group together in a
    /// report instead of reading as two distinct reasons.
    /// </summary>
    public static string? NormaliseReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var collapsed = System.Text.RegularExpressions.Regex.Replace(raw.Trim(), @"\s+", " ");
        if (collapsed.Length == 0) return null;

        return collapsed.Length <= MaxReasonLength
            ? collapsed
            : collapsed.Substring(0, MaxReasonLength);
    }

    /// <summary>
    /// May this discount be recorded — and if so, as what?
    ///
    /// ⚠ The authority comes BACK OUT of this method rather than being built by the caller, so the
    /// normalised reason and the refusal are decided in one place. A caller that normalised
    /// separately would eventually store the raw string it validated the trimmed one from.
    /// </summary>
    /// <param name="stepUpWasRequired">True when the amount exceeded the operator's own ceiling, so
    /// a supervisor had to authorise it. ⚠ Passed IN rather than inferred from
    /// <paramref name="authorisedByUserId"/> being present: inferring it would mean a step-up that
    /// silently failed to record an authoriser reads as "no step-up was needed", which is precisely
    /// the case this exists to catch.</param>
    public static (DiscountAuditVerdict Verdict, DiscountAuthority Authority) Authorise(
        string? reason,
        long amountPence,
        Guid requestedByUserId,
        bool stepUpWasRequired,
        Guid? authorisedByUserId,
        string? authorisedByName)
    {
        var normalised = NormaliseReason(reason);
        var authority = new DiscountAuthority(
            normalised ?? string.Empty,
            amountPence < 0 ? -amountPence : amountPence,
            requestedByUserId,
            authorisedByUserId,
            string.IsNullOrWhiteSpace(authorisedByName) ? null : authorisedByName!.Trim());

        if (normalised is null)
            return (DiscountAuditVerdict.NoReason, authority);

        if (stepUpWasRequired && authorisedByUserId is not Guid)
            return (DiscountAuditVerdict.NoAuthoriser, authority);

        // ⚠ Guid.Empty is not an authoriser. An unset id compared against an unset requester would
        // otherwise pass as "two different people" on one path and "self-approval" on another.
        if (authorisedByUserId is Guid authoriser &&
            authoriser != Guid.Empty &&
            authoriser == requestedByUserId)
            return (DiscountAuditVerdict.SelfAuthorised, authority);

        return (DiscountAuditVerdict.Recordable, authority);
    }

    /// <summary>
    /// The authority for a discount the SYSTEM applied — a member's tier rate, not an operator's
    /// decision.
    ///
    /// ⚠ It still gets a record, and that is deliberate. "All discounts" includes the automatic ones,
    /// and a member discount is the one most likely to be queried later ("why is this basket 10%
    /// under?"). ⚠ There is no authoriser because nobody authorised it — the tier did, and the tier
    /// is portal-configured. Passing the operator as the authoriser here would manufacture a
    /// self-approval that never happened.
    /// </summary>
    public static DiscountAuthority Automatic(string reason, long amountPence, Guid requestedByUserId) =>
        new(NormaliseReason(reason) ?? "Automatic discount",
            amountPence < 0 ? -amountPence : amountPence,
            requestedByUserId,
            null,
            null);
}
