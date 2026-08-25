import { describe, expect, it } from "vitest";
import { commitQuantity, isQuantityDraft } from "./quantityEntry.ts";

/**
 * Typing a quantity into the till.
 *
 * ⚠⚠ THIS IS A MONEY PATH. Quantity multiplies price, so a wrong answer here is charged to a
 * customer. The vectors that earn the file are the ones `parseInt` would have accepted — `"2.5"`
 * becomes 2 and `"3x"` becomes 3 — and the empty box, which read as a number is 0 and on a basket
 * line means **delete the line**.
 */

describe("commitQuantity — a basket line (0 may remove it)", () => {
  const commit = (s: string) => commitQuantity(s, true);

  it("takes a plain whole number", () => {
    expect(commit("3")).toEqual({ action: "set", quantity: 3 });
    expect(commit(" 12 ")).toEqual({ action: "set", quantity: 12 });
  });

  it("treats an explicit 0 as removing the line, like − at quantity 1", () => {
    expect(commit("0")).toEqual({ action: "remove" });
  });

  it("⚠ REVERTS on an empty box rather than removing the line", () => {
    // The one that matters: clearing the box to retype must not delete what you were editing.
    expect(commit("")).toEqual({ action: "revert" });
    expect(commit("   ")).toEqual({ action: "revert" });
  });

  it("⚠ refuses a decimal instead of silently truncating it", () => {
    expect(commit("2.5")).toEqual({ action: "revert" });
    expect(commit("0.9")).toEqual({ action: "revert" });
  });

  it("⚠ refuses digits with anything stuck to them", () => {
    expect(commit("3x")).toEqual({ action: "revert" });
    expect(commit("3 apples")).toEqual({ action: "revert" });
    expect(commit("1e3")).toEqual({ action: "revert" });
    expect(commit("+4")).toEqual({ action: "revert" });
  });

  it("⚠ refuses a negative — a return's sign comes from isReturn, never from the quantity", () => {
    expect(commit("-2")).toEqual({ action: "revert" });
  });

  it("⚠ refuses a number too large to be represented exactly", () => {
    expect(commit("99999999999999999999")).toEqual({ action: "revert" });
  });
});

describe("commitQuantity — the pending-scan box (0 may NOT remove anything)", () => {
  const commit = (s: string) => commitQuantity(s, false);

  it("reverts on 0 rather than removing, because there is no line yet", () => {
    expect(commit("0")).toEqual({ action: "revert" });
  });

  it("otherwise behaves the same", () => {
    expect(commit("5")).toEqual({ action: "set", quantity: 5 });
    expect(commit("2.5")).toEqual({ action: "revert" });
  });
});

describe("isQuantityDraft — the keystroke filter", () => {
  it("allows an empty box so the operator can clear and retype", () => {
    expect(isQuantityDraft("")).toBe(true);
  });

  it("allows digits", () => {
    expect(isQuantityDraft("7")).toBe(true);
    expect(isQuantityDraft("100")).toBe(true);
  });

  it("blocks what can never be part of a quantity", () => {
    expect(isQuantityDraft("2.5")).toBe(false);
    expect(isQuantityDraft("-1")).toBe(false);
    expect(isQuantityDraft("x")).toBe(false);
  });
});
