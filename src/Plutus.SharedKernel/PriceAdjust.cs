namespace Plutus.SharedKernel;

/// <summary>
/// Adjusting a line’s price at the till — what the operator types, and what the VAT does.
///
/// ⚠⚠ THE OPERATOR TYPES **ONE** NUMBER: the price the customer pays, VAT included. The ex-VAT half
/// is DERIVED from the catalogue pair's proportion. That is the whole rule, and it exists because
/// asking for both halves lets the two disagree — and the sale line's declared VAT rate is derived
/// from the pair (C1 rule 2), so two numbers typed by hand ARE the VAT figure on that sale.
///
/// ⚠ Found 2026-08-18: MAUI's price override asked for **ex AND inc in one dialog** and wrote both
/// straight onto the line, so an operator could type £10.00 ex / £10.50 inc and that line would
/// declare **5% VAT on a 20% item** — no validation, no warning, and the wrong number on a VAT
/// return. The web till has always asked for one number and scaled (`till/basket.ts`, the `adjust`
/// reducer). This is that rule, shared, so the two cannot drift again.
///
/// ⚠ PROPORTION, NOT RATE. It deliberately does not look up the item's VAT band: the catalogue pair
/// already encodes it, and a band lookup would need the network for something that must work with the
/// line down. A zero-rated item has ex == inc, so its override stays zero-rated by construction.
/// </summary>
public static class PriceAdjust
{
    /// <summary>
    /// The ex-VAT pence for a newly typed inc-VAT price, keeping the catalogue's VAT proportion.
    ///
    /// ⚠ FROM THE CATALOGUE PAIR, NOT THE LINE'S CURRENT PAIR — so adjusting the same line twice
    /// derives from the same proportion both times and cannot drift a penny per edit. This is what
    /// the web till does (`l.item.exPrice / l.item.price`, never `l.exPricePence / l.pricePence`).
    ///
    /// ⚠⚠ MULTIPLY BEFORE DIVIDING, round ONCE, AWAY FROM ZERO — the house convention for every
    /// shared money rule, and the one the card surcharge had to learn twice: `newInc × (ex ÷ inc)`
    /// puts a non-terminating decimal in the middle and turns an exact midpoint into a value that
    /// rounds the wrong way. ⚠ .NET's `Math.Round` defaults to **banker's rounding**, which would
    /// disagree with the web till's `Math.round` on every exact half — hence the explicit mode.
    ///
    /// ⚠ A catalogue pair that cannot give a proportion (a zero or negative inc price — a free line,
    /// or data nobody has fixed) yields ex == inc: no VAT claimed on a price we cannot reason about,
    /// which is the conservative answer and the same one the web till gives via its `: 1` fallback.
    /// </summary>
    public static long ExFromInc(long newIncPence, long catalogueIncPence, long catalogueExPence)
    {
        if (catalogueIncPence <= 0) return newIncPence;

        // ⚠ Clamped: an ex above inc would declare negative VAT, and an ex below zero is not a price.
        // Both mean the catalogue row is wrong, and neither should reach a VAT return through here.
        if (catalogueExPence <= 0) return 0;
        if (catalogueExPence >= catalogueIncPence) return newIncPence;

        return (long)System.Math.Round(
            (decimal)newIncPence * catalogueExPence / catalogueIncPence,
            System.MidpointRounding.AwayFromZero);
    }
}
