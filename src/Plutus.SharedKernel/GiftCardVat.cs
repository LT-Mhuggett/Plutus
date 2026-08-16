using System;

namespace Plutus.SharedKernel;

/// <summary>
/// The tenant's declared HMRC voucher treatment — WHEN a gift card's VAT falls due.
///
/// ⚠ THERE IS NO DEFAULT, AND THAT IS THE POINT. The server 409s every gift-card operation while
/// `GiftCardSettings` is absent rather than assuming one, because the two answers put VAT in
/// different periods and guessing writes a wrong figure onto a VAT return that nobody chose.
/// </summary>
public enum VoucherTreatment : byte
{
    /// <summary>Nobody has chosen. ⚠ A till must refuse to sell a card, not pick one.</summary>
    NotChosen = 0,

    /// <summary>
    /// **Multi-purpose** — the tenant sells at mixed VAT rates, so what the card will buy is unknown
    /// when it is sold. **No VAT at the sale of the card**; VAT comes off the goods when it is SPENT.
    /// </summary>
    Multi = 1,

    /// <summary>
    /// **Single-purpose** — everything the card can buy carries one rate, so the VAT is knowable at
    /// the sale. **VAT is charged when the card is SOLD**, and the later redemption reduces the
    /// sale's VAT-able total rather than acting as a plain tender.
    /// </summary>
    Single = 2,
}

/// <summary>
/// What price pair a gift-card ACTIVATION line carries, and under which band.
///
/// ⚠⚠ IT IS SHARED BEFORE THE SECOND TILL NEEDS IT, not after. The rule existed only inside the web
/// till's `api.ts` checkout (`vatRateBp:` / `vatBand:` ternaries on `l.giftCardCode`), and MAUI was
/// about to become a second implementation of a VAT decision. CLAUDE.md's C2 rule says a money rule
/// gets one home; this is that home, and the same move `MemberDiscount` made in step 27.
///
/// ⚠ A GIFT CARD IS A LIABILITY, NOT A SUPPLY — that is the whole reason this is not ordinary VAT
/// arithmetic. Selling one takes money for goods that have not been chosen yet. Under
/// <see cref="VoucherTreatment.Multi"/> nothing has been supplied, so nothing is VAT-able yet, and
/// the line declares **zero** — the money becomes VAT-able when the card is spent on real goods.
/// Getting that backwards declares output tax twice: once on the card, once on the goods.
///
/// ⚠ SEE ALSO <see cref="GiftCards"/> for what identifies the activation line, and the loyalty
/// design's decision 1 for why a loyalty *gem* — never purchased — is a discount rather than
/// this. The two must never share a code path: one is bought and is a liability, the other is not.
/// </summary>
public static class GiftCardVat
{
    /// <summary>
    /// Read the wire's treatment string. ⚠ Anything unrecognised — including null — is
    /// <see cref="VoucherTreatment.NotChosen"/>, never a guess: an unknown value means this build
    /// does not understand what the tenant chose, which is exactly when it must not sell a card.
    /// </summary>
    public static VoucherTreatment FromWireName(string? treatment) =>
        (treatment?.Trim().ToLowerInvariant()) switch
        {
            "multi" => VoucherTreatment.Multi,
            "single" => VoucherTreatment.Single,
            _ => VoucherTreatment.NotChosen,
        };

    /// <summary>
    /// The inc/ex price pair for loading a card with <paramref name="loadedPence"/>.
    ///
    /// ⚠ THE CUSTOMER ALWAYS PAYS THE FACE VALUE. The treatment decides how much of that is VAT, not
    /// how much they hand over — a £20 card costs £20 either way.
    ///
    /// ⚠ <b>Multi:</b> ex == inc, so <see cref="VatLineMath.ForLine"/> derives 0bp. The pair IS the
    /// declaration; there is no separate "zero-rate" flag to forget to set.
    ///
    /// ⚠ <b>Single:</b> the VAT is inside the face value, so ex = inc ÷ (1 + rate). ⚠ MULTIPLY
    /// BEFORE DIVIDING and round once, for the reason `CardSurchargeVat.PairFor` documents: putting
    /// a non-terminating decimal in the middle drags true midpoints the wrong way.
    /// </summary>
    /// <param name="standardRateBp">The PUBLISHED standard rate in basis points — from
    /// `GET /api/v1/vat/bands`, never a literal 2000. Ignored under Multi.</param>
    /// <exception cref="InvalidOperationException">The treatment has not been chosen. ⚠ Throwing is
    /// correct: there is no sensible pair to return, and returning one would be the guess this class
    /// exists to prevent.</exception>
    public static (long IncPence, long ExPence) PairFor(
        long loadedPence, VoucherTreatment treatment, int standardRateBp)
    {
        if (loadedPence <= 0)
            throw new ArgumentOutOfRangeException(nameof(loadedPence),
                "A gift card is loaded with a positive amount. Zero or less is not a card anybody sold.");

        switch (treatment)
        {
            case VoucherTreatment.Multi:
                // ⚠ No VAT at the sale — the card is stored value, not a supply.
                return (loadedPence, loadedPence);

            case VoucherTreatment.Single:
                if (standardRateBp < 0)
                    throw new ArgumentOutOfRangeException(nameof(standardRateBp),
                        "A negative VAT rate is not a rate.");

                // ex = inc × 10000 ÷ (10000 + rateBp), one rounding, products first.
                var ex = (long)Math.Round(
                    loadedPence * 10000m / (10000m + standardRateBp), MidpointRounding.AwayFromZero);

                return (loadedPence, ex);

            default:
                throw new InvalidOperationException(
                    "This tenant has not chosen a gift-card VAT treatment, so a card cannot be sold. "
                    + "The treatment decides whether VAT falls due now or when the card is spent, and "
                    + "guessing it puts a number on a VAT return that nobody chose. Set it in the portal.");
        }
    }

    /// <summary>
    /// Which published band an activation line was rung up under.
    ///
    /// ⚠⚠ UNDER `Single` IT IS **STATED**, NOT DERIVED FROM THE PAIR, and the web till's comment
    /// explains why: round-tripping pence through the generic ratio wobbles the implied rate to
    /// 1998–2002bp and scatters one tenant's card sales across several bands on the VAT report. The
    /// treatment says it IS the standard rate, so the line says so.
    ///
    /// ⚠ Under `Multi` the answer is null — "the portal has not decided which band this is" — which
    /// is a legitimate answer the server understands, and NOT a guess. A zero-VAT liability is not
    /// the same thing as a zero-rated supply, and this must never claim to be one.
    /// </summary>
    public static string? BandKeyFor(VoucherTreatment treatment) =>
        treatment == VoucherTreatment.Single ? VatRateHistory.Standard : null;

    /// <summary>Can a card be sold at all right now?</summary>
    public static bool CanSell(VoucherTreatment treatment) => treatment != VoucherTreatment.NotChosen;
}
