import { describe, expect, it } from "vitest";
import {
  DISCOUNT_FIXED,
  DISCOUNT_PERCENT,
  candidatesFor,
  forLine,
  isLiveAt,
  isWellFormed,
  label,
  targets,
  type DiscountableLine,
  type ScheduledDiscount,
} from "./scheduledDiscounts.ts";

/**
 * ⚠⚠ THE TYPESCRIPT HALF OF THE C2 PAIR — `ScheduledDiscountsTests.cs` runs the SAME VECTORS in .NET.
 * Two tills that disagree about when "Wednesday Warhammer" is live charge different prices for the
 * same basket on the same day. **Add a case to one, add it to the other.**
 */

const WARHAMMER = "11111111-1111-1111-1111-111111111111";
const PAINT = "22222222-2222-2222-2222-222222222222";
const WEDNESDAYS = 1 << 3; // bit 0 = Sunday, so Wednesday is bit 3

// Wednesday 2026-08-19 14:00 LOCAL, and the Thursday after it.
const wedAfternoon = new Date(2026, 7, 19, 14, 0, 0);
const thuAfternoon = new Date(2026, 7, 20, 14, 0, 0);

const rule = (over: Partial<ScheduledDiscount> = {}): ScheduledDiscount => ({
  id: 7,
  name: "Wednesday Warhammer",
  type: DISCOUNT_PERCENT,
  percentFraction: 0.1,
  fixedAmountPence: 0,
  autoApply: true,
  allApplicable: false,
  daysOfWeekMask: WEDNESDAYS,
  categoryIds: [WARHAMMER],
  ...over,
});

/** A £5.00 Warhammer line, quantity 1. */
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

describe("the schedule", () => {
  it("is live on a Wednesday and not on a Thursday", () => {
    expect(isLiveAt(rule(), wedAfternoon)).toBe(true);
    expect(isLiveAt(rule(), thuAfternoon)).toBe(false);
  });

  /** ⚠ Bit 0 = Sunday, matching `getDay()` and .NET's `DayOfWeek`. A mask built Monday-first shifts
   *  every rule in the shop by one day, silently. */
  it("reads the day mask Sunday-first", () => {
    expect(isLiveAt(rule({ daysOfWeekMask: 1 << 0 }), new Date(2026, 7, 23, 12, 0, 0))).toBe(true);  // Sunday
    expect(isLiveAt(rule({ daysOfWeekMask: 1 << 0 }), new Date(2026, 7, 24, 12, 0, 0))).toBe(false); // Monday
    expect(isLiveAt(rule({ daysOfWeekMask: 1 << 6 }), new Date(2026, 7, 22, 12, 0, 0))).toBe(true);  // Saturday
    expect(isLiveAt(rule({ daysOfWeekMask: 1 << 1 }), new Date(2026, 7, 24, 12, 0, 0))).toBe(true);  // Monday
  });

  it("treats no day mask as every day", () => {
    expect(isLiveAt(rule({ daysOfWeekMask: null }), wedAfternoon)).toBe(true);
    expect(isLiveAt(rule({ daysOfWeekMask: null }), thuAfternoon)).toBe(true);
  });

  /**
   * ⚠⚠ INCLUSIVE AT BOTH ENDS, mirroring `PermissionGrant.IsActiveAt` — a 09:00–17:00 rule is live AT
   * 17:00:00. Pinned because the plausible "fix" is a half-open interval, which would end the discount
   * one second before the permission that governs it.
   */
  it("applies the time window in local time, inclusive at both ends", () => {
    const windowed = rule({ windowStartLocal: "09:00:00", windowEndLocal: "17:00:00" });
    expect(isLiveAt(windowed, new Date(2026, 7, 19, 8, 59, 0))).toBe(false);
    expect(isLiveAt(windowed, new Date(2026, 7, 19, 9, 0, 0))).toBe(true);
    expect(isLiveAt(windowed, new Date(2026, 7, 19, 17, 0, 0))).toBe(true);
    expect(isLiveAt(windowed, new Date(2026, 7, 19, 17, 1, 0))).toBe(false);
  });

  /** ⚠⚠ A window that wraps midnight matches NOTHING — copied from the permission twin deliberately
   *  rather than fixed here, because fixing one side alone would make a late-night discount behave
   *  differently from a late-night permission. */
  it("matches nothing for a window that wraps midnight", () => {
    const wrapped = rule({ daysOfWeekMask: null, windowStartLocal: "22:00:00", windowEndLocal: "02:00:00" });
    for (const h of [23, 1, 12]) {
      expect(isLiveAt(wrapped, new Date(2026, 7, 19, h, 0, 0))).toBe(false);
    }
  });

  it("bounds the rule by its UTC validity dates", () => {
    const dated = rule({
      daysOfWeekMask: null,
      validFromUtc: "2026-08-19T00:00:00Z",
      validToUtc: "2026-08-19T23:59:59Z",
    });
    expect(isLiveAt(dated, wedAfternoon)).toBe(true);
    expect(isLiveAt(dated, thuAfternoon)).toBe(false);
  });

  /**
   * ⚠⚠ THE TRAP THIS LANGUAGE HAS AND .NET DOES NOT. `Date.parse("2026-08-19T00:00:00")` — no `Z` —
   * reads as LOCAL time, so a server that serialised an unspecified-Kind DateTime would shift every
   * promotion boundary by the till's offset. A bare instant is treated as UTC.
   */
  it("reads a UTC instant that arrived without its Z as UTC anyway", () => {
    const withZ = rule({ daysOfWeekMask: null, validFromUtc: "2026-08-19T13:00:00Z" });
    const without = rule({ daysOfWeekMask: null, validFromUtc: "2026-08-19T13:00:00" });
    // 13:00Z is in the past at the moment under test either way; the two must agree.
    expect(isLiveAt(without, wedAfternoon)).toBe(isLiveAt(withZ, wedAfternoon));
  });
});

