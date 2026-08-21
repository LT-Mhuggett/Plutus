import { describe, expect, it } from "vitest";
import { MEMBER_DISCOUNT_ID, NO_MEMBER, autoDiscountForLine, memberLabel, type MemberStanding } from "./autoDiscounts.ts";
import { DISCOUNT_PERCENT, type DiscountableLine, type ScheduledDiscount } from "./scheduledDiscounts.ts";

/**
 * ⚠⚠ THE TYPESCRIPT HALF OF THE C2 PAIR — `AutoDiscountsTests.cs` runs the same vectors in .NET.
 *
 * The case this file exists for is "both apply": a Gold member buying Warhammer on a Wednesday must be
 * charged the same on both tills. Without one shared resolver each till decides for itself, and the
 * two answers differ by real money on a basket nothing downstream can flag.
 */

const WARHAMMER = "11111111-1111-1111-1111-111111111111";
const PAINT = "22222222-2222-2222-2222-222222222222";
const WEDNESDAYS = 1 << 3;

const wedAfternoon = new Date(2026, 7, 19, 14, 0, 0);
const thuAfternoon = new Date(2026, 7, 20, 14, 0, 0);

const rule = (percentFraction = 0.1, id = 7): ScheduledDiscount => ({
  id,
  name: "Wednesday Warhammer",
  type: DISCOUNT_PERCENT,
  percentFraction,
  fixedAmountPence: 0,
  autoApply: true,
  allApplicable: false,
  daysOfWeekMask: WEDNESDAYS,
  categoryIds: [WARHAMMER],
});

const line = (over: Partial<DiscountableLine> = {}): DiscountableLine => ({
  unitIncPence: 500,
  quantity: 1,
  categoryId: WARHAMMER,
  itemIdOne: "SPACEMARINE-01",
  isReturn: false,
  hasManualDiscount: false,
  isGiftCard: false,
  isCardSurcharge: false,
  ...over,
});

const gold = (rate = 0.1): MemberStanding => ({
  hasMembership: true, expired: false, autoDiscountRate: rate, tierName: "Gold",
});

describe("nothing applies", () => {
  it("takes nothing on an anonymous sale with no live rule", () => {
    expect(autoDiscountForLine(line(), NO_MEMBER, [rule()], thuAfternoon)).toBeNull();
  });

  it("takes nothing on a line outside the rule's categories", () => {
    expect(autoDiscountForLine(line({ categoryId: PAINT }), NO_MEMBER, [rule()], wedAfternoon)).toBeNull();
  });
});

describe("one source at a time", () => {
  it("applies a live rule and carries the rule's REAL id", () => {
    const got = autoDiscountForLine(line(), NO_MEMBER, [rule()], wedAfternoon);
    expect(got).not.toBeNull();
    expect(got!.pence).toBe(50);
    expect(got!.discountId).toBe(7);
    expect(got!.name).toBe("Wednesday Warhammer");
  });

  /** ⚠ The tier discount keeps the SENTINEL id (0). That is what holds it off `LineMeta.discounts[]`,
   *  whose legacy-bridge projection is keyed on a real `DiscountId` and would FK-fail the sale. */
  it("applies a member alone and carries the SENTINEL id", () => {
    const got = autoDiscountForLine(line(), gold(), null, wedAfternoon);
    expect(got).not.toBeNull();
    expect(got!.pence).toBe(50);
    expect(got!.discountId).toBe(MEMBER_DISCOUNT_ID);
    expect(got!.name).toBe("Gold 10%");
  });

  it("grants nothing for an expired membership", () => {
    const expired: MemberStanding = { hasMembership: true, expired: true, autoDiscountRate: 0.1, tierName: "Gold" };
    expect(autoDiscountForLine(line(), expired, null, thuAfternoon)).toBeNull();
  });
});

