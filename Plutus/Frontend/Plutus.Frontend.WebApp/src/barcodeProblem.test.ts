import { describe, expect, it } from "vitest";
import { barcodeProblem } from "./barcodeProblem.ts";

/**
 * The live barcode check — the vectors for the rule itself.
 *
 * ⚠ These are the OPERATOR-FACING half of the multi-barcode rules; the server-side half (reserved
 * shapes, length, the unique index) is pinned by `ItemBarcodeRulesTests` in .NET. Neither suite covers
 * the other, on purpose — this one must NOT restate the reserved shapes, because a copy of those here
 * is exactly the drift the C2 register exists to catch.
 *
 * ⚠⚠ THE PORTAL'S BYTE-IDENTICAL COPY IS PINNED ELSEWHERE: `FrontendTwinTests` in
 * `tests/Plutus.Tests.Architecture`. It is not pinned here because the web till has no `@types/node`,
 * so a test in this project cannot read a sibling project off disk without adding a dependency — and
 * the architecture suite already scans source text on disk and knows where the repo root is.
 */

const OWN = "9780123456789";
const MINE = ["ALT-1", "ALT-2"];
const ALL = [...MINE, "OTHER-ITEM-CODE"];

const check = (raw: string, editing?: string) => barcodeProblem(raw, MINE, ALL, OWN, editing);

describe("barcodeProblem", () => {
  it("says nothing about an empty box", () => {
    // ⚠ An empty field is not a mistake, it is a field nobody has filled in yet. Warning on it would
    // paint the dialog red the moment it opens.
    expect(check("")).toBeNull();
  });

  it("accepts a free code", () => {
    expect(check("NEW-CODE-1")).toBeNull();
  });

  it("warns — but does not block — on leading or trailing whitespace", () => {
    for (const raw of [" 123", "123 ", "  123  ", "\t123"]) {
      const p = check(raw);
      expect(p?.level, raw).toBe("warn");
      expect(p?.message, raw).toContain("saved without it");
    }
  });

  it("warns about whitespace BEFORE calling a padded duplicate a duplicate", () => {
    // ⚠⚠ THE ORDER IS THE RULE. " ALT-1 " trims to a code this item already has, so a trim-first
    // implementation reports "this item already has that barcode" and DISABLES the save — for a value
    // the server would have accepted as a no-op. The whitespace warning has to win.
    const p = check(" ALT-1 ");
    expect(p?.level).toBe("warn");
  });

  it("refuses the item's own barcode", () => {
    const p = check(OWN);
    expect(p?.level).toBe("error");
    expect(p?.message).toContain("own barcode");
  });

  it("refuses a code this item already has", () => {
    expect(check("ALT-1")?.level).toBe("error");
    expect(check("ALT-1")?.message).toContain("This item already has");
  });

  it("refuses a code another item has, with a different sentence", () => {
    // ⚠ Two distinct messages because the operator's next action differs: one means "you already did
    // this", the other means "go and find out which item owns it".
    expect(check("OTHER-ITEM-CODE")?.message).toContain("Another item already has");
  });

  it("lets a code being corrected keep its own value", () => {
    // ⚠ Without this the 🔒 Edit box is invalid the instant it opens — the code under correction is in
    // both lists, so it would clash with itself and the Save button would never light up.
    expect(check("ALT-1", "ALT-1")).toBeNull();
  });

  it("still refuses a DIFFERENT taken code while correcting one", () => {
    expect(check("ALT-2", "ALT-1")?.level).toBe("error");
    expect(check("OTHER-ITEM-CODE", "ALT-1")?.level).toBe("error");
  });

  it("is case-sensitive, matching the server", () => {
    // ⚠ `ItemBarcodeRules.Normalise` trims and NEVER case-folds, because a barcode is a byte string
    // from a scanner. "alt-1" is a different code from "ALT-1" and must be allowed through to the
    // server, which owns the decision.
    expect(check("alt-1")).toBeNull();
  });

  it("treats an empty own-id as no own-id", () => {
    // A brand-new item has no IdOne yet. It must not make "" collide with the empty box, and the
    // empty-box case above already returns null before this could matter — this pins that it stays so.
    expect(barcodeProblem("", MINE, ALL, "")).toBeNull();
    expect(barcodeProblem("ANYTHING", MINE, ALL, "")).toBeNull();
  });
});
