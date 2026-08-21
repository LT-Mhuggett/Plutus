using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// What may be an item's additional barcode (multi-barcode plan, MB1).
///
/// ⚠⚠ THE SENTENCES ARE ASSERTED VERBATIM, ON PURPOSE. The portal shows whatever the server says,
/// so these strings are the user interface — a reworded sentence is a reworded screen, and it
/// should have to be a deliberate edit with a test to change.
///
/// ⚠ The reserved-shape cases are the ones that matter. Member-card and gift-card routing runs
/// BEFORE item lookup on both tills, so an alias of either shape would be a row that looks
/// configured and can never fire — which reads as "the feature is broken" rather than as a
/// mistake somebody made.
/// </summary>
public class ItemBarcodeRuleTests
{
    // ── normalising ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("5011921068203", "5011921068203")]
    [InlineData("  5011921068203  ", "5011921068203")]
    public void Normalise_trims_and_collapses_nothing_to_null(string? raw, string? expected)
    {
        Assert.Equal(expected, ItemBarcodeRules.Normalise(raw));
    }

    /// <summary>
    /// ⚠⚠ NO CASE FOLDING — plan decision D6. `IdOne` matching is already case-sensitive on both
    /// tills' local stores, so folding here would make an alias match where the item's OWN barcode
    /// would not. Pinned because "helpfully" upper-casing is the obvious wrong improvement.
    /// </summary>
    [Fact]
    public void Normalise_does_NOT_change_case()
    {
        Assert.Equal("abc123", ItemBarcodeRules.Normalise("  abc123  "));
        Assert.Equal("ABC123", ItemBarcodeRules.Normalise("ABC123"));
    }

    // ── what is acceptable ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("5011921068203")]            // an ordinary EAN-13
    [InlineData("045496452603")]             // UPC-A
    [InlineData("12345678901234567890")]     // exactly 20 — the ceiling is inclusive
    [InlineData("ABC-123")]                  // a supplier code
    [InlineData("BAGGY-10")]                 // ⚠ NOT a bag: the prefix must be exact
    [InlineData("GIFT-CARDS")]               // ⚠ NOT the platform row: equality, not prefix
    [InlineData("C123456")]                  // member-shaped but too SHORT to be a card
    public void An_ordinary_barcode_is_acceptable(string code)
    {
        Assert.Null(ItemBarcodeRules.WhyRefused(code));
        Assert.True(ItemBarcodeRules.IsAcceptable(code));
    }

    // ── the refusals, each with its sentence ───────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_typed_is_refused(string? code)
    {
        Assert.Equal("Enter a barcode.", ItemBarcodeRules.WhyRefused(code));
    }

    /// <summary>⚠ 20 is `Item.IdOne`'s own `[MaxLength(20)]`, not a number chosen here.</summary>
    [Fact]
    public void A_barcode_longer_than_the_column_is_refused()
    {
        Assert.Equal(
            "That is longer than a barcode can be — 20 characters at most.",
            ItemBarcodeRules.WhyRefused(new string('9', 21)));
    }

    [Fact]
    public void A_barcode_with_an_interior_space_is_refused()
    {
        Assert.Equal("A barcode cannot contain spaces.", ItemBarcodeRules.WhyRefused("501192 068203"));
    }

    /// <summary>
    /// ⚠⚠ THE ONE THAT JUSTIFIES THIS CLASS. `C000482P` is a real member card (six digits plus a
    /// check character). Both tills route a member scan BEFORE any item lookup, so this alias could
    /// never fire — and a row that cannot fire looks like a broken feature rather than a mistake.
    /// </summary>
    [Fact]
    public void A_member_card_shape_is_refused_with_the_reason()
    {
        var code = MemberNumbers.BarcodePayload(MemberNumbers.Format(482));

        Assert.True(MemberNumbers.LooksLikeMemberScan(code));   // the premise, stated
        Assert.Equal(
            "That is the shape of a membership card. Cards are matched before items are, so it "
            + "could never scan as this item.",
            ItemBarcodeRules.WhyRefused(code));
    }

    /// <summary>⚠ Same reasoning as the member card — MAUI's gift-card routing is TERMINAL, so such
    /// an alias could not even fall through to an item lookup on that till.</summary>
    [Fact]
    public void A_gift_card_shape_is_refused_with_the_reason()
    {
        var code = GiftCardCodes.BarcodePayload(GiftCardCodes.New());

        Assert.True(GiftCardCodes.LooksLikeCard(code));         // the premise, stated
        Assert.Equal(
            "That is the shape of a gift card. Cards are matched before items are, so it "
            + "could never scan as this item.",
            ItemBarcodeRules.WhyRefused(code));
    }

    /// <summary>⚠ A bag's PRICE IS ITS IDENTITY (`BAG-<pence>`), so pointing a bag id at an
    /// ordinary item would put two meanings on one string.</summary>
    [Fact]
    public void A_carrier_bag_id_is_refused_with_the_reason()
    {
        Assert.Equal(
            "That is a carrier-bag id. Bags are set up on the Company page, not as barcodes.",
            ItemBarcodeRules.WhyRefused(CarrierBags.IdFor(10)));
    }

    /// <summary>⚠ These two decide VAT treatment — a gift-card activation posts zero VAT and the
    /// card fee carries the basket's blended rate. Aliasing either is not a naming clash, it is a
    /// second money rule on one string. ⚠ Case-insensitive, matching how the platform compares them.</summary>
    [Theory]
    [InlineData("GIFT-CARD")]
    [InlineData("gift-card")]
    [InlineData("CARD-SURCHARGE")]
    [InlineData("Card-Surcharge")]
    public void A_platform_id_is_refused_with_the_reason(string code)
    {
        Assert.Equal(
            "That id belongs to the platform and cannot be used as a barcode.",
            ItemBarcodeRules.WhyRefused(code));
    }
}
