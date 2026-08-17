import { basketTotals, type BasketLine } from "./basket.ts";

/**
 * W-P7 — the card surcharge, on the web till.
 *
 * ⚠⚠ C2 TWIN of `Plutus.SharedKernel.CardSurchargeVat` (the money) and
 * `Services/Storage/CheckoutCommit.SurchargeItem` (which lines it rides on). Change one, change all
 * three in the same commit — `till-design.md` C2 pins the pair, and `surcharge.test.ts` deliberately
 * uses the SAME VECTORS as `tests/Plutus.Tests.Unit/CardSurchargeVatTests.cs` so a divergence shows
 * up as a failing test rather than as a penny.
 *
 * ⚠⚠ THE FEE'S VAT FOLLOWS THE BASKET, and this is the whole reason the rule is shared rather than
 * re-derived. A surcharge is further consideration for the main supply (*Bookit* C-607/14 / *NEC*
 * C-130/15), so a fee on zero-rated goods carries NO VAT, on standard-rated goods 20%, and on a
 * mixed basket the blend. The failure mode is a plausible-looking constant: hardcoding 20% on the fee
 * reconciles perfectly — every invariant passes — and is wrong on every basket that is not purely
 * standard-rated.
 *
 * ⚠ WHY THE WEB TILL HAD NOTHING HERE. MAUI has charged this since it moved off the legacy
 * `PaymentMethod.Charge` field; the web till had no surcharge code at all, so the same tenant's two
 * counters would have taken different money for the same basket the moment anybody set a rate. It is
 * DORMANT for Kapow (rate zero → no line, no behaviour change), which is exactly why it could sit
 * unnoticed until the first tenant that charges a card fee.
 */

/** The natural key of the provisioned catalogue row the fee is rung through — owned by
 *  `CardSurchargeVat.ItemIdOne`. ⚠ Two spellings of this string would be a fee that sells fine on
 *  one till and breaks the legacy sale bridge on another. */
export const SURCHARGE_ITEM_ID = "CARD-SURCHARGE";

/** What the operator and the customer read on the line. Matches `CardSurchargeSaleItem.ItemName`. */
export const SURCHARGE_NAME = "Card surcharge";

/**
 * The tax row the fee line claims — deliberately one NO published band can claim.
 *
 * ⚠⚠ THE ITEM'S CATALOGUE BAND MUST NEVER TAX THIS LINE. Server-side the `CARD-SURCHARGE` row sits
 * on the zero band (see `CardSurchargeSaleItem`), and sending `vatBand: "zero"` alongside a line
 * whose rate is the basket's blended 1905bp is a contradiction — the server would have a stated band
 * saying no VAT is due and a declared figure saying some is. On a standard-rated basket that reads as
 * zero-rated output tax.
 *
 * A taxId no band claims makes `vatBandForTaxId` return null, so no band travels and the server snaps
 * the band from the declared rate — the same thing the MAUI till does by sending `VatBandKey: null`
 * on every line.
 *
 * ⚠ Negative because legacy tax ids are positive: there is no future band that could claim it.
 */
export const SURCHARGE_TAX_ID = -1;

/** The wire byte for a card tender — `SharedKernel.Tenders.Card`, and `api.ts`'s `tenderTypeFor`
 *  returns it. ⚠ A gift card is 4, not 1: `tenderTypeFor` checks "gift" first for exactly this
 *  reason, and a gift-card redemption must not attract a card fee. */
const CARD_TENDER = 1;

/**
 * Away-from-zero at the midpoint — .NET's `MidpointRounding.AwayFromZero`.
 *
 * ⚠ `Math.round` only matches it for non-negative values: JS rounds −2.5 to −2, .NET to −3. Every
 * caller below has already refused negatives, so this is a named guard against a future one that
 * doesn't, not a live difference.
 */
const roundAway = (v: number): number => (v < 0 ? -Math.round(-v) : Math.round(v));

