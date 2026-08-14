using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Binding default 22(c) — *"All discounts need to be tracked — till, logged-in employee and
/// reason."*
///
/// ⚠ THE RULE IS SHARED SO IT CANNOT BE ENFORCED ON ONE TILL ONLY. That is the entire reason it is
/// in `SharedKernel` rather than in `TillViewModel`: a mandatory reason that MAUI demands and the
/// web till does not is a reporting column that is populated on some counters and blank on others,
/// which is worse than not having it — a query over it looks answered and is not.
/// </summary>
public class DiscountAuditTests
{
    private static readonly Guid Cashier = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Supervisor = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── The reason is mandatory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ THE RULING, PINNED. An optional reason is an empty column, and the one discount anybody
    /// ever asks about is the one where nobody typed anything.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n ")]
    public void A_discount_with_no_reason_cannot_be_recorded(string? reason)
    {
        var (verdict, _) = DiscountAudit.Authorise(
            reason, 500, Cashier, stepUpWasRequired: false, null, null);

        Assert.Equal(DiscountAuditVerdict.NoReason, verdict);
    }

    /// <summary>
    /// ⚠ WHITESPACE IS THE ONE THAT MATTERS. `"   "` is present to a SQL query and blank to a human,
    /// so a rule that only checked for null would have produced exactly the empty column the ruling
    /// exists to prevent — while reporting 100% coverage.
    /// </summary>
    [Fact]
    public void A_reason_of_only_whitespace_is_no_reason_at_all()
    {
        Assert.Null(DiscountAudit.NormaliseReason("     "));
        Assert.Null(DiscountAudit.NormaliseReason("\t"));
    }

    [Fact]
    public void A_reason_is_trimmed_so_the_stored_value_is_the_words_and_nothing_else()
    {
        Assert.Equal("damaged box", DiscountAudit.NormaliseReason("  damaged box  "));
    }

    /// <summary>
    /// ⚠ So "damaged    box" and "damaged box" GROUP TOGETHER in a report instead of reading as two
    /// distinct reasons. Without this the most common reason in a shop fragments across every
    /// spacing an operator happens to type.
    /// </summary>
    [Fact]
    public void Interior_whitespace_is_collapsed_so_two_spellings_of_one_reason_group_together()
    {
        Assert.Equal("damaged box", DiscountAudit.NormaliseReason("damaged    box"));
        Assert.Equal("damaged box", DiscountAudit.NormaliseReason("damaged\tbox"));
        Assert.Equal("staff discount for Jo", DiscountAudit.NormaliseReason("staff  discount\nfor   Jo"));
    }

    /// <summary>
    /// ⚠ TRUNCATED, NEVER REFUSED. Refusing at a counter because somebody pasted too much would lose
    /// the whole discount over something nobody can fix quickly — and the cap exists only because
    /// the reason is repeated on every line a basket-wide discount apportions onto.
    /// </summary>
    [Fact]
    public void An_overlong_reason_is_truncated_rather_than_refused()
    {
        var long_reason = new string('x', DiscountAudit.MaxReasonLength + 50);

        var normalised = DiscountAudit.NormaliseReason(long_reason);

        Assert.NotNull(normalised);
        Assert.Equal(DiscountAudit.MaxReasonLength, normalised!.Length);

        var (verdict, _) = DiscountAudit.Authorise(
            long_reason, 500, Cashier, stepUpWasRequired: false, null, null);
        Assert.Equal(DiscountAuditVerdict.Recordable, verdict);
    }

    /// <summary>⚠ The authority carries the NORMALISED reason, not the raw one. A caller that
    /// validated the trimmed string and stored the raw one would defeat the grouping above.</summary>
    [Fact]
    public void The_authority_carries_the_normalised_reason_not_what_was_typed()
    {
        var (_, authority) = DiscountAudit.Authorise(
            "  damaged   box  ", 500, Cashier, stepUpWasRequired: false, null, null);

        Assert.Equal("damaged box", authority.Reason);
    }

    // ── Who authorised it ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ NULL IS A REAL ANSWER, NOT MISSING DATA. A discount inside the operator's own ceiling was
    /// authorised by nobody because nobody needed to — and that IS the audit answer to "who approved
    /// this?". Recording the operator as their own authoriser would manufacture a self-approval that
    /// never happened.
    /// </summary>
    [Fact]
    public void A_discount_within_the_operators_own_ceiling_needs_no_authoriser()
    {
        var (verdict, authority) = DiscountAudit.Authorise(
            "damaged box", 300, Cashier, stepUpWasRequired: false, null, null);

        Assert.Equal(DiscountAuditVerdict.Recordable, verdict);
        Assert.Null(authority.AuthorisedByUserId);
        Assert.Equal(Cashier, authority.RequestedByUserId);
    }

