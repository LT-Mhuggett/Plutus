import { describe, expect, it } from "vitest";
import { exFromInc } from "./priceAdjust.ts";

/**
 * ⚠⚠ THE SAME VECTORS AS `tests/Plutus.Tests.Unit/PriceAdjustTests.cs`. This is the TypeScript half of
 * a C2 twin, and the rule is a VAT rule: the operator types the inc-VAT price, the ex half is derived
 * from the catalogue proportion, and the sale line's declared rate comes from that pair.
 *
 * ⚠ MAUI asked for ex AND inc in one dialog and wrote both, so a mistyped pair declared 5% VAT on a
 * 20% item. That is the fault these vectors exist to keep fixed on both tills.
 */

const standard = { incPence: 1200, exPence: 1000 };   // £12.00 inc / £10.00 ex — 20%
const zeroRated = { incPence: 1000, exPence: 1000 };  // ex == inc — no VAT

describe("exFromInc", () => {
  it("keeps 20% on a standard-rated item", () => {
    expect(exFromInc(600, standard)).toBe(500);
  });

  it("⚠ keeps a zero-rated item zero-rated", () => {
    // The one a separate ex field gets wrong: an override must not acquire VAT because somebody
    // typed into two boxes.
    expect(exFromInc(750, zeroRated)).toBe(750);
  });

  it("⚠⚠ multiplies before dividing — the vector that only THIS language can catch", () => {
    // 45 × 70 ÷ 100 is exactly 31.5 → 32. Ratio-first is `45 × 0.7` = 31.499999999999996 → 31,
    // because a double cannot hold 70/100. ⚠ The .NET twin's mutation run showed ratio-first
    // SURVIVING there (decimal carries enough digits), so this vector is the whole reason the rule
    // is products-first — and the reason the mutation has to be run in both languages.
    expect(exFromInc(45, { incPence: 100, exPence: 70 })).toBe(32);
  });

  it("rounds away from zero at an exact half", () => {
    // 5 × 50 ÷ 100 = 2.5 → 3. Banker's rounding would say 2.
    expect(exFromInc(5, { incPence: 100, exPence: 50 })).toBe(3);
  });

  it("does not drift when the same line is adjusted twice", () => {
    // Derived from the CATALOGUE both times, never from the line's current pair.
    expect(exFromInc(999, standard)).toBe(833);   // 832.5 → 833
    expect(exFromInc(999, standard)).toBe(exFromInc(999, standard));
  });

  it.each([0, -100])("gives ex == inc when the catalogue has no usable inc price (%i)", (incPence) => {
    expect(exFromInc(500, { incPence, exPence: 400 })).toBe(500);
  });

  it("⚠ clamps an impossible catalogue pair rather than propagating it", () => {
    // ex above inc would declare NEGATIVE VAT; ex of zero or less is not a price. Both mean the
    // catalogue row is wrong, and neither may reach a VAT return through here.
    expect(exFromInc(500, { incPence: 1000, exPence: 1200 })).toBe(500);
    expect(exFromInc(500, { incPence: 1000, exPence: 0 })).toBe(0);
  });

  it("a zero override is zero on both halves", () => {
    expect(exFromInc(0, standard)).toBe(0);
  });
});