describe("targeting", () => {
  it("targets its category and nothing else", () => {
    expect(targets(rule(), WARHAMMER, "SPACEMARINE-01")).toBe(true);
    expect(targets(rule(), PAINT, "PAINT-01")).toBe(false);
  });

  it("targets its barcodes case-insensitively", () => {
    const byItem = rule({ categoryIds: null, itemIdOnes: ["SPACEMARINE-01"] });
    expect(targets(byItem, null, "SPACEMARINE-01")).toBe(true);
    expect(targets(byItem, null, "spacemarine-01")).toBe(true);
    expect(targets(byItem, null, "PAINT-01")).toBe(false);
  });

  /** The three targets are a UNION — naming one extra item must not stop a rule applying to the
   *  category it also names. */
  it("may target a category AND an extra item", () => {
    const both = rule({ itemIdOnes: ["PAINT-01"] });
    expect(targets(both, WARHAMMER, "SPACEMARINE-01")).toBe(true);
    expect(targets(both, PAINT, "PAINT-01")).toBe(true);
    expect(targets(both, PAINT, "BRUSH-01")).toBe(false);
  });

  it("targets everything when allApplicable, including an uncategorised item", () => {
    const all = rule({ allApplicable: true, categoryIds: null });
    expect(targets(all, null, null)).toBe(true);
    expect(targets(all, PAINT, "PAINT-01")).toBe(true);
  });
});

describe("what is not a rule at all", () => {
  /**
   * ⚠⚠ THE MONEY CASE ON THIS LIST. A rule that targets nothing must mean NOTHING, never EVERYTHING —
   * the carrier-bag precedent. A category list that failed to load would otherwise turn a category
   * promotion into a whole-basket one, on every sale, silently.
   */
  it("lands on nothing when it targets nothing", () => {
    const untargeted = rule({ allApplicable: false, categoryIds: null, itemIdOnes: null });
    expect(isWellFormed(untargeted)).toBe(false);
    expect(targets(untargeted, WARHAMMER, "SPACEMARINE-01")).toBe(false);
    expect(forLine(untargeted, line(), wedAfternoon)).toBe(0);
  });

  /** ⚠ A percentage outside 0–1 is refused rather than left to throw on the selling path with a
   *  customer waiting. The portal refuses to save one; this is the reader's half. */
  it("refuses a percentage outside 0 to 1", () => {
    for (const percentFraction of [1.5, 0, -0.1]) {
      expect(isWellFormed(rule({ percentFraction }))).toBe(false);
      expect(forLine(rule({ percentFraction }), line(), wedAfternoon)).toBe(0);
    }
  });

  /** The 0–1 ceiling is a PERCENTAGE rule; £1.50 off each unit is perfectly legal, and the two amounts
   *  are separate fields so the ceiling cannot leak across. */
  it("allows a fixed amount above one pound", () => {
    expect(isWellFormed(rule({ type: DISCOUNT_FIXED, percentFraction: 0, fixedAmountPence: 150 }))).toBe(true);
  });

  /** ⚠ An unrecognised KIND is not a discount — a later backend's third type must not be applied by a
   *  till that has no idea what it means. */
  it("refuses an unknown discount kind", () => {
    expect(isWellFormed(rule({ type: 99 }))).toBe(false);
  });

  /** ⚠ The two amount fields cannot cover for each other: a percentage rule carrying only a pence
   *  figure is incomplete, and applying it would take a fraction of nothing off. */
  it("refuses a kind whose own amount field is unset", () => {
    expect(isWellFormed(rule({ type: DISCOUNT_PERCENT, percentFraction: 0, fixedAmountPence: 150 }))).toBe(false);
    expect(isWellFormed(rule({ type: DISCOUNT_FIXED, percentFraction: 0.1, fixedAmountPence: 0 }))).toBe(false);
  });
});

