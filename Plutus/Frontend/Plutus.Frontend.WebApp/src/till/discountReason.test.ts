import { describe, expect, it } from "vitest";
import { MAX_DISCOUNT_REASON, normaliseReason } from "./basket.ts";

/**
 * Binding default 22(c) — "All discounts need to be tracked — till, logged-in employee and reason."
 *
 * ⚠⚠ THIS IS HALF OF A C2 TWIN. `SharedKernel.DiscountAudit.NormaliseReason` is the other half, and
 * the two must agree character for character: the reason rides in the line's `discountsJson`, so two
 * tills normalising differently write the same operator's words two different ways and a report
 * grouped by reason splits by which counter the sale was rung on.
 *
 * ⚠ The cases below are deliberately the SAME cases as `DiscountAuditTests`. If you add one here,
 * add it there — a twin pinned on one side only is not pinned.
 */
describe("discount reason normalisation (C2 twin of DiscountAudit.NormaliseReason)", () => {
  it("treats blank, empty and whitespace-only as no reason at all", () => {
    // ⚠ The whitespace case is the one that matters: "   " is present to a query and blank to a
    // human, which is the empty column this ruling exists to prevent — while reporting full coverage.
    expect(normaliseReason(undefined)).toBeUndefined();
    expect(normaliseReason(null)).toBeUndefined();
    expect(normaliseReason("")).toBeUndefined();
    expect(normaliseReason("   ")).toBeUndefined();
    expect(normaliseReason("\t\r\n ")).toBeUndefined();
  });

  it("trims, so the stored value is the words and nothing else", () => {
    expect(normaliseReason("  damaged box  ")).toBe("damaged box");
  });

  it("collapses interior whitespace so two spellings of one reason group together", () => {
    expect(normaliseReason("damaged    box")).toBe("damaged box");
    expect(normaliseReason("damaged\tbox")).toBe("damaged box");
    expect(normaliseReason("staff  discount\nfor   Jo")).toBe("staff discount for Jo");
  });

  it("truncates an overlong reason rather than refusing it", () => {
    // ⚠ Refusing at a counter because somebody pasted too much would lose the whole discount over
    // something nobody can fix quickly. The cap exists only because a basket-wide discount repeats
    // this string on every line it apportions onto.
    const long = "x".repeat(MAX_DISCOUNT_REASON + 50);
    expect(normaliseReason(long)).toHaveLength(MAX_DISCOUNT_REASON);
  });

  it("agrees with the .NET rule's cap", () => {
    // ⚠ `DiscountAudit.MaxReasonLength` is 200. A drift here is silent and permanent.
    expect(MAX_DISCOUNT_REASON).toBe(200);
  });
});