describe("decision D2 — when both apply", () => {
  /**
   * ⚠⚠ THE REASON THIS MODULE EXISTS. A Gold member (10%) buying Warhammer on a Wednesday when the
   * rule is 20%: the line takes 20%, ONCE. Never both — the till is structurally one-discount-per-line
   * — and never the smaller of the two promises the shop made.
   */
  it("takes the LARGER and lands only one", () => {
    const got = autoDiscountForLine(line(), gold(0.1), [rule(0.2)], wedAfternoon);
    expect(got!.pence).toBe(100);        // 20% of £5, not 30%
    expect(got!.discountId).toBe(7);     // the rule won
  });

  it("keeps the member when the member is worth more", () => {
    const got = autoDiscountForLine(line(), gold(0.2), [rule(0.1)], wedAfternoon);
    expect(got!.pence).toBe(100);
    expect(got!.discountId).toBe(MEMBER_DISCOUNT_ID);
    expect(got!.name).toBe("Gold 20%");
  });

  /** ⚠ EQUAL MONEY KEEPS THE MEMBER'S BADGE. The choice is free, so it is fixed rather than left to
   *  comparison order — the tier discount is the one the customer is told about at the counter. */
  it("gives a tie to the member", () => {
    const got = autoDiscountForLine(line(), gold(0.1), [rule(0.1)], wedAfternoon);
    expect(got!.pence).toBe(50);
    expect(got!.discountId).toBe(MEMBER_DISCOUNT_ID);
  });

  it("takes the biggest of several live rules", () => {
    const got = autoDiscountForLine(line(), NO_MEMBER, [rule(0.05, 3), rule(0.15, 4), rule(0.1, 5)], wedAfternoon);
    expect(got!.pence).toBe(75);
    expect(got!.discountId).toBe(4);
  });
});

describe("manual always wins, and the resolver must not eat its own output", () => {
  it("stops both sources when the operator has discounted the line", () => {
    expect(autoDiscountForLine(line({ hasManualDiscount: true }), gold(), [rule()], wedAfternoon)).toBeNull();
  });

  /**
   * ⚠⚠ THE RE-ENTRANCY TRAP, PINNED. The resolver runs again on every basket change and
   * `hasManualDiscount` means the OPERATOR's discounts only. If a caller passed "this line has any
   * discount at all" — including the one the resolver just wrote — the second scan would find every
   * line ineligible and the discount would silently vanish from the basket.
   */
  it("still answers for a line it has already discounted", () => {
    const first = autoDiscountForLine(line(), NO_MEMBER, [rule()], wedAfternoon);
    const again = autoDiscountForLine(line(), NO_MEMBER, [rule()], wedAfternoon);
    expect(first).not.toBeNull();
    expect(again).toEqual(first);
  });
});

describe("the exclusions, settled before either source is asked", () => {
  it("never discounts a return", () => {
    expect(autoDiscountForLine(line({ isReturn: true }), gold(), [rule()], wedAfternoon)).toBeNull();
  });

  it("never discounts a gift card", () => {
    expect(autoDiscountForLine(line({ isGiftCard: true }), gold(), [rule()], wedAfternoon)).toBeNull();
  });

  /** ⚠ The exclusion the shared member rule does NOT carry, so it is settled in the resolver — which is
   *  how both tills stop holding their own copy of it. */
  it("never discounts the card surcharge", () => {
    expect(autoDiscountForLine(line({ isCardSurcharge: true }), gold(), [rule()], wedAfternoon)).toBeNull();
  });
});

describe("the tier label", () => {
  /** ⚠ The twin of `MemberDiscount.Label`, and of the string `TillPage` used to build inline. A till
   *  that wrote "Gold member discount" would put a different word on the receipt for the same money. */
  it("reads as the tier and its whole-number rate", () => {
    expect(memberLabel("Gold", 0.1)).toBe("Gold 10%");
    expect(memberLabel(null, 0.1)).toBe("10%");
  });

  /** ⚠ 12.5% ROUNDS TO 13, NOT 12 — the vector that discriminates. `Math.round` is half-up and the
   *  .NET twin asks for `AwayFromZero` explicitly; .NET's DEFAULT (banker's) would answer 12 and put a
   *  different rate on the receipt for the same tier. A rate of 10% cannot tell those apart. */
  it("rounds a midpoint rate away from zero, not to even", () => {
    expect(memberLabel("Silver", 0.125)).toBe("Silver 13%");
  });
});
