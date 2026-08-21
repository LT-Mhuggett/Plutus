/**
 * Scheduled discounts — "Wednesday Warhammer": a rule the portal sets that takes money off by
 * itself, with no card and no operator action.
 *
 * ⚠⚠ C2 TWIN of `Plutus.SharedKernel.ScheduledDiscounts`, and `scheduledDiscounts.test.ts` runs the
 * SAME VECTORS as `ScheduledDiscountsTests`. **Add a case to one, add it to the other** — the card
 * surcharge row proved that copying vectors across is not the same as copying their protection.
 *
 * ⚠⚠ THE SCHEDULE IS EVALUATED HERE, AGAINST THIS TILL'S CLOCK, from raw fields the server never
 * pre-evaluates. A browser till left open from Monday to Thursday must start discounting on Wednesday
 * morning and stop on Wednesday night without ever reloading — the same reason
 * `PermissionGrant.IsActiveAt` ships its window raw rather than an answer.
 */

/** `Discount.Type` — a DB/wire contract, not an enum anybody may renumber. */
export const DISCOUNT_FIXED = 0;
/** A fraction: 0.10 is 10%. */
export const DISCOUNT_PERCENT = 1;

/**
 * One rule as `GET /api/v1/discounts/rules` sends it.
 *
 * ⚠ `windowStartLocal`/`windowEndLocal` are LOCAL wall-clock `"HH:mm:ss"`, while `validFromUtc`/
 * `validToUtc` are UTC instants. Mixing them is the bug the .NET record's header exists to make hard,
 * and the shapes are named so the mistake is visible here too.
 */
export interface ScheduledDiscount {
  /** ⚠ The catalogue's REAL id — what lets an auto-applied rule ride `LineMeta.discounts[]` and
   *  project into `Transaction_Discount`, which is keyed on a real `DiscountId`. */
  id: number;
  name: string;
  type: number;
  /** A FRACTION — 0.10 is 10%. Used when `type === DISCOUNT_PERCENT`. */
  percentFraction: number;
  /** ⚠ INTEGER PENCE, per unit. Used when `type === DISCOUNT_FIXED`.
   *
   *  ⚠⚠ THE WIRE CARRIES PENCE BECAUSE ONE AMBIGUOUS FIELD WAS REFUSED. The legacy column is a
   *  decimal meaning POUNDS here and a FRACTION there, and the .NET guard
   *  `No_module_declares_decimal_or_double_money_members` would not let that shape travel — rightly:
   *  a field that is money in one branch and a ratio in the other is a unit error waiting for its
   *  first reader. The server splits it once. */
  fixedAmountPence: number;
  autoApply: boolean;
  allApplicable: boolean;
  /** Bit 0 = Sunday … bit 6 = Saturday; null/absent = any day. */
  daysOfWeekMask?: number | null;
  windowStartLocal?: string | null;
  windowEndLocal?: string | null;
  validFromUtc?: string | null;
  validToUtc?: string | null;
  categoryIds?: string[] | null;
  itemIdOnes?: string[] | null;
}

/**
 * Pence off one line for a (type, amount) pair — **the till's one discount arithmetic**.
 *
 * ⚠⚠ `basket.ts lineDiscountPence` DELEGATES TO THIS, so the figure the resolver compares candidates
 * on is by construction the figure the basket charges. Predicting the money with a second expression
 * is how a "largest wins" comparison picks a different winner from the one the customer pays for.
 *
 * ⚠ The .NET twin (`LineDiscounts`) additionally caps at the line's value and THROWS above a
 * fraction of 1. Neither is reachable from a rule: `isWellFormed` refuses `amount > 1` for a
 * percentage, and for any fraction at or below 1 `round(v × f) ≤ v`, so the cap cannot bite. The two
 * are therefore identical for every input a rule can produce — stated here because a reader diffing
 * the files will notice the missing lines and deserve to know they are unreachable rather than
 * forgotten.
 */
export const discountPence = (type: number, amount: number, unitIncPence: number, quantity: number): number =>
  type === DISCOUNT_FIXED
    ? Math.round(amount * 100) * quantity
    : Math.round(unitIncPence * quantity * amount);

