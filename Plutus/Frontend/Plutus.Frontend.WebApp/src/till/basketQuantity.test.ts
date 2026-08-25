import { describe, expect, it } from "vitest";
import { reduceBasket, type BasketLine, type BasketState } from "./basket.ts";

/**
 * The reducer's half of typing a quantity — `setQuantity` (2026-08-25).
 *
 * ⚠ Deliberately NOT called `basket.test.ts`. `basketMerge.test.ts`'s header claims *"`basket.test.ts`
 * covers the reducer's behaviour end to end"* and **there is no such file** — the reducer has never had
 * an end-to-end test. This file does not pretend to be it; it covers the one case being added, and the
 * gap is recorded in `Platform Gaps.md` rather than papered over with a reassuring filename.
 *
 * ⚠⚠ WHY THE REDUCER AND NOT JUST THE RULE. `quantityEntry.ts` decides what the operator MEANT;
 * this decides what the basket DOES. The dangerous half is here: a quantity of 0 must remove the line,
 * and it must remove **only that** line.
 */

const item = (idOne: string, name: string) =>
  ({ idOne, name, brand: "", desc: "", cost: 0, price: 5, exPrice: 4.17, taxId: 1, catId: "c1" }) as unknown as BasketLine["item"];

const line = (key: number, idOne: string, quantity: number): BasketLine => ({
  key,
  item: item(idOne, `item ${idOne}`),
  quantity,
  pricePence: 500,
  exPricePence: 417,
  adjusted: false,
});

const state = (...lines: BasketLine[]): BasketState => ({ lines, nextKey: 99 });

describe("setQuantity", () => {
  it("sets an absolute quantity, not a delta", () => {
    const s = reduceBasket(state(line(1, "A", 3)), { type: "setQuantity", key: 1, quantity: 7 });
    expect(s.lines[0].quantity).toBe(7);
  });

  it("can go DOWN as well as up — the point of typing over it", () => {
    const s = reduceBasket(state(line(1, "A", 12)), { type: "setQuantity", key: 1, quantity: 2 });
    expect(s.lines[0].quantity).toBe(2);
  });

  it("⚠ 0 removes the line, exactly as a delta down to 0 does", () => {
    const viaTyping = reduceBasket(state(line(1, "A", 1)), { type: "setQuantity", key: 1, quantity: 0 });
    const viaMinus = reduceBasket(state(line(1, "A", 1)), { type: "quantity", key: 1, delta: -1 });
    expect(viaTyping.lines).toHaveLength(0);
    // ⚠ The two routes must agree — one `> 0` filter in the reducer is what guarantees it.
    expect(viaTyping.lines).toEqual(viaMinus.lines);
  });

  it("⚠ removes ONLY the line addressed, and leaves the others untouched", () => {
    const s = reduceBasket(state(line(1, "A", 2), line(2, "B", 4), line(3, "C", 6)), {
      type: "setQuantity", key: 2, quantity: 0,
    });
    expect(s.lines.map((l) => l.key)).toEqual([1, 3]);
    expect(s.lines.map((l) => l.quantity)).toEqual([2, 6]);
  });

  it("touches no other line when setting a quantity", () => {
    const s = reduceBasket(state(line(1, "A", 2), line(2, "B", 4)), {
      type: "setQuantity", key: 1, quantity: 9,
    });
    expect(s.lines.map((l) => l.quantity)).toEqual([9, 4]);
  });

  it("ignores an unknown key rather than throwing", () => {
    const before = state(line(1, "A", 2));
    const s = reduceBasket(before, { type: "setQuantity", key: 404, quantity: 5 });
    expect(s.lines.map((l) => l.quantity)).toEqual([2]);
  });

  it("preserves everything else on the line — discount, return flag, adjusted price", () => {
    const l: BasketLine = {
      ...line(1, "A", 2),
      adjusted: true,
      isReturn: true,
      pricePence: 450,
      discount: { id: 7, name: "Staff", percent: 10 } as unknown as BasketLine["discount"],
    };
    const s = reduceBasket(state(l), { type: "setQuantity", key: 1, quantity: 5 });
    expect(s.lines[0]).toMatchObject({
      quantity: 5, adjusted: true, isReturn: true, pricePence: 450,
    });
    expect(s.lines[0].discount).toBeTruthy();
  });
});