/**
 * The fee itself, from the tenant's setting: `flat + gross × bp ÷ 10000`, rounded ONCE, away from
 * zero, MULTIPLY BEFORE DIVIDING — the same conventions as every shared money rule.
 *
 * Percent-plus-flat because that is the shape of every acquirer's own pricing (1.69% + 20p), and
 * lawful surcharging (where it is lawful at all) is capped at passing that cost through.
 *
 * ⚠ THROWS on a negative setting, exactly as the shared rule does. Money off is a discount, not a
 * fee, and the two must not blur — a return drops a discount and keeps a fee. `CheckoutDialog`
 * catches it and refuses to complete rather than blanking the till: fail closed on money.
 *
 * ⚠ A basket with no positive sale value gets 0 rather than a throw — "don't surcharge this" is an
 * answer, not an error.
 *
 * ⚠ DOUBLE vs `decimal`, and why it agrees: at the only point rounding can differ — a true exact
 * `.5` — the quotient is an integer plus a half and so is EXACTLY representable as a double, and
 * IEEE division is correctly rounded, so `Math.round` sees the exact midpoint the .NET decimal sees.
 * The nearest non-half value a denominator of 10000 can produce is 5e-5 away, tens of thousands of
 * ulps at till-sized numbers. There is no basket in the money range where the two disagree.
 */
export function feePence(surchargeBp: number, flatPence: number, basketGrossPence: number): number {
  if (surchargeBp < 0 || flatPence < 0)
    throw new Error("A surcharge setting cannot be negative — money off is a discount, not a fee.");

  if (basketGrossPence <= 0) return 0;

  return flatPence + roundAway((basketGrossPence * surchargeBp) / 10000);
}

/**
 * The (inc, ex) price pair for the fee "line", apportioned from the basket it rides on.
 *
 * One blended ratio, rounded ONCE, away from zero — HMRC asks for a fair and reasonable
 * apportionment by the values of the underlying supplies, and `fee × basketEx ÷ basketGross` is
 * exactly that. The line's declared rate then derives from this pair and its VAT is gross − ex;
 * nothing new is invented, and nothing downstream needs to know the line is a fee.
 *
 * ⚠⚠ MULTIPLY BEFORE DIVIDING. `fee × (ex ÷ gross)` puts a non-terminating decimal in the middle
 * (10000/12000 = 0.8333…), so a true midpoint like 3 × 10000 ÷ 12000 = 2.5 arrives as 2.4999… and
 * rounds the wrong way. Products first keeps the value exact until the single rounding. Both tills
 * must produce THIS number or one basket carries two different VAT figures on two counters.
 *
 * ⚠ Refuses a negative fee, a non-positive basket (do not surcharge a refund — there is no supply
 * for the fee to follow) and an ex total outside `[0, gross]` (the caller summed the wrong lines,
 * and the fee would inherit the error).
 */
export function pairFor(
  surchargePence: number,
  basketGrossPence: number,
  basketExPence: number,
): { incPence: number; exPence: number } {
  if (surchargePence < 0)
    throw new Error(
      "A surcharge is a positive fee. Money off is a discount, and the two must not blur, "
      + "because a return drops one and not the other.");

  if (surchargePence === 0) return { incPence: 0, exPence: 0 };

  if (basketGrossPence <= 0)
    throw new Error(
      "A surcharge takes the VAT treatment of the goods it is charged on, so a basket with no "
      + "positive sale value gives it nothing to follow. Do not surcharge a refund.");

  if (basketExPence < 0 || basketExPence > basketGrossPence)
    throw new Error(
      "The basket's ex-VAT total must sit between zero and its gross — anything else means the "
      + "caller summed the wrong lines, and the fee would inherit the error.");

  return {
    incPence: surchargePence,
    exPence: roundAway((surchargePence * basketExPence) / basketGrossPence),
  };
}

/** Is the fee already on this basket? ⚠ Applied ONCE per sale — a split across two cards must not
 *  charge the flat half twice. Case-insensitive like `MemberDiscountBasket`'s own exclusion. */
export const hasSurcharge = (lines: readonly BasketLine[]): boolean =>
  lines.some((l) => l.item.idOne?.toUpperCase() === SURCHARGE_ITEM_ID);

/**
 * Is a card actually being tendered?
 *
 * ⚠ THIS IS THE WEB TILL'S EQUIVALENT OF MAUI PICKING A CARD METHOD IN THE TENDER LOOP. MAUI asks
 * for one tender at a time and adds the fee when a card method is chosen; this screen shows every
 * method at once, so "chosen" is a positive amount typed in a card row. Same decision, same money.
 *
 * ⚠ There is no circularity to worry about: the fee is a function of the GOODS, not of the amounts,
 * so filling the card row with the new remainder cannot move it again.
 */
