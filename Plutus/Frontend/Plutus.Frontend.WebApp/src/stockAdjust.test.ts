import { describe, expect, it } from "vitest";
import { adjustedMessage, amountHint, buildStockMovement, isUsableAmount } from "./stockAdjust.ts";

/**
 * The web till's "Adjust stock…" rule.
 *
 * ⚠ The .NET twin is `StockMovementClientTests`, over MAUI's `ExecuteAdjustStock` +
 * `PostStockMovementAsync`. Neither suite covers the other; the SERVER is what actually stops the two
 * drifting, because it refuses a positive `WriteOff`, a zero qty and a blank reason by name.
 *
 * ⚠⚠ THE VECTORS THAT EARN THIS FILE are the `parseInt` ones. `parseInt("2.5")` is 2 and
 * `parseInt("3 apples")` is 3 — so the obvious implementation writes off a number the operator never
 * typed, tells them it worked, and the ledger takes it. MAUI's `int.TryParse` refuses both, and these
 * tests are what keep this side refusing them too.
 */

describe("isUsableAmount", () => {
  it("takes a plain whole number", () => {
    expect(isUsableAmount("3")).toBe(true);
    expect(isUsableAmount(" 12 ")).toBe(true);
  });

  it("refuses nothing, zero and a negative", () => {
    expect(isUsableAmount("")).toBe(false);
    expect(isUsableAmount("   ")).toBe(false);
    expect(isUsableAmount("0")).toBe(false);
    expect(isUsableAmount("-3")).toBe(false);
  });

  // ⚠⚠ THE ONES THAT MATTER — every one of these is a number `parseInt` would have accepted.
  it("refuses a decimal rather than silently truncating it", () => {
    expect(isUsableAmount("2.5")).toBe(false);
    expect(isUsableAmount("0.9")).toBe(false);
  });

  it("refuses digits with anything stuck to them", () => {
    expect(isUsableAmount("3 apples")).toBe(false);
    expect(isUsableAmount("3x")).toBe(false);
    expect(isUsableAmount("1e3")).toBe(false);
    expect(isUsableAmount("+4")).toBe(false);
  });
});

describe("buildStockMovement", () => {
  it("a write-off is NEGATIVE, and the operator never typed a sign", () => {
    const r = buildStockMovement("writeOff", "4", "damaged in transit");
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.movement).toEqual({ type: "WriteOff", qty: -4, reason: "damaged in transit" });
  });

  it("adding is POSITIVE and an Adjustment, not a Receipt", () => {
    // ⚠ `Receipt` is goods-in and the server does not require a reason for it; a till correction is
    // an Adjustment precisely so the reason stays compulsory.
    const r = buildStockMovement("add", "2", "found behind the counter");
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.movement).toEqual({ type: "Adjustment", qty: 2, reason: "found behind the counter" });
  });

  it("trims the amount and the reason", () => {
    const r = buildStockMovement("add", " 7 ", "  delivery not booked in  ");
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.movement.qty).toBe(7);
    expect(r.movement.reason).toBe("delivery not booked in");
  });

  it("refuses a blank reason instead of letting the server say so", () => {
    const r = buildStockMovement("writeOff", "1", "   ");
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.problem).toContain("Say why");
    expect(r.problem).toContain("Nothing has been changed");
  });

  it("refuses a bad amount and says nothing has changed", () => {
    for (const bad of ["", "0", "-2", "2.5", "abc"]) {
      const r = buildStockMovement("writeOff", bad, "a reason");
      expect(r.ok).toBe(false);
      if (r.ok) return;
      expect(r.problem).toContain("Nothing has been changed");
    }
  });

  it("checks the amount BEFORE the reason, so one refusal names one fault", () => {
    const r = buildStockMovement("add", "nope", "");
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.problem).toContain("whole number");
  });
});

describe("amountHint", () => {
  // ⚠⚠ This sentence is a guard, not decoration: the server ADDS the delta, so an operator who reads
  // the box as "the new total" doubles the stock and nothing errors.
  it("names the current figure and says it is not the new total", () => {
    const h = amountHint("Batman Year One", 7);
    expect(h).toContain("7 in stock");
    expect(h).toContain("not the new total");
  });

  it("says so plainly when there is no stock record yet", () => {
    expect(amountHint("Bag", null)).toContain("no stock record yet");
  });
});

describe("adjustedMessage", () => {
  it("says which way it went", () => {
    expect(adjustedMessage("writeOff", 3, "Mug")).toContain("Wrote off 3");
    expect(adjustedMessage("add", 3, "Mug")).toContain("Added 3");
  });
});