/**
 * ⚠ A UTC instant that arrived WITHOUT its `Z` is still a UTC instant. `Date.parse` reads a bare
 * `"2026-08-19T00:00:00"` as LOCAL time, so a server that serialised a `DateTime` with an unspecified
 * Kind would shift every promotion boundary by the till's offset — an hour of wrong prices at each
 * end, twice a year. The portal already learned this (`new Date(closedAtUtc + "Z")`).
 */
const asUtcMs = (iso: string): number => {
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(iso);
  return Date.parse(hasZone ? iso : iso + "Z");
};

/** `"HH:mm"`, `"HH:mm:ss"` or `"HH:mm:ss.fff"` → seconds since midnight; null when unreadable. */
const secondsOfDay = (t: string | null | undefined): number | null => {
  if (!t) return null;
  const m = /^(\d{1,2}):(\d{2})(?::(\d{2}))?/.exec(t.trim());
  if (!m) return null;
  const h = Number(m[1]), min = Number(m[2]), s = Number(m[3] ?? 0);
  if (h > 23 || min > 59 || s > 59) return null;
  return h * 3600 + min * 60 + s;
};

/**
 * Is this rule something the till may act on at all?
 *
 * ⚠⚠ IT FAILS TOWARDS NOT DISCOUNTING. A rule that should have applied and did not charges full
 * price — visible at the counter, and the operator can still take it off by hand. A malformed rule
 * that applies anyway gives money away silently on every basket.
 *
 * ⚠⚠ AND THE EMPTY-TARGET CASE MEANS **NOTHING**, NEVER **EVERYTHING** — the carrier-bag precedent.
 * A rule whose category list failed to load must not become a whole-basket discount.
 */
export const isWellFormed = (r: ScheduledDiscount): boolean =>
  !!r &&
  // ⚠ A fraction ABOVE 1 is refused, not clamped: it is the shape of the legacy defect where a typed
  // "10" meant 1000%. ⚠ An unrecognised kind is not a discount — fails closed, like every gate here.
  (r.type === DISCOUNT_PERCENT
    ? r.percentFraction > 0 && r.percentFraction <= 1
    : r.type === DISCOUNT_FIXED && r.fixedAmountPence > 0) &&
  (r.allApplicable || (r.categoryIds?.length ?? 0) > 0 || (r.itemIdOnes?.length ?? 0) > 0);

/**
 * Is this rule live at `now`?
 *
 * ⚠⚠ MIRRORS `PermissionGrant.IsActiveAt` EXPRESSION BY EXPRESSION, including the parts that could be
 * argued — two "is it live now" rules that disagree about a boundary are worse than one imperfect
 * rule.
 *
 * ⚠ THE WINDOW IS INCLUSIVE AT BOTH ENDS, so a 09:00–17:00 rule is live AT 17:00:00. ⚠ A window that
 * WRAPS MIDNIGHT (22:00–02:00) matches nothing, copied deliberately rather than fixed here: the .NET
 * twin calls it "deliberately unhandled rather than half-handled", and fixing one side alone would
 * make a late-night discount behave differently from a late-night permission.
 *
 * ⚠ `getDay()` is 0 = Sunday, which is why bit 0 is Sunday on both sides.
 */
export const isLiveAt = (r: ScheduledDiscount, now: Date): boolean => {
  const nowMs = now.getTime();
  if (r.validFromUtc && nowMs < asUtcMs(r.validFromUtc)) return false;
  if (r.validToUtc && nowMs > asUtcMs(r.validToUtc)) return false;

  const mask = r.daysOfWeekMask;
  if (mask !== null && mask !== undefined && (mask & (1 << now.getDay())) === 0) return false;

  const start = secondsOfDay(r.windowStartLocal);
  const end = secondsOfDay(r.windowEndLocal);
  if (start !== null || end !== null) {
    const t = now.getHours() * 3600 + now.getMinutes() * 60 + now.getSeconds();
    if (start !== null && t < start) return false;
    if (end !== null && t > end) return false;
  }
  return true;
};