export const cardIsTendered = (
  rows: readonly { tenderType: number; pence: number }[],
): boolean => rows.some((r) => r.tenderType === CARD_TENDER && r.pence > 0);

/**
 * The fee line for this basket, priced by the shared rules — or null when none applies (no setting,
 * nothing being sold, or the fee already on).
 *
 * ⚠⚠ A REAL BASKET LINE, not a note and not a header adjustment: the sale model has no fee field,
 * `grossPence` must equal the sum of the line grosses, and the legacy projection's rows foreign-key
 * to Items. That is why the server provisions `CARD-SURCHARGE` at startup.
 *
 * ⚠ Computed on the SALE lines only, after discounts — the goods actually being paid for. A refund
 * attracts no fee, and an existing fee line never rides on itself.
 *
 * ⚠ ≥ ONE SALE LINE IS THE TEST, not "the basket nets positive" — the twin of MAUI's
 * `refundOnly = !Basket.Any(item && !IsReturn)`. A mixed basket that nets negative still carries a
 * fee on both tills. That is a shared quirk, deliberately kept shared: a third behaviour invented
 * here would be a divergence, and the tills disagreeing is the failure this work package removes.
 *
 * ⚠ BUILT AT CHECKOUT, NEVER STORED IN THE BASKET — the one place the web till deliberately differs
 * from MAUI's mechanics (it adds the line to `Basket`). A fee line living in basket state would
 * survive a cancelled checkout, a park/recall and a switch to cash, and a phantom fee on a cash sale
 * is money taken that nobody authorised. Derived here it cannot outlive the dialog that priced it.
 */
export function surchargeLine(
  lines: readonly BasketLine[],
  surchargeBp: number,
  flatPence: number,
  key: number,
): BasketLine | null {
  if (hasSurcharge(lines)) return null;

  const saleLines = lines.filter((l) => !l.isReturn);
  if (saleLines.length === 0) return null;

  const { totalPence, totalExTaxPence } = basketTotals(saleLines);
  const fee = feePence(surchargeBp, flatPence, totalPence);
  if (fee === 0) return null;

  const { incPence, exPence } = pairFor(fee, totalPence, totalExTaxPence);

  return {
    key,
    item: {
      idOne: SURCHARGE_ITEM_ID,
      name: SURCHARGE_NAME,
      brand: "-",
      desc: "",
      cost: 0,
      // Kept consistent with the line's own pence so nothing that reads the item disagrees with the
      // line. ⚠ The SERVER's row is priced 0 — the fee has no list price, the till prices it.
      exPrice: exPence / 100,
      price: incPence / 100,
      taxId: SURCHARGE_TAX_ID,
      catId: "",
      stockUntracked: true,
    },
    quantity: 1,
    pricePence: incPence,
    exPricePence: exPence,
    adjusted: false,
  };
}

// ── the tenant's setting, cached ─────────────────────────────────────────────

const CACHE_KEY = "plutus.surcharge";

/**
 * Last-known-good, so a till that cannot reach the server charges what it last knew.
 *
 * ⚠⚠ THE PORTAL IS THE SOURCE OF TRUTH AND THIS IS WHY THE CACHE EXISTS: a fee silently vanishing
 * offline and reappearing online would make two identical baskets total differently an hour apart,
 * and the operator would wear the argument. `GatewaySurcharge` caches it in the MAUI till's Meta for
 * the same reason.
 *
 * ⚠ Never blocks and never guesses: unreadable storage, a bad blob or a till that has never
 * connected all charge NOTHING.
 */
export function rememberSurcharge(surchargeBp: number, flatPence: number): void {
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify({ bp: surchargeBp, flat: flatPence }));
  } catch {
    // private mode / quota — the till keeps selling, it just won't know the fee offline
  }
}

export function cachedSurcharge(): { bp: number; flatPence: number } {
  const none = { bp: 0, flatPence: 0 };
  try {
    const raw = localStorage.getItem(CACHE_KEY);
    if (!raw) return none;
    const saved = JSON.parse(raw) as { bp?: unknown; flat?: unknown };
    const bp = typeof saved?.bp === "number" && saved.bp >= 0 ? Math.round(saved.bp) : 0;
    const flat = typeof saved?.flat === "number" && saved.flat >= 0 ? Math.round(saved.flat) : 0;
    return { bp, flatPence: flat };
  } catch {
    return none;
  }
}
