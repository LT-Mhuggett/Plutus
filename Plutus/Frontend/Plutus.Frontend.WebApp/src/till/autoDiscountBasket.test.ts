import { describe, expect, it } from "vitest";
import { AD_HOC_DISCOUNT_ID, lineDiscountPence, reduceBasket, type BasketLine, type BasketState } from "./basket.ts";
import { MEMBER_DISCOUNT_ID, NO_MEMBER, type MemberStanding } from "./autoDiscounts.ts";
import { DISCOUNT_PERCENT, type ScheduledDiscount } from "./scheduledDiscounts.ts";
import type { Item } from "../api.ts";

/**
 * The auto-discount rules AS THE REDUCER ACTUALLY APPLIES THEM.
 *
 * ⚠⚠ THIS FILE EXISTS BECAUSE THE UNIT-LEVEL VECTORS CANNOT SEE THE TWO FAULTS THAT MATTER MOST.
 * `autoDiscounts.test.ts` proves the resolver picks the right discount for one line; it says nothing
 * about whether the basket KEEPS it, or whether an operator can get rid of it. Both of those are
 * properties of the reducer, and both fail in a way an operator would see immediately and a unit test
 * never would:
 *
 *   1. the resolver re-running and treating its OWN output as "already discounted", so the discount
 *      vanishes the moment a second item is scanned;
 *   2. the resolver putting a discount straight back after the operator deliberately removed it.
 */

const WARHAMMER = "11111111-1111-1111-1111-111111111111";
const WEDNESDAYS = 1 << 3;
const wedMs = new Date(2026, 7, 19, 14, 0, 0).getTime();
const thuMs = new Date(2026, 7, 20, 14, 0, 0).getTime();

const rule: ScheduledDiscount = {
  id: 7, name: "Wednesday Warhammer", type: DISCOUNT_PERCENT, percentFraction: 0.1, fixedAmountPence: 0,
  autoApply: true, allApplicable: false, daysOfWeekMask: WEDNESDAYS, categoryIds: [WARHAMMER],
};

const gold: MemberStanding = { hasMembership: true, expired: false, autoDiscountRate: 0.1, tierName: "Gold" };

/** A £5.00 Warhammer item. Only the fields the discount path reads are meaningful. */
const item = (idOne: string, catId = WARHAMMER): Item => ({
  idOne, name: `Item ${idOne}`, brand: "-", desc: "", cost: 0,
  exPrice: 4.17, price: 5, taxId: 1, catId,
} as Item);

const line = (key: number, idOne: string, over: Partial<BasketLine> = {}): BasketLine => ({
  key, item: item(idOne), quantity: 1, pricePence: 500, exPricePence: 417, adjusted: false, ...over,
});

const basket = (...lines: BasketLine[]): BasketState => ({ lines, nextKey: lines.length + 1 });

const runAuto = (state: BasketState, member = NO_MEMBER, rules = [rule], nowMs = wedMs) =>
  reduceBasket(state, { type: "autoDiscounts", member, rules, nowMs });

describe("a scheduled rule lands on the basket", () => {
  it("discounts the lines it targets and leaves the others", () => {
    const after = runAuto(basket(line(1, "SM-01"), line(2, "PAINT-01", { item: item("PAINT-01", "22222222-2222-2222-2222-222222222222") })));

    expect(after.lines[0].discount?.discountId).toBe(7);
    expect(lineDiscountPence(after.lines[0])).toBe(50);
    expect(after.lines[1].discount).toBeUndefined();
  });

  it("takes nothing on a day the rule is not live", () => {
    const after = runAuto(basket(line(1, "SM-01")), NO_MEMBER, [rule], thuMs);
    expect(after.lines[0].discount).toBeUndefined();
  });
});

describe("the resolver does not eat its own output", () => {
  /**
   * ⚠⚠ THE FAULT THIS PINS. The resolver skips a line that "already has a discount" — that is the
   * no-stacking rule. If it could not tell its OWN discount from the operator's, then scanning a
   * second item (which re-runs it) would find line 1 discounted, skip it, and the discount would
   * still be there... until anything recomputed it. Run it twice and the answer must not move.
   */
  it("survives being run again, and again", () => {
    const once = runAuto(basket(line(1, "SM-01")));
    const twice = runAuto(once);
    const thrice = runAuto(twice);

    expect(lineDiscountPence(twice.lines[0])).toBe(50);
    expect(lineDiscountPence(thrice.lines[0])).toBe(50);
    expect(twice.lines[0].discount).toEqual(thrice.lines[0].discount);
  });

  it("re-prices when a bigger rule arrives", () => {
    const before = runAuto(basket(line(1, "SM-01")));
    expect(lineDiscountPence(before.lines[0])).toBe(50);

    const after = runAuto(before, NO_MEMBER, [rule, { ...rule, id: 9, name: "Better", percentFraction: 0.2 }]);
    expect(lineDiscountPence(after.lines[0])).toBe(100);
    expect(after.lines[0].discount?.discountId).toBe(9);
  });

  /** ⚠ Detaching a member re-runs this with nobody attached — there is no separate clear-down action
   *  any more, so "the member left" and "the rule expired" take one code path. */
  it("takes the tier discount off when the member is detached", () => {
    const attached = runAuto(basket(line(1, "SM-01")), gold, []);
    expect(attached.lines[0].discount?.discountId).toBe(MEMBER_DISCOUNT_ID);

    const detached = runAuto(attached, NO_MEMBER, []);
    expect(detached.lines[0].discount).toBeUndefined();
  });
});