describe("the money", () => {
  it("takes 10% off a £5.00 line as 50p", () => {
    expect(forLine(rule(), line(), wedAfternoon)).toBe(50);
  });

  /** ⚠ A fixed rule's amount is PER UNIT and multiplied by quantity: £1 off three of something is £3,
   *  not £1. ⚠ And the wire carries PENCE — 100, not 1.00 — so a reader cannot take a hundredth of the
   *  intended discount by guessing the unit. */
  it("takes its pence off every unit", () => {
    const fixed = rule({ type: DISCOUNT_FIXED, percentFraction: 0, fixedAmountPence: 100 });
    expect(forLine(fixed, line({ quantity: 3 }), wedAfternoon)).toBe(300);
  });

  it("takes nothing when the rule is not live", () => {
    expect(forLine(rule(), line(), thuAfternoon)).toBe(0);
  });
});

describe("the four exclusions, which are the members' discount's", () => {
  it("never discounts a return", () => {
    expect(forLine(rule(), line({ isReturn: true }), wedAfternoon)).toBe(0);
  });

  it("never stacks on a line the operator already discounted", () => {
    expect(forLine(rule(), line({ hasManualDiscount: true }), wedAfternoon)).toBe(0);
  });

  it("never discounts a gift card", () => {
    expect(forLine(rule(), line({ isGiftCard: true }), wedAfternoon)).toBe(0);
  });

  /** ⚠ A fee is not shopping: discounting the card surcharge makes the shop pass on less than the
   *  acquirer charges it. */
  it("never discounts the card surcharge", () => {
    expect(forLine(rule(), line({ isCardSurcharge: true }), wedAfternoon)).toBe(0);
  });
});

describe("candidates", () => {
  /** A catalogue entry an operator picks by hand must never apply itself — that is the whole
   *  difference between a rule and a list item. */
  it("ignores a rule that is not autoApply", () => {
    expect(candidatesFor([rule({ autoApply: false })], line(), wedAfternoon)).toEqual([]);
  });

  it("comes back biggest first", () => {
    const got = candidatesFor([rule(), rule({ percentFraction: 0.2, id: 9 })], line(), wedAfternoon);
    expect(got.map((c) => c.pence)).toEqual([100, 50]);
    expect(got[0].rule.id).toBe(9);
  });

  /** ⚠ Two rules worth the same money resolve by LOWEST ID so both tills show the same badge. Left to
   *  iteration order this would depend on JSON ordering. */
  it("breaks a tie on the lower id", () => {
    const got = candidatesFor([rule({ id: 9, name: "Later" }), rule({ id: 4, name: "Earlier" })], line(), wedAfternoon);
    expect(got[0].rule.id).toBe(4);
  });

  it("yields nothing for a null rule set", () => {
    expect(candidatesFor(null, line(), wedAfternoon)).toEqual([]);
  });
});

describe("the label", () => {
  /** ⚠ THE SHOP'S OWN NAME, with no rate appended — a shop that called its rule "Wednesday Warhammer
   *  10%" would otherwise get the rate twice, and the money off is already printed beside it. */
  it("is the rule name as the shop wrote it", () => {
    expect(label("Wednesday Warhammer")).toBe("Wednesday Warhammer");
    expect(label("  Wednesday Warhammer  ")).toBe("Wednesday Warhammer");
  });

  it("falls back to one shared word for a blank name", () => {
    expect(label(null)).toBe("Discount");
    expect(label("")).toBe("Discount");
    expect(label("   ")).toBe("Discount");
  });
});
