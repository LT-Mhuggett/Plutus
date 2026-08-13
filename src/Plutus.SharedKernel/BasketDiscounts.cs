using System;

namespace Plutus.SharedKernel;

/// <summary>Why a discount was refused. The caller turns this into a sentence — see
/// <see cref="DiscountDecision"/> for why the sentence is not built here.</summary>
public enum DiscountVerdict : byte
{
    Allowed = 0,
    /// <summary>More than the basket has left. <see cref="DiscountDecision.HeadroomPence"/> is the
    /// most that could be taken off, and <see cref="DiscountDecision.AlreadyPence"/> says how much
    /// is already off — an operator shown a maximum below the basket total and not told why will
    /// reasonably conclude the till is wrong.</summary>
    ExceedsBasket = 1,
    /// <summary>Nothing here can be discounted — an empty basket, or one holding only returns. ⚠ Its
    /// own verdict rather than a zero headroom, because "£0.00 is the most you can take off" is true
    /// and useless: a refund gives back what the customer actually paid, and that is the sentence
    /// the operator needs.</summary>
    NothingToDiscount = 2,
    /// <summary>Zero or negative. A discount that discounts nothing is a record that means nothing.</summary>
    NotAnAmount = 3,
}

/// <summary>
/// A discount decision.
///
/// ⚠ Carries NO money in a string, deliberately and for the same reasons
/// <see cref="RefundDecision"/> does not: `Money.cs` makes formatting a client concern, "£" is wrong
/// the first time a tenant trades in another currency, and the MAUI till runs its strings through
/// `I18N_L10N`. The amounts are fields; the caller composes the sentence.
/// </summary>
/// <param name="HeadroomPence">The most that could be discounted right now — 0 when none.</param>
/// <param name="AlreadyPence">What is already off the basket, so the caller can explain a headroom
/// smaller than the basket total.</param>
public readonly record struct DiscountDecision(
    DiscountVerdict Verdict, long HeadroomPence, long AlreadyPence)
{
    public bool IsAllowed => Verdict == DiscountVerdict.Allowed;
}

/// <summary>
/// Whether a discount may be applied, and how much room is left.
///
/// ⚠⚠ MATT, 2026-08-13: *"You cannot have a discount greater than the basket."* Binding default 22.
/// Deliberately the same shape as <see cref="RefundRules"/> — which answers *"how much of this sale
/// is still refundable"* — because it is the same kind of question and carries the same trap: a
/// ceiling checked in one place and not another is a ceiling that does not exist.
///
/// ⚠ IT EXISTS BECAUSE ITS ABSENCE MADE SALES UN-COMPLETABLE. `CheckoutCommit.ApplyAlterations`
/// apportions each alteration against the gross **remaining after earlier discounts**, and
/// <see cref="DiscountApportionment.Across"/> rightly throws above that — the alternative is a
/// negative-gross "sale". Nothing capped the total, so £5 off plus £5 off an £8 basket threw and
/// `CommitAsync` reported *"Nothing has been taken — try again"*, which never worked: the basket
/// could not be completed and nothing said a discount was the cause.
///
/// ⚠ THE REFUSAL BELONGS WHERE THE DISCOUNT IS APPLIED, NOT AT COMMIT. Capping silently at commit
/// would turn a £5 discount into £3 with nobody told — the silent money change this codebase exists
/// to prevent. Checkout keeps its own guard as a backstop; this is the gate the operator meets.
///
/// ⚠ RETURNS ARE NOT DISCOUNTABLE ROOM. Headroom counts SALE lines only: £10 of goods alongside a
/// £30 refund has £10 of headroom, not −£20 and not £40. <see cref="VatLineMath.ForLine"/> drops a
/// discount on a return by design, so money apportioned onto one vanishes and the sale then fails
/// the server's reconcile invariant.
/// </summary>
public static class BasketDiscounts
{
    /// <summary>
    /// How much discount this basket can still take.
    ///
    /// ⚠ NET OF WHAT IS ALREADY OFF, which is the point — two discounts are judged together, not
    /// each against the undiscounted basket. That is exactly what `ApplyAlterations` does at commit,
    /// so judging it any other way here would pass a basket through this gate and throw at the next.
    /// </summary>
    /// <param name="saleLinesGrossPence">Σ (unit inc-VAT × qty) over SALE lines only — returns excluded.</param>
    /// <param name="discountAlreadyPence">Σ discount already applied to those lines.</param>
    public static long HeadroomPence(long saleLinesGrossPence, long discountAlreadyPence)
    {
        if (saleLinesGrossPence <= 0) return 0;
        var headroom = saleLinesGrossPence - discountAlreadyPence;
        return headroom > 0 ? headroom : 0;
    }

    /// <summary>
    /// May <paramref name="requestedPence"/> come off this basket?
    ///
    /// ⚠ THE BOUNDARY IS INCLUSIVE — a discount equal to the basket is allowed, so "everything free"
    /// works and only *more* than everything is refused. A legitimate 100% staff discount must not be
    /// caught by an off-by-one, which is why that boundary is pinned by its own test.
    /// </summary>
    public static DiscountDecision Authorise(
        long requestedPence, long saleLinesGrossPence, long discountAlreadyPence)
    {
        var already = discountAlreadyPence > 0 ? discountAlreadyPence : 0;
        var headroom = HeadroomPence(saleLinesGrossPence, already);

        if (saleLinesGrossPence <= 0)
            return new DiscountDecision(DiscountVerdict.NothingToDiscount, 0, already);

        if (requestedPence <= 0)
            return new DiscountDecision(DiscountVerdict.NotAnAmount, headroom, already);

        if (requestedPence > headroom)
            return new DiscountDecision(DiscountVerdict.ExceedsBasket, headroom, already);

        return new DiscountDecision(DiscountVerdict.Allowed, headroom, already);
    }
}