/**
 * Does this rule target this item — by "everything", by its category, or by its barcode?
 *
 * ⚠ A UNION, not a precedence chain: naming one extra item must not stop a rule applying to the
 * category it also names. ⚠ Barcodes compare case-insensitively — a rule that missed because somebody
 * typed a lower-case letter in the portal is indistinguishable from a rule that was not live.
 */
export const targets = (r: ScheduledDiscount, categoryId: string | null | undefined, itemIdOne: string | null | undefined): boolean => {
  if (r.allApplicable) return true;
  if (categoryId && r.categoryIds?.some((c) => c.toLowerCase() === categoryId.toLowerCase())) return true;
  if (itemIdOne && r.itemIdOnes?.some((i) => i.toLowerCase() === itemIdOne.toLowerCase())) return true;
  return false;
};

/** The facts about one line that decide whether an automatic discount may land on it. */
export interface DiscountableLine {
  unitIncPence: number;
  quantity: number;
  categoryId?: string | null;
  itemIdOne?: string | null;
  isReturn: boolean;
  /** ⚠⚠ A discount the OPERATOR put there — never one the resolver put there. See `autoDiscounts.ts`. */
  hasManualDiscount: boolean;
  isGiftCard: boolean;
  isCardSurcharge: boolean;
}

/**
 * Would this rule land on this line, right now?
 *
 * ⚠ The four exclusions are the members' discount's, not a second opinion: a scheduled discount that
 * discounted a return, stacked on a manual discount, knocked money off a gift card, or ate into the
 * card fee would be wrong for the identical reasons.
 */
export const landsOn = (r: ScheduledDiscount, line: DiscountableLine, now: Date): boolean => {
  if (!isWellFormed(r)) return false;
  if (!isLiveAt(r, now)) return false;
  if (line.isCardSurcharge || line.isReturn || line.hasManualDiscount || line.isGiftCard) return false;
  return targets(r, line.categoryId, line.itemIdOne);
};

/**
 * Pence this rule takes off this line — 0 when it does not apply.
 *
 * ⚠ Goes through `discountPence`, the till's ONE discount arithmetic, so a scheduled discount and a
 * hand-applied one of the same size come to the same penny. ⚠ `discountPence` takes the fixed amount
 * in POUNDS because that is the shape of a `LineDiscount` on this till, so the pence the wire carries
 * are converted here — once, in the one expression that needs it.
 */
export const forLine = (r: ScheduledDiscount, line: DiscountableLine, now: Date): number =>
  landsOn(r, line, now)
    ? discountPence(
        r.type,
        r.type === DISCOUNT_PERCENT ? r.percentFraction : r.fixedAmountPence / 100,
        line.unitIncPence,
        line.quantity,
      )
    : 0;

/**
 * The AUTO-APPLY rules that would land on this line, biggest first then by lowest id.
 *
 * ⚠ Rules with `autoApply: false` are catalogue entries an operator picks by hand and are never
 * returned here — that is the whole difference between a rule and a list item.
 *
 * ⚠ THE TIE-BREAK IS NOT COSMETIC: two rules worth the same money must resolve identically on both
 * tills, or the same basket shows a different badge on two counters. Id is the only stable key
 * available offline; left to iteration order this would depend on JSON ordering.
 */
export const candidatesFor = (
  rules: ScheduledDiscount[] | null | undefined,
  line: DiscountableLine,
  now: Date,
): { rule: ScheduledDiscount; pence: number }[] =>
  (rules ?? [])
    .filter((r) => r && r.autoApply)
    .map((rule) => ({ rule, pence: forLine(rule, line, now) }))
    .filter((c) => c.pence > 0)
    .sort((a, b) => b.pence - a.pence || a.rule.id - b.rule.id);

/**
 * How a scheduled discount is LABELLED on the line and the receipt.
 *
 * ⚠ THE SHOP'S OWN NAME, with no rate appended — a shop that called its rule "Wednesday Warhammer
 * 10%" would otherwise get the rate twice, and the money off is already printed beside it.
 * ⚠ A blank name still prints the same something on every till: an empty receipt line reads as a fault.
 */
export const label = (name: string | null | undefined): string =>
  !name || !name.trim() ? "Discount" : name.trim();
