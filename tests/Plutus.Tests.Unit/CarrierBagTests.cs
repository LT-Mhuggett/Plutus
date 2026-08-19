using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Carrier bags — the shop defines them in the portal and every till gets them (2026-08-19).
///
/// ⚠ The two rules worth pinning are the ones that would cost money if wrong: the id encodes the price
/// (so a bag cannot sell at a price its own key contradicts), and the VAT band is STANDARD, not the
/// zero band the gift-card item deliberately uses.
/// </summary>
public class CarrierBagTests
{
    [Theory]
    [InlineData(10, "BAG-10")]
    [InlineData(20, "BAG-20")]
    [InlineData(5, "BAG-5")]
    [InlineData(25, "BAG-25")]
    public void The_id_encodes_the_price_in_pence(long pence, string expected)
    {
        Assert.Equal(expected, CarrierBags.IdFor(pence));
        Assert.Equal(pence, CarrierBags.PriceFromId(expected));
    }

    /// <summary>
    /// ⚠ PRICE IS THE IDENTITY, which is what stops a shop accumulating three 10p bag rows that all
    /// look the same on a receipt and split one line across three report rows.
    /// </summary>
    [Fact]
    public void Two_bags_at_the_same_price_are_the_same_bag()
    {
        Assert.Equal(CarrierBags.IdFor(10), CarrierBags.IdFor(10));
    }

    /// <summary>⚠ A free bag is not a line on a receipt — and `BAG-0` would parse as a bag for ever.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_bag_must_have_a_price(long pence) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CarrierBags.IdFor(pence));

    [Theory]
    [InlineData("BAG-10", true)]
    [InlineData("BAG-0", false)]         // ⚠ zero is not a price
    [InlineData("BAG--5", false)]
    [InlineData("BAG-", false)]
    [InlineData("BAG-abc", false)]
    [InlineData("BAGGY-10", false)]      // ⚠ prefix must be exact, or a real product could pass
    [InlineData("GIFT-CARD", false)]
    [InlineData("045778022960", false)]  // a real bag PRODUCT is not a carrier bag
    [InlineData(null, false)]
    public void Only_a_real_bag_id_is_recognised(string id, bool expected) =>
        Assert.Equal(expected, CarrierBags.IsBagId(id));

    // ── ⚠⚠ The VAT band. This is the one that would cost money ────────────────

    /// <summary>
    /// ⚠⚠ STANDARD RATE, NOT THE ZERO BAND. `GiftCardSaleItem` picks the LOWEST band on purpose —
    /// activation is not a VAT-able supply — and reusing that helper here would under-declare VAT on
    /// every carrier bag a shop ever sells. Opposite ends of the same list.
    /// </summary>
    [Fact]
    public void The_band_chosen_is_the_standard_one_not_the_zero_one()
    {
        var bands = new List<(int, decimal)> { (1, 1.20m), (2, 1.00m), (3, 1.05m) };

        Assert.Equal(1, CarrierBags.StandardRatePreferred(bands));
    }

    /// <summary>
    /// ⚠ 20% is PREFERRED, not assumed. A tenant trading where the standard rate is different must not
    /// have 20% invented for them, so an exact 20% band wins and otherwise the highest does.
    /// </summary>
    [Fact]
    public void Twenty_percent_wins_when_present_and_the_highest_otherwise()
    {
        var withTwenty = new List<(int, decimal)> { (7, 1.23m), (8, 1.20m) };
        Assert.Equal(8, CarrierBags.StandardRatePreferred(withTwenty));

        var withoutTwenty = new List<(int, decimal)> { (7, 1.23m), (9, 1.05m) };
        Assert.Equal(7, CarrierBags.StandardRatePreferred(withoutTwenty));
    }

