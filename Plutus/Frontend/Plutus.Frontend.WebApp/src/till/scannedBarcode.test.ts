import { describe, expect, it } from "vitest";
import { reduceBasket, type BasketState } from "./basket.ts";
import type { Item } from "../api.ts";

/**
 * Which barcode was actually scanned, when an item has more than one (multi-barcode, MB4/MB5).
 *
 * ⚠⚠ THE SAFETY PROPERTY IS THAT `item.idOne` STAYS CANONICAL. Pricing, line merging, the
 * deterministic item GUID and the sale line all key on it, and an alias reaching any of them would
 * create a phantom stock level and drop the line's VAT band — both silently. The scanned code is a
 * SNAPSHOT beside it, for the day a supplier's barcode migration goes wrong.
 *
 * ⚠ These drive the REAL reducer (`reduceBasket`), not a restatement of its rules — the weakness C2
 * records about `basketMerge.test.ts`.
 */

const item = (idOne: string): Item => ({
  idOne, name: `Item ${idOne}`, brand: "-", desc: "", cost: 0, exPrice: 4.17, price: 5,
  taxId: 1, catId: "",
} as Item);

const empty: BasketState = { lines: [], nextKey: 1 };

describe("recording the scanned barcode", () => {
  /** ⚠ The line keeps the CANONICAL code as its identity and the alias only as a note. */
  it("records an ADDITIONAL barcode beside the canonical one", () => {
    const after = reduceBasket(empty, { type: "add", item: item("5010"), scannedBarcode: "OLD-SUPPLIER" });

    expect(after.lines[0].item.idOne).toBe("5010");
    expect(after.lines[0].scannedBarcode).toBe("OLD-SUPPLIER");
  });

  /**
   * ⚠ Omitted when the scanned code WAS the item's own — almost every line. That is what keeps an
   * ordinary sale's checkout payload byte-identical to what it was before this field existed.
   */
  it("records nothing when the item's own barcode was scanned", () => {
    const after = reduceBasket(empty, { type: "add", item: item("5010"), scannedBarcode: "5010" });
    expect(after.lines[0].scannedBarcode).toBeUndefined();
  });

  it("records nothing when no code was passed at all", () => {
    const after = reduceBasket(empty, { type: "add", item: item("5010") });
    expect(after.lines[0].scannedBarcode).toBeUndefined();
  });

  /**
   * ⚠ A merged unit does NOT overwrite the line's snapshot. The line is one row on one receipt, and
   * its snapshot belongs to the scan that created it — a second unit arriving under a different code
   * cannot rewrite what the first one recorded, because there is only one field for two facts.
   */
  it("leaves an existing line's snapshot alone when a second unit merges in", () => {
    const first = reduceBasket(empty, { type: "add", item: item("5010"), scannedBarcode: "ALIAS-A" });
    const second = reduceBasket(first, { type: "add", item: item("5010"), scannedBarcode: "ALIAS-B" });

    expect(second.lines).toHaveLength(1);          // merged, as it always did
    expect(second.lines[0].quantity).toBe(2);
    expect(second.lines[0].scannedBarcode).toBe("ALIAS-A");
  });

  /** ⚠ And an alias scan must not change WHICH line a unit merges into — merging keys on `idOne`
   *  (plus not-adjusted, not-discounted), and the alias is invisible to it. */
  it("merges by the canonical code, whichever alias was scanned", () => {
    const first = reduceBasket(empty, { type: "add", item: item("5010") });
    const second = reduceBasket(first, { type: "add", item: item("5010"), scannedBarcode: "ALIAS" });

    expect(second.lines).toHaveLength(1);
    expect(second.lines[0].quantity).toBe(2);
  });
});