describe("the operator always wins", () => {
  it("never overwrites a discount the operator applied", () => {
    const manual = reduceBasket(basket(line(1, "SM-01")), {
      type: "applyAdHocDiscount", kind: 1, amount: 0.5, label: "50% off",
      keys: [1], reason: "damaged box",
    });
    expect(lineDiscountPence(manual.lines[0])).toBe(250);

    // The rule is worth only 50p, but that is not why it must not apply — a manual discount wins
    // whatever it is worth.
    const after = runAuto(manual, gold, [{ ...rule, percentFraction: 0.9 }]);
    expect(after.lines[0].discount?.discountId).toBe(AD_HOC_DISCOUNT_ID);
    expect(lineDiscountPence(after.lines[0])).toBe(250);
  });

  /**
   * ⚠⚠ "YOU CAN ALWAYS CHARGE FULL PRICE" — and without `autoWaived` that promise lasts exactly
   * until the next scan. Removing an automatic discount has to stick, or the operator watches it
   * reappear with no way to stop it.
   */
  it("keeps an automatic discount OFF once the operator has removed it", () => {
    const discounted = runAuto(basket(line(1, "SM-01")));
    expect(discounted.lines[0].discount).toBeDefined();

    const cleared = reduceBasket(discounted, { type: "clearDiscount", key: 1 });
    expect(cleared.lines[0].discount).toBeUndefined();
    expect(cleared.lines[0].autoWaived).toBe(true);

    // Scanning another item re-runs the resolver over the whole basket.
    const rerun = runAuto(cleared);
    expect(rerun.lines[0].discount).toBeUndefined();
  });

  /** ⚠ Clearing a MANUAL discount does not waive anything — nothing re-applies those, and treating it
   *  as a waiver would stop a rule ever landing on a line whose staff discount was cancelled. */
  it("does not waive the automatic discount when a MANUAL one is cleared", () => {
    const manual = reduceBasket(basket(line(1, "SM-01")), {
      type: "applyAdHocDiscount", kind: 0, amount: 1, label: "£1.00 off", keys: [1], reason: "goodwill",
    });
    const cleared = reduceBasket(manual, { type: "clearDiscount", key: 1 });
    expect(cleared.lines[0].autoWaived).toBeFalsy();

    const after = runAuto(cleared);
    expect(after.lines[0].discount?.discountId).toBe(7);
  });
});

describe("a typed discount carries no catalogue id", () => {
  /**
   * ⚠⚠ IT MUST BE NEGATIVE, and checkout only puts a POSITIVE id on `LineMeta.discounts[]`. That
   * array projects into legacy `Transaction_Discount` rows keyed on a real `DiscountId`, so an
   * invented positive id would FK-fail the projection of the entire sale.
   */
  it("uses the ad-hoc sentinel, which is not a positive id", () => {
    const after = reduceBasket(basket(line(1, "SM-01")), {
      type: "applyAdHocDiscount", kind: 1, amount: 0.1, label: "10% off", keys: [1], reason: "damaged box",
    });

    expect(after.lines[0].discount?.discountId).toBe(AD_HOC_DISCOUNT_ID);
    expect(after.lines[0].discount!.discountId).toBeLessThan(1);
    expect(after.lines[0].discount?.reason).toBe("damaged box");
    // ⚠ Not marked `auto`: the operator typed it, so the resolver must leave it alone.
    expect(after.lines[0].discount?.auto).toBeFalsy();
  });

  it("never lands on a gift-card line", () => {
    const after = reduceBasket(basket(line(1, "GC-01", { giftCardCode: "G123" })), {
      type: "applyAdHocDiscount", kind: 1, amount: 0.1, label: "10% off", keys: [1], reason: "why not",
    });
    expect(after.lines[0].discount).toBeUndefined();
  });
});