    /// <summary>
    /// ⚠ NULL, never a guess, when a business has no bands yet. A half-seeded tenant is retried on the
    /// next provisioning pass — the same choice `GiftCardSaleItem` makes for the same reason.
    /// </summary>
    [Fact]
    public void No_bands_means_no_answer_rather_than_a_guess()
    {
        Assert.Null(CarrierBags.StandardRatePreferred(new List<(int, decimal)>()));
        Assert.Null(CarrierBags.StandardRatePreferred(null));
    }

    // ── The name ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_default_name_states_the_price_and_nothing_it_cannot_know()
    {
        var name = CarrierBags.DefaultNameFor(10);

        Assert.Contains("0.10", name);

        // ⚠⚠ It must NOT claim "single-use" or "bag for life": which is which depends on the shop and on
        // the statutory minimum where it trades, and a guess puts a wrong word on a customer's receipt.
        Assert.DoesNotContain("single", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("for life", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reusable", name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⚠⚠ NO STATUTORY PRICE IS HARDCODED ANYWHERE. England's minimum went from 5p to **10p on
    /// 21 May 2021** and the four nations have moved at different times, so a figure baked into a build
    /// is wrong the next time Parliament moves and right nowhere but one country. The portal sets it.
    /// This pins that intent: the type offers no default price at all.
    /// </summary>
    // ── ⚠ Hidden from the inventory lists (Matt: "doesnt show in the Inventory") ──

    /// <summary>
    /// ⚠⚠ The exclusion is in the QUERY, not in the pages, because both inventory lists page on the
    /// server: a browser-side filter would show 24 rows on a page of 25 and an "X of N" that never
    /// agrees with itself. These vectors pin the filter itself.
    /// </summary>
    [Fact]
    public void Carrier_bags_are_hidden_from_item_lists_by_default()
    {
        var predicate = new Plutus.Repository.QueryParameters.ItemParameters().GetExpression().Compile();

        Assert.False(predicate(ItemWithId("BAG-10")));
        Assert.False(predicate(ItemWithId("BAG-20")));

        // ⚠ Ordinary stock is untouched — a filter that quietly dropped real items would be found
        // as "my inventory is missing things", days later, by a shop.
        Assert.True(predicate(ItemWithId("045778022960")));
        Assert.True(predicate(ItemWithId("GIFT-CARD")));   // ⚠ deliberately still visible
    }

    /// <summary>⚠ A caller that wants the bags asks for them — nothing is unreachable.</summary>
    [Fact]
    public void A_caller_can_ask_for_the_bags()
    {
        var predicate = new Plutus.Repository.QueryParameters.ItemParameters { IncludeCarrierBags = true }
            .GetExpression().Compile();

        Assert.True(predicate(ItemWithId("BAG-10")));
        Assert.True(predicate(ItemWithId("045778022960")));
    }

    /// <summary>
    /// ⚠⚠ THE BIN STILL WINS. A withdrawn bag is binned, and if the bag exclusion had replaced the bin
    /// filter rather than composing with it, asking for the bags would have shown withdrawn ones as
    /// though they were on sale.
    /// </summary>
    [Fact]
    public void Asking_for_the_bags_does_not_reopen_the_bin()
    {
        var predicate = new Plutus.Repository.QueryParameters.ItemParameters { IncludeCarrierBags = true }
            .GetExpression().Compile();

        var binned = ItemWithId("BAG-10");
        binned.BinnedAtUtc = DateTime.UtcNow;

        Assert.False(predicate(binned));
    }

    private static Plutus.Entities.Models.Item ItemWithId(string idOne) =>
        // ⚠ CreatedAt must be set: the base filter is a date window from the Unix epoch, and a default
        // DateTime is 0001-01-01, so an unset item fails for a reason that has nothing to do with bags.
        new() { IdOne = idOne, IdTwo = Guid.NewGuid(), CreatedAt = DateTime.Now };

    [Fact]
    public void The_code_hardcodes_no_statutory_price()
    {
        var members = typeof(CarrierBags).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => f.Name)
            .ToList();

        Assert.DoesNotContain(members, n => n.Contains("Price", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, n => n.Contains("Pence", StringComparison.OrdinalIgnoreCase));
    }
}