    [Fact]
    public void A_stepped_up_discount_records_both_people()
    {
        var (verdict, authority) = DiscountAudit.Authorise(
            "manager approved", 5000, Cashier,
            stepUpWasRequired: true, Supervisor, "Sam Supervisor");

        Assert.Equal(DiscountAuditVerdict.Recordable, verdict);
        Assert.Equal(Cashier, authority.RequestedByUserId);
        Assert.Equal(Supervisor, authority.AuthorisedByUserId);
        Assert.Equal("Sam Supervisor", authority.AuthorisedByName);
    }

    /// <summary>
    /// ⚠⚠ THE CASE THE `stepUpWasRequired` PARAMETER EXISTS FOR. If the rule inferred "a step-up
    /// happened" from an authoriser being PRESENT, then a step-up that silently failed to record one
    /// would read as "no step-up was needed" — indistinguishable from a £2 discount by a cashier who
    /// was entitled to give it. That is the exact shape of the finding this whole slice came from:
    /// the authoriser was verified and then thrown away, and nothing anywhere noticed.
    /// </summary>
    [Fact]
    public void A_step_up_that_recorded_no_authoriser_is_refused_rather_than_read_as_no_step_up()
    {
        var (verdict, _) = DiscountAudit.Authorise(
            "big discount", 5000, Cashier,
            stepUpWasRequired: true, authorisedByUserId: null, authorisedByName: null);

        Assert.Equal(DiscountAuditVerdict.NoAuthoriser, verdict);
    }

    /// <summary>
    /// ⚠⚠ THE SECOND GUARD. `OperatorLogin.AuthoriseOverrideAsync` already refuses self-approval when
    /// the credentials are typed; this refuses it again when the RECORD is built. Two guards on one
    /// rule is right here — the first protects the act, this protects the record — and a trail
    /// stating that a cashier approved their own £50 discount is worse than no trail: it is an audit
    /// answer that is confidently wrong.
    ///
    /// ⚠ Do not delete either on the grounds that the other exists.
    /// </summary>
    [Fact]
    public void Nobody_can_authorise_their_own_discount_even_if_the_login_layer_let_them()
    {
        var (verdict, _) = DiscountAudit.Authorise(
            "I approve of me", 5000, Cashier,
            stepUpWasRequired: true, authorisedByUserId: Cashier, authorisedByName: "Same Person");

        Assert.Equal(DiscountAuditVerdict.SelfAuthorised, verdict);
    }

    /// <summary>
    /// ⚠ AN UNSET ID IS NOT A PERSON. Without this, a till that failed to resolve either user would
    /// compare `Guid.Empty` against `Guid.Empty` and report self-approval — turning "we don't know
    /// who they were" into a specific accusation.
    /// </summary>
    [Fact]
    public void An_empty_guid_is_not_treated_as_a_person_authorising_themselves()
    {
        var (verdict, _) = DiscountAudit.Authorise(
            "reason", 500, Guid.Empty,
            stepUpWasRequired: false, authorisedByUserId: Guid.Empty, authorisedByName: null);

        Assert.NotEqual(DiscountAuditVerdict.SelfAuthorised, verdict);
    }

    // ── The money ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ MAGNITUDE. A till holds a discount as a NEGATIVE price (`BasketAlteration.Price`), and an
    /// audit report summing signed values would net two discounts against each other. Every amount
    /// that reaches the record is positive, the same convention `RefundRules` uses.
    /// </summary>
    [Theory]
    [InlineData(-500L, 500L)]
    [InlineData(500L, 500L)]
    [InlineData(0L, 0L)]
    public void The_recorded_amount_is_always_positive(long given, long expected)
    {
        var (_, authority) = DiscountAudit.Authorise(
            "reason", given, Cashier, stepUpWasRequired: false, null, null);

        Assert.Equal(expected, authority.AmountPence);
    }

    // ── Automatic discounts ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ "ALL DISCOUNTS" INCLUDES THE ONES NOBODY CHOSE. A member's tier discount is the one most
    /// likely to be queried later — *"why is this basket 10% under?"* — so it gets a record too.
    /// ⚠ With NO authoriser: the tier authorised it, and the tier is portal-configured. Naming the
    /// operator there would invent a self-approval.
    /// </summary>
    [Fact]
    public void An_automatic_discount_is_recorded_with_a_reason_and_no_authoriser()
    {
        var authority = DiscountAudit.Automatic("Gold member 10%", 250, Cashier);

        Assert.Equal("Gold member 10%", authority.Reason);
        Assert.Equal(250, authority.AmountPence);
        Assert.Equal(Cashier, authority.RequestedByUserId);
        Assert.Null(authority.AuthorisedByUserId);
        Assert.Null(authority.AuthorisedByName);
    }

    /// <summary>⚠ An automatic discount can never end up blank either — a caller passing an empty
    /// label gets a stated fallback rather than a record that says nothing.</summary>
    [Fact]
    public void An_automatic_discount_with_no_label_still_says_something()
    {
        var authority = DiscountAudit.Automatic("   ", 250, Cashier);

        Assert.False(string.IsNullOrWhiteSpace(authority.Reason));
    }
}
