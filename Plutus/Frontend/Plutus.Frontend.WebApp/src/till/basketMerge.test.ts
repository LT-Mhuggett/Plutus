import { describe, expect, it } from "vitest";

/**
 * When a newly-added unit joins an existing basket line — the web till's half of the rule.
 *
 * ⚠⚠ THESE VECTORS ARE THE C2 PIN. The same table runs in `tests/Plutus.Tests.Unit/BasketMergeTests.cs`
 * against `SharedKernel.BasketMerge.CanMerge`. Two tills that disagree about when a line merges
 * disagree about **what the customer is charged** — and they did disagree until 2026-08-18, when
 * MAUI's selected-line fast path was found joining a second unit to a hand-adjusted line at the
 * adjusted price. **Add a case here and add it there, in the same commit.**
 *
 * ⚠ WHY THE PREDICATE IS RESTATED HERE RATHER THAN IMPORTED. The rule lives inside a `find(…)` inside
 * `addLine` in `basket.ts` and is not exported — extracting it would be the better fix, and until
 * somebody does, this test is what says the two tills agree. ⚠ That makes this file a **copy under
 * test**, so if the reducer's condition changes and this does not, these vectors go on passing while
 * the tills drift. `basket.test.ts` covers the reducer's behaviour end to end; this covers the rule.
 */

/** The predicate as `basket.ts`'s `addLine` applies it, plus the price-pair test MAUI applies. */
function canMerge(o: {
  sameItem: boolean;
  isReturn: boolean;
  adjusted: boolean;
  discounted: boolean;
  lineInc: number;
  lineEx: number;
  catInc: number;
  catEx: number;
}): boolean {
  if (!o.sameItem) return false;
  if (o.isReturn) return false;
  if (o.adjusted) return false;
  if (o.discounted) return false;
  return o.lineInc === o.catInc && o.lineEx === o.catEx;
}

// A £5.00 item at 20% VAT: 500 inc, 417 ex — the same figures as the C# vectors.
const INC = 500;
const EX = 417;

const merge = (over: Partial<Parameters<typeof canMerge>[0]> = {}) =>
  canMerge({
    sameItem: true,
    isReturn: false,
    adjusted: false,
    discounted: false,
    lineInc: INC,
    lineEx: EX,
    catInc: INC,
    catEx: EX,
    ...over,
  });

describe("basket merge — the C2 vectors", () => {
  it("merges the same item at the catalogue price", () => {
    expect(merge()).toBe(true);
  });

  it("never merges a different item", () => {
    expect(merge({ sameItem: false })).toBe(false);
  });

  // ⚠ Goods going back and goods going out are opposite directions of money.
  it("never adds a sale unit to a return line", () => {
    expect(merge({ isReturn: true })).toBe(false);
  });

  // ⚠⚠ The case MAUI got wrong: a second scan joined the adjusted line at the adjusted price.
  it("never adds a unit to an adjusted line", () => {
    expect(merge({ adjusted: true, lineInc: 50, lineEx: 42 })).toBe(false);
  });

  // ⚠⚠ And not even when the adjusted price equals the catalogue price — the flag is checked before
  // the prices, because otherwise adjusting a line to the figure it started at lets it re-merge.
  it("excludes an adjusted line by the flag, not by its price", () => {
    expect(merge({ adjusted: true })).toBe(false);
  });

  it("never adds a unit to a discounted line", () => {
    expect(merge({ discounted: true })).toBe(false);
  });

  it("does not merge a line whose price has moved off the catalogue", () => {
    expect(merge({ lineInc: 450, lineEx: 375 })).toBe(false);
  });

  // ⚠⚠ Both halves of the pair matter: the sale line's declared VAT rate is derived from the pair, so
  // merging rows that agree on inc and differ on ex puts two VAT answers on one row.
  it("checks the ex half even when the inc half agrees", () => {
    expect(merge({ lineEx: 400 })).toBe(false);
  });

  it("checks the inc half even when the ex half agrees", () => {
    expect(merge({ lineInc: 550 })).toBe(false);
  });

  // ⚠ Zero is a real price, not "unset".
  it("merges a genuinely free item with itself", () => {
    expect(merge({ lineInc: 0, lineEx: 0, catInc: 0, catEx: 0 })).toBe(true);
  });
});
