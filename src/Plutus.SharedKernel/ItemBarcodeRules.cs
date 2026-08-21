using System;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>
/// What may be an item's ADDITIONAL barcode — the multi-barcode rule.
///
/// ⚠⚠ THIS REVERSES A RECORDED RULING, DELIBERATELY. `TillStore.FindByBarcodeAsync` and
/// `LocalSchema` recorded that a local alias table existed, was never written by anything, and was
/// deleted on 2026-08-09 — *"Matt confirmed the same day that multi-barcode items are not needed."*
/// That note also said what real support would require: *"a server entity, a feed field and a
/// portal UI first — a platform decision, not a till change."* Matt asked for it on 2026-08-20;
/// `Build/archive/Multi-barcode plan.md` is that decision and this class is its first package.
///
/// ⚠⚠ AN ALIAS IS NOT AN IDENTITY. <c>Item.IdOne</c> remains the item's identity for ever — it
/// seeds <see cref="DeterministicGuid.ForItem"/> (whose output is frozen by a golden vector and
/// twinned in TypeScript), it is half the composite primary key, it carries five foreign-key
/// families, and it is on every historical sale line. Aliases are additive rows that RESOLVE to an
/// item; nothing is ever re-keyed. Plan decision D1.
///
/// ⚠⚠ AND THE ALIAS STRING MUST NEVER TRAVEL PAST RESOLUTION (plan decision D2). Whoever resolves
/// a scan hands on the CANONICAL <c>IdOne</c>. Two silent faults punish a leak: an unknown id makes
/// `StockProjectionConsumer` create a phantom `StockLevel` with no error, and makes `VatBandStamp`
/// give up and leave the line's VAT band null. Neither surfaces anywhere a person would look.
/// </summary>
public static class ItemBarcodeRules
{
    /// <summary>
    /// The longest barcode there can be.
    ///
    /// ⚠ It is <c>Item.IdOne</c>'s own <c>[MaxLength(20)]</c>, not a number chosen here. An alias
    /// longer than an identity could never be promoted to one, and the column it lands in is the
    /// same width.
    /// </summary>
    public const int MaxLength = 20;

    /// <summary>
    /// The alias as it will be stored — trimmed, with "nothing" collapsed to null.
    ///
    /// ⚠ TRIM ONLY. NO CASE FOLDING, and that is a decision rather than an omission (plan D6).
    /// Today's <c>IdOne</c> matching is already inconsistent across the estate — MySQL's collation
    /// on the server, a case-SENSITIVE <c>==</c> on SQLite at the MAUI till, a case-sensitive
    /// IndexedDB key on the web till — and it has never bitten, because scanners emit exact
    /// strings. Aliases inherit exactly that behaviour. Normalising here would make an alias match
    /// where the item's OWN barcode would not, which is a new inconsistency dressed as a fix.
    /// </summary>
    public static string? Normalise(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    /// <summary>
    /// Why this string may not be an alias — or null when it may.
    ///
    /// ⚠⚠ EVERY REFUSAL IS A SENTENCE A PERSON CAN ACT ON, and the tests assert them verbatim. The
    /// portal shows what the server says, so a reworded sentence here is a reworded sentence in
    /// front of whoever is trying to set a barcode up. The opening-hours work is the precedent: the
    /// readers are tolerant, the WRITER is strict, and it says what is wrong rather than that
    /// something is.
    ///
    /// ⚠⚠ THE RESERVED SHAPES ARE REFUSED BECAUSE THEY COULD NEVER SCAN ANYWAY. Member-card and
    /// gift-card routing runs BEFORE any item lookup on both tills — the web till tests its two
    /// regexes first, and MAUI's `TryRouteMemberScanAsync` is terminal for a gift card. So an alias
    /// of either shape would be a row that looks configured, sits in the portal, and can never
    /// fire. That is worse than a refusal: it looks like the feature is broken.
    ///
    /// ⚠ Carrier bags, <c>GIFT-CARD</c> and <c>CARD-SURCHARGE</c> are refused because they are
    /// identities with their own money rules — a bag's price IS its id, and the other two decide
    /// VAT treatment. Pointing one of them at an ordinary item would put a second meaning on a
    /// string the whole platform reads one way.
    ///
    /// ⚠ Callers must <see cref="Normalise"/> first; this judges the stored form.
    /// </summary>
    public static string? WhyRefused(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Enter a barcode.";

        if (code.Length > MaxLength)
            return $"That is longer than a barcode can be — {MaxLength} characters at most.";

        // ⚠ Interior whitespace, not leading/trailing: `Normalise` has already trimmed those. A
        // barcode with a space in it cannot be scanned back, and would only ever have been typed.
        if (code.Any(char.IsWhiteSpace))
            return "A barcode cannot contain spaces.";

        if (MemberNumbers.LooksLikeMemberScan(code))
            return "That is the shape of a membership card. Cards are matched before items are, "
                 + "so it could never scan as this item.";

        if (GiftCardCodes.LooksLikeCard(code))
            return "That is the shape of a gift card. Cards are matched before items are, so it "
                 + "could never scan as this item.";

        if (CarrierBags.IsBagId(code))
            return "That is a carrier-bag id. Bags are set up on the Company page, not as barcodes.";

        if (string.Equals(code, GiftCards.ItemIdOne, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, CardSurchargeVat.ItemIdOne, StringComparison.OrdinalIgnoreCase))
            return "That id belongs to the platform and cannot be used as a barcode.";

        return null;
    }

    /// <summary>May this string be an alias? The boolean form of <see cref="WhyRefused"/>.</summary>
    public static bool IsAcceptable(string? code) => WhyRefused(code) is null;
}
