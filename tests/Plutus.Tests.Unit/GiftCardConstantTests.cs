using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The gift-card activation id — a string that decides a VAT treatment.
///
/// ⚠⚠ IT EXISTS IN THREE PLACES and only this one is shared: `SharedKernel.GiftCards.ItemIdOne`,
/// the server's private `SalesIngestService.GiftCardItemIdOne`, and the web till's `giftCardCode`
/// marker. C2 records that honestly. These tests pin the shared value so the other two have
/// something to be compared against — and so a rename cannot happen quietly.
///
/// ⚠ What rides on getting it right: an activation line posts **ZERO VAT** (VAT falls due when the
/// card is SPENT, not when it is sold) and takes **no members' discount** (stored value is a
/// liability, not a supply). Both stop applying silently if the line is not recognised, on a line
/// that looks completely ordinary next to the goods.
/// </summary>
public class GiftCardConstantTests
{
    /// <summary>⚠ The literal is asserted deliberately. This is not a tautology: the point is that
    /// changing the constant must break a test that names the server's copy, because the server
    /// cannot see this file.</summary>
    [Fact]
    public void The_shared_activation_id_is_the_value_the_server_and_the_provisioned_row_use()
    {
        Assert.Equal("GIFT-CARD", GiftCards.ItemIdOne);
    }

    /// <summary>
    /// ⚠ CASE-INSENSITIVE, matching the server's own comparison (`SalesIngestService:560` uses
    /// `OrdinalIgnoreCase`). A till comparing case-sensitively would sell a gift card **with VAT on
    /// it** the first time a catalogue row came back lower-cased — and nothing would look wrong.
    /// </summary>
    [Theory]
    [InlineData("GIFT-CARD")]
    [InlineData("gift-card")]
    [InlineData("Gift-Card")]
    public void An_activation_line_is_recognised_whatever_its_casing(string idOne)
    {
        Assert.True(GiftCards.IsActivation(idOne));
    }

    /// <summary>⚠ And nothing else is. A near-miss must NOT be treated as stored value — the
    /// exclusions are strong ones, and applying them to ordinary goods would zero-rate a taxable
    /// supply.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GIFTCARD")]
    [InlineData("GIFT-CARDS")]
    [InlineData("CARD-SURCHARGE")]
    public void Anything_else_is_ordinary_goods(string? idOne)
    {
        Assert.False(GiftCards.IsActivation(idOne));
    }
}
