/**
 * The ONE automatic-discount resolver on this till — the member's tier, or a scheduled rule,
 * whichever is worth more.
 *
 * ⚠⚠ C2 TWIN of `Plutus.SharedKernel.AutoDiscounts`; `autoDiscounts.test.ts` runs the same vectors as
 * `AutoDiscountsTests`. **Add a case to one, add it to the other.**
 *
 * ⚠⚠ IT EXISTS BECAUSE THE ALTERNATIVE IS TWO ENGINES RACING. Before scheduled discounts there was
 * exactly one automatic discount — the tier — and each till applied it its own way. Adding a second
 * source without one resolver means each till deciding for itself what happens when both apply, and
 * "the customer got 10% here and 15% there on the same Wednesday" is a money defect nothing
 * downstream can detect.
 */

import {
  DISCOUNT_PERCENT,
  candidatesFor,
  discountPence,
  label as ruleLabel,
  type DiscountableLine,
  type ScheduledDiscount,
} from "./scheduledDiscounts.ts";

/** ⚠ The sentinel the members' discount carries — the twin of `MemberDiscount.SentinelDiscountId`.
 *  It is load-bearing: it keeps the tier discount OFF `LineMeta.discounts[]`, whose legacy-bridge
 *  projection is keyed on a REAL `DiscountId` and would FK-fail the whole sale on a synthetic one.
 *  It is also how `clearMemberDiscount` removes only the automatic discount and leaves the
 *  operator's own alone. */
export const MEMBER_DISCOUNT_ID = 0;

/** What the attached customer is entitled to right now. */
export interface MemberStanding {
  hasMembership: boolean;
  expired: boolean;
  autoDiscountRate: number;
  tierName?: string | null;
}

export const NO_MEMBER: MemberStanding = { hasMembership: false, expired: false, autoDiscountRate: 0, tierName: null };

/** The automatic discount a line should carry. */
export interface AutoDiscount {
  discountId: number;
  name: string;
  type: number;
  amount: number;
  pence: number;
  /** Auto-filled and mandatory (binding default 22(c)): nobody typed it because nobody decided
   *  anything — the tier or the rule IS the reason. */
  reason: string;
}

/** ⚠ The tier label the customer reads, on the line and on the receipt — the twin of
 *  `MemberDiscount.Label`. Centralised here because `TillPage` built the same string inline, and a
 *  till that wrote "Gold member discount" would put a different word on the receipt for the same
 *  money. The percentage is rounded for DISPLAY only; the arithmetic uses the unrounded fraction. */
export const memberLabel = (tierName: string | null | undefined, rate: number): string =>
  `${tierName ?? ""} ${Math.round(rate * 100)}%`.trim();

/** Does this membership grant anything at all? Twin of `MemberDiscount.Applies` — an EXPIRED
 *  membership grants nothing and its rate is ignored rather than trusted. */
export const memberApplies = (m: MemberStanding): boolean =>
  m.hasMembership && !m.expired && m.autoDiscountRate > 0;

/**
 * The automatic discount this line should carry right now, or null for none.
 *
 * ⚠⚠ DECISION D2 — THE LINE TAKES THE LARGER, NEVER BOTH. This till is structurally
 * one-discount-per-line (a single `discount?` slot), so stacking is not merely disallowed, it is
 * inexpressible. Largest-wins is the customer-best reading of two promises the shop has made, and it
 * is deterministic where "whichever applied first" is not.
 *
 * ⚠ A TIE GOES TO THE MEMBER. Equal money either way, so the choice is free — and the tier discount
 * is the one that existed first, the one the customer is told about at the counter, and the one whose
 * absence they would query. Fixed rather than left to comparison order so both tills agree.
 *
 * ⚠ MANUAL ALWAYS BEATS AUTOMATIC — `hasManualDiscount` stops both sources. That is the no-stacking
 * rule, unchanged, and it is why the caller must pass the OPERATOR's discounts only: if the
 * resolver's own output counted, the second scan would find every line ineligible and the discount
 * would silently vanish from the basket.
 */
export const autoDiscountForLine = (
  line: DiscountableLine,
  member: MemberStanding,
  rules: ScheduledDiscount[] | null | undefined,
  now: Date,
): AutoDiscount | null => {
  // ⚠ A fee is not shopping, and a refund gives back what was actually paid. Both are settled before
  // either source is asked, so neither can reach them by a different route.
  if (line.isCardSurcharge || line.isReturn || line.hasManualDiscount || line.isGiftCard) return null;

  const memberPence = memberApplies(member)
    ? discountPence(DISCOUNT_PERCENT, member.autoDiscountRate, line.unitIncPence, line.quantity)
    : 0;

  const candidates = candidatesFor(rules, line, now);
  const bestPence = candidates.length > 0 ? candidates[0].pence : 0;

  if (memberPence <= 0 && bestPence <= 0) return null;

  // ⚠ `>=` is the tie rule stated above: equal money keeps the member's badge.
  if (memberPence >= bestPence) {
    const name = memberLabel(member.tierName, member.autoDiscountRate);
    return {
      discountId: MEMBER_DISCOUNT_ID,
      name,
      type: DISCOUNT_PERCENT,
      amount: member.autoDiscountRate,
      pence: memberPence,
      reason: name,
    };
  }

  const { rule } = candidates[0];
  const name = ruleLabel(rule.name);
  return {
    discountId: rule.id,
    name,
    type: rule.type,
    // ⚠ `LineDiscount.amount` on this till means POUNDS for a fixed discount and a FRACTION for a
    // percentage — the existing basket contract, unchanged. The wire carries pence, so the one
    // conversion happens here rather than being spread through the reducer.
    amount: rule.type === DISCOUNT_PERCENT ? rule.percentFraction : rule.fixedAmountPence / 100,
    pence: bestPence,
    reason: name,
  };
};
