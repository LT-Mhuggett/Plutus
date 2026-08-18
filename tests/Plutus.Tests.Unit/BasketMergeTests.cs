using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// When a newly-added unit joins an existing basket line.
///
/// ⚠⚠ **THESE VECTORS ARE THE C2 PIN.** The same table runs in
/// `Plutus.Frontend.WebApp/src/till/basketMerge.test.ts` against the web till's own predicate. Two
/// tills that disagree about when a line merges disagree about **what the customer is charged** — and
/// they did disagree until 2026-08-18. Add a case here and add it there, in the same commit.
/// </summary>
public class BasketMergeTests
{
    // A £5.00 item at 20% VAT: 500 inc, 417 ex.
    private const long Inc = 500;
    private const long Ex = 417;

    private static bool Merge(
        bool sameItem = true, bool isReturn = false, bool adjusted = false, bool discounted = false,
        long lineInc = Inc, long lineEx = Ex) =>
        BasketMerge.CanMerge(sameItem, isReturn, adjusted, discounted, lineInc, lineEx, Inc, Ex);

    [Fact]
    public void The_same_item_at_the_catalogue_price_merges()
    {
        Assert.True(Merge());
    }

    [Fact]
    public void A_different_item_never_merges()
    {
        Assert.False(Merge(sameItem: false));
    }

    /// <summary>⚠ Goods going back and goods going out are opposite directions of money; one row
    /// cannot be both.</summary>
    [Fact]
    public void A_return_line_never_takes_a_sale_unit()
    {
        Assert.False(Merge(isReturn: true));
    }

    /// <summary>
    /// ⚠⚠ THE ONE THIS RULE WAS EXTRACTED FOR. Ring a £5 item, adjust it to 50p, leave the line
    /// selected, scan the same item again — and MAUI's selected-line fast path joined the second unit
    /// to the adjusted line **at 50p**. The shop sold the second one for a tenth of its price, with
    /// the receipt as the only evidence. The web till has never done this.
    /// </summary>
    [Fact]
    public void An_adjusted_line_never_takes_another_unit()
    {
        Assert.False(Merge(adjusted: true, lineInc: 50, lineEx: 42));
    }

    /// <summary>
    /// ⚠⚠ AND NOT EVEN WHEN THE ADJUSTED PRICE HAPPENS TO EQUAL THE CATALOGUE PRICE. This is the case
    /// the old code relied on getting wrong: it tested only the price pair, so a line adjusted to the
    /// very figure it started at passed the arithmetic and re-merged. The flag is checked **before**
    /// the prices for exactly this reason.
    /// </summary>
    [Fact]
    public void An_adjusted_line_is_excluded_by_the_flag_not_by_its_price()
    {
        Assert.False(Merge(adjusted: true, lineInc: Inc, lineEx: Ex));
    }

    /// <summary>⚠ A discount was granted against what was in the basket at the time, not against the
    /// item for ever.</summary>
    [Fact]
    public void A_discounted_line_never_takes_another_unit()
    {
        Assert.False(Merge(discounted: true));
    }

    [Fact]
    public void A_line_whose_price_has_moved_off_the_catalogue_does_not_merge()
    {
        Assert.False(Merge(lineInc: 450, lineEx: 375));
    }

    /// <summary>
    /// ⚠⚠ BOTH HALVES OF THE PAIR ARE COMPARED. A line agreeing on the inc price while disagreeing on
    /// the ex price holds a different VAT position — merging them would put two VAT answers on one
    /// row, and the sale line's declared rate is derived from that pair (till-design C1 rule 2).
    /// ⚠ This is the case a single-field comparison would let through.
    /// </summary>
    [Fact]
    public void The_ex_half_matters_even_when_the_inc_half_agrees()
    {
        Assert.False(Merge(lineEx: 400));
    }

    [Fact]
    public void The_inc_half_matters_even_when_the_ex_half_agrees()
    {
        Assert.False(Merge(lineInc: 550));
    }

    /// <summary>⚠ A zero-priced line — a freebie — still merges if nothing was adjusted, because the
    /// catalogue itself says zero. Nothing here treats 0 as "unset".</summary>
    [Fact]
    public void A_genuinely_free_item_merges_with_itself()
    {
        Assert.True(BasketMerge.CanMerge(true, false, false, false, 0, 0, 0, 0));
    }
}
