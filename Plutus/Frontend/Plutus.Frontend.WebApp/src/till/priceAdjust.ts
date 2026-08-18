/**
 * Adjusting a line's price at the till.
 *
 * ⚠⚠ C2 TWIN of `Plutus.SharedKernel.PriceAdjust`. The operator types **one** number — the price the
 * customer pays, VAT included — and the ex-VAT half is DERIVED from the catalogue pair's proportion.
 *
 * ⚠ The web till has always worked this way; it is MAUI that asked for ex AND inc in one dialog and
 * wrote both onto the line, so a mistyped pair became the declared VAT rate on that sale (5% on a 20%
 * item, silently). Extracted here — from the `adjust` reducer, unchanged in behaviour except where
 * noted below — so the two tills share the rule instead of resembling each other.
 *
 * ⚠⚠ PRODUCTS BEFORE DIVISION, and in THIS language that is not cosmetic. The reducer computed
 * `ratio = exPrice / price` in POUNDS and then `Math.round(pricePence * ratio)`. A double cannot hold
 * `70/100` exactly, so `45 × 0.7` is `31.499999999999996` and rounds to **31** where the exact answer
 * is 31.5 → **32**. Multiplying first keeps the value an exact rational until the single rounding.
 * ⚠ The .NET twin's mutation run showed the ratio-first form SURVIVING there, because `decimal`
 * carries enough digits — so this is a divergence that only one of the two languages can detect, and
 * the vector belongs in both suites. Same lesson as the card surcharge (§5b W-P7).
 */

export interface CataloguePair {
  /** Inc-VAT catalogue price, in pence. */
  incPence: number;
  /** Ex-VAT catalogue price, in pence. */
  exPence: number;
}

/** Away-from-zero at the midpoint — .NET's `MidpointRounding.AwayFromZero`. ⚠ `Math.round` matches it
 *  only for non-negative values, and the caller has already refused negatives. */
const roundAway = (v: number): number => (v < 0 ? -Math.round(-v) : Math.round(v));

/**
 * The ex-VAT pence for a newly typed inc-VAT price, keeping the catalogue's VAT proportion.
 *
 * ⚠ From the CATALOGUE pair, never the line's current pair, so adjusting the same line twice derives
 * from the same proportion both times and cannot drift a penny per edit.
 *
 * ⚠ A catalogue pair that cannot give a proportion — no inc price, or an impossible pair (ex above
 * inc would declare negative VAT; ex at or below zero is not a price) — yields **ex == inc**, i.e. no
 * VAT claimed on a price we cannot reason about. Conservative on purpose, and the same answer the
 * .NET twin gives.
 */
export function exFromInc(newIncPence: number, catalogue: CataloguePair): number {
  const { incPence, exPence } = catalogue;
  if (!(incPence > 0)) return newIncPence;
  if (!(exPence > 0)) return 0;
  if (exPence >= incPence) return newIncPence;

  return roundAway((newIncPence * exPence) / incPence);
}
