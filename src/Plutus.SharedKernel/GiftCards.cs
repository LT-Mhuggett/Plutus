namespace Plutus.SharedKernel;

/// <summary>
/// What identifies a gift-card ACTIVATION line — the line that sells stored value.
///
/// ⚠⚠ IT IS HERE BECAUSE IT WAS ABOUT TO BECOME A THIRD COPY. The string already exists twice: as a
/// private const in `Plutus.Sales/SalesIngestService.cs` (which uses it to give an activation line
/// its zero-VAT treatment) and implicitly in the web till, which marks the line with a
/// `giftCardCode` instead. MAUI needs to recognise one too — <see cref="MemberDiscount"/> excludes
/// gift cards from the members' discount — and hardcoding the literal a third time would put a
/// string that decides a VAT treatment in three places with nothing pinning them together.
///
/// ⚠ THE EXCLUSIONS IT DRIVES ARE NOT COSMETIC. A gift card is a **liability, not a supply**:
///   • its activation posts **ZERO VAT**, because VAT falls due when the card is spent, not sold;
///   • it takes **no members' discount** — selling £50 of spendable money for £45 hands over £50 of
///     purchasing power for £45, and that £50 is then spent again on already-discounted goods.
/// Get the identification wrong and both of those silently stop applying, on a line that looks
/// completely ordinary.
///
/// ⚠ C2: the server still holds its own private copy. They agree today and
/// `GiftCardItemIdOne_matches_the_shared_constant` is what keeps them agreeing; the duplicate goes
/// when WP13 brings gift cards to MAUI and the server can reference this directly.
/// </summary>
public static class GiftCards
{
    /// <summary>The provisioned catalogue row every gift-card activation is sold against.</summary>
    public const string ItemIdOne = "GIFT-CARD";

    /// <summary>
    /// Is this catalogue id the gift-card activation row?
    ///
    /// ⚠ Case-insensitive, matching the server's own comparison (`SalesIngestService:560` uses
    /// `OrdinalIgnoreCase`). A till that compared case-sensitively would sell a gift card with VAT
    /// on it the first time the catalogue row came back lower-cased.
    /// </summary>
    public static bool IsActivation(string? itemIdOne) =>
        !string.IsNullOrWhiteSpace(itemIdOne) &&
        string.Equals(itemIdOne, ItemIdOne, System.StringComparison.OrdinalIgnoreCase);
}
