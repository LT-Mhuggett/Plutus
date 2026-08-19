import { describe, expect, it } from "vitest";
import { readableInkOn, withDerivedPairs } from "./theme.ts";

/**
 * The TypeScript half of the slot-pair twin — WP-T1 T1.2, 2026-08-19.
 *
 * ⚠⚠ **THE SAME VECTORS AS `ThemeSlotPairTests` IN .NET, ON PURPOSE.** Two tills that fill a half-set
 * pair differently show one shop two different screens from one theme, and nothing flags it.
 *
 * ⚠ The fault this closes is the DEFAULT configuration, not an edge case: a theme of
 * `{"accent":"#f5f5c0"}` — one pale brand colour, which is what a shop sets — left the accent ink at
 * white, so every accent button rendered white text on pale yellow.
 *
 * **Add a case here, add it there.**
 */

/** ⚠ WCAG contrast, so a claim about legibility is arithmetic rather than an opinion. */
function contrast(a: string, b: string): number {
  const lum = (hex: string) => {
    const ch = (v: number) => {
      const s = v / 255;
      return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
    };
    const h = hex.replace("#", "");
    return 0.2126 * ch(parseInt(h.slice(0, 2), 16))
      + 0.7152 * ch(parseInt(h.slice(2, 4), 16))
      + 0.0722 * ch(parseInt(h.slice(4, 6), 16));
  };

  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

describe("the readable ink", () => {
  it("is the one with more contrast", () => {
    expect(readableInkOn("#ffffff")).toBe("#000000");
    expect(readableInkOn("#000000")).toBe("#ffffff");
    expect(readableInkOn("#f5f5c0")).toBe("#000000");   // the pale accent from the live fault
    expect(readableInkOn("#2c698d")).toBe("#ffffff");   // the stock accent — still takes white
    expect(readableInkOn("#7fbf7f")).toBe("#000000");   // a mid green: "is the hex big" gets this wrong
  });

  /** ⚠ Rubbish in means nothing out, so a caller keeps its stylesheet default. */
  it("derives nothing from an unusable background", () => {
    expect(readableInkOn("#abc")).toBeNull();
    expect(readableInkOn("#12345678")).toBeNull();
    expect(readableInkOn("red")).toBeNull();
    expect(readableInkOn("")).toBeNull();
    expect(readableInkOn(null)).toBeNull();
    expect(readableInkOn(undefined)).toBeNull();
  });
});

describe("filling a half-set pair", () => {
  /** ⚠⚠ THE LIVE FAULT. */
  it("gives a pale accent a readable ink", () => {
    const paired = withDerivedPairs({ accent: "#f5f5c0" });

    expect(paired.accentInk).toBe("#000000");
    expect(contrast(paired.accent!, paired.accentInk!)).toBeGreaterThanOrEqual(4.5);
  });

  it("gives a dark surface a readable ink", () => {
    const paired = withDerivedPairs({ surface: "#101216" });

    expect(paired.ink).toBe("#ffffff");
    expect(contrast(paired.surface!, paired.ink!)).toBeGreaterThanOrEqual(4.5);
  });

  /**
   * ⚠⚠ A PAIR THE PORTAL SET ITSELF IS LEFT ALONE, even when it contrasts badly. An owner who set both
   * halves owns the result, and overwriting would make the portal's own preview a lie.
   */
  it("never overwrites a slot the portal set", () => {
    expect(withDerivedPairs({ accent: "#f5f5c0", accentInk: "#ffffff" }).accentInk).toBe("#ffffff");
  });

  /** ⚠⚠ ONLY THE TWO NAMED PAIRS — inventing a surface2 from a surface is not this code's decision. */
  it("invents nothing beyond the two named pairs", () => {
    const paired = withDerivedPairs({ accent: "#f5f5c0", surface: "#101216" });

    expect(Object.keys(paired).sort()).toEqual(["accent", "accentInk", "ink", "surface"]);
  });

  /** ⚠ Nothing set means nothing derived — clearing an override must restore the defaults exactly. */
  it("derives nothing from an empty theme", () => {
    expect(withDerivedPairs({})).toEqual({});
  });
});
