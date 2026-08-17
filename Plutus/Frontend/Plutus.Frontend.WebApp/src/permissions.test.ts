import { describe, expect, it } from "vitest";
import { can, ceilingFor, effectiveAt, isActiveAt, POS_DISCOUNT } from "./permissions.ts";
import type { OperatorGrant } from "./roster.ts";

/**
 * W-P3 — what an operator may do, and up to how much money.
 *
 * ⚠⚠ THE C2 TWIN of `SharedKernel/Permissions.cs`, and **this one is MONEY**: it decides how much a
 * cashier may take off a basket. The two tills disagreeing means one shop's cashiers have a limit and
 * another's do not — and the wrong direction here is silent, because an over-generous ceiling looks
 * exactly like a correct one until somebody audits the discounts.
 */

const GRANT = (over: Partial<OperatorGrant> = {}): OperatorGrant => ({
  code: POS_DISCOUNT,
  maxPence: null,
  validFromUtc: null,
  validToUtc: null,
  daysOfWeekMask: null,
  windowStartLocal: null,
  windowEndLocal: null,
  ...over,
});

// A Monday, 10:00 local.
const MON_10 = new Date(2026, 7, 17, 10, 0, 0);

describe("can — the money question", () => {
  it("allows an amount at or under the ceiling and refuses above it", () => {
    const g = [GRANT({ maxPence: 500 })];

    expect(can(g, POS_DISCOUNT, MON_10, 499)).toBe(true);
    expect(can(g, POS_DISCOUNT, MON_10, 500)).toBe(true); // ⚠ inclusive — "up to £5" includes £5
    expect(can(g, POS_DISCOUNT, MON_10, 501)).toBe(false);
  });

  it("treats a null ceiling as unlimited", () => {
    expect(can([GRANT({ maxPence: null })], POS_DISCOUNT, MON_10, 999_999)).toBe(true);
  });

  /**
   * ⚠ A ceiling applies to the AMOUNT, so a null amount against a ceiling-bearing grant is ALLOWED:
   * *"may discount at all"* and *"may discount £40"* are different questions, and the discount dialog
   * asks the first before it knows the answer to the second.
   */
  it("allows the no-amount question against a capped grant", () => {
    expect(can([GRANT({ maxPence: 500 })], POS_DISCOUNT, MON_10, null)).toBe(true);
  });

  /**
   * ⚠⚠ FAILS CLOSED. An unknown code is DENIED, never waved through — `"perm:x"` and a policy name
   * are different namespaces, and a typo between them has already caused one silent outage here (the
   * pick-notes gate).
   */
  it.each(["pos.discounts", "POS.DISCOUNT", "perm:pos.discount", "", "nonsense"])(
    "refuses an unrecognised or mis-cased code (%s)",
    (code) => {
      expect(can([GRANT({ maxPence: null })], code, MON_10, 100)).toBe(false);
    },
  );

  it.each([null, undefined, []])("refuses when there are no grants at all (%s)", (grants) => {
    expect(can(grants as OperatorGrant[] | null, POS_DISCOUNT, MON_10, 100)).toBe(false);
  });
});

describe("effectiveAt — union-merge", () => {
  /**
   * ⚠⚠ UNION, NOT INTERSECTION, and unlimited wins outright. Two roles each allowing £20 do NOT make
   * £40 — but a role with no ceiling plus a role capped at £20 means no ceiling. Getting this
   * backwards silently demotes every manager who also holds a cashier role.
   */
  it("takes the largest ceiling for a code", () => {
    const merged = effectiveAt([GRANT({ maxPence: 500 }), GRANT({ maxPence: 2000 })], MON_10);

    expect(merged).toHaveLength(1);
    expect(merged[0].maxPence).toBe(2000);
  });

  it("lets an unlimited grant beat any ceiling, in either order", () => {
    expect(effectiveAt([GRANT({ maxPence: 500 }), GRANT({ maxPence: null })], MON_10)[0].maxPence).toBeNull();
    expect(effectiveAt([GRANT({ maxPence: null }), GRANT({ maxPence: 500 })], MON_10)[0].maxPence).toBeNull();
  });

  it("keeps different codes apart", () => {
    const merged = effectiveAt(
      [GRANT({ code: "pos.sell" }), GRANT({ code: POS_DISCOUNT, maxPence: 500 })],
      MON_10,
    );

    expect(merged.map((p) => p.code)).toEqual(["pos.discount", "pos.sell"]); // ordinal-sorted
  });
});

describe("isActiveAt — when a grant counts", () => {
  it("respects the validity period", () => {
    expect(isActiveAt(GRANT({ validFromUtc: "2027-01-01T00:00:00Z" }), MON_10)).toBe(false);
    expect(isActiveAt(GRANT({ validToUtc: "2020-01-01T00:00:00Z" }), MON_10)).toBe(false);
    expect(isActiveAt(GRANT({ validFromUtc: "2020-01-01T00:00:00Z", validToUtc: "2030-01-01T00:00:00Z" }), MON_10)).toBe(true);
  });

  /** ⚠ Bit per day, **Sunday = 0** — matching .NET's `DayOfWeek`. A different origin here would shift
   *  every shift window by a day, which is the kind of bug that only shows up on one weekday. */
  it("respects the day-of-week mask with Sunday as bit 0", () => {
    const mondayOnly = 1 << 1;
    const sundayOnly = 1 << 0;

    expect(isActiveAt(GRANT({ daysOfWeekMask: mondayOnly }), MON_10)).toBe(true);
    expect(isActiveAt(GRANT({ daysOfWeekMask: sundayOnly }), MON_10)).toBe(false);
  });

  it("respects a time-of-day window", () => {
    expect(isActiveAt(GRANT({ windowStartLocal: "09:00", windowEndLocal: "17:00" }), MON_10)).toBe(true);
    expect(isActiveAt(GRANT({ windowStartLocal: "11:00" }), MON_10)).toBe(false);
    expect(isActiveAt(GRANT({ windowEndLocal: "09:30" }), MON_10)).toBe(false);
  });

  /**
   * ⚠⚠ A WINDOW THAT WRAPS MIDNIGHT REJECTS EVERYTHING, and that is mirrored ON PURPOSE. The .NET
   * side says so explicitly: *"deliberately unhandled rather than half-handled — a night-shift window
   * that silently denied every action would be worse than one nobody can save in the first place."*
   * ⚠ Do not "fix" it here alone — that would make the two tills disagree.
   */
  it("mirrors the deliberate midnight-wrap limitation", () => {
    const nightShift = GRANT({ windowStartLocal: "22:00", windowEndLocal: "02:00" });

    expect(isActiveAt(nightShift, MON_10)).toBe(false);                          // 10:00 — outside
    expect(isActiveAt(nightShift, new Date(2026, 7, 17, 23, 0))).toBe(false);     // 23:00 — still refused
  });

  /** ⚠ A malformed window is treated as ABSENT, not as a closed door — a bad value in the portal must
   *  not lock a shop out of its own till. */
  it("ignores an unparseable window rather than denying everything", () => {
    expect(isActiveAt(GRANT({ windowStartLocal: "not a time" }), MON_10)).toBe(true);
  });

  it("counts a grant with no constraints at all", () => {
    expect(isActiveAt(GRANT(), MON_10)).toBe(true);
  });
});

describe("ceilingFor — for showing the operator the number", () => {
  it("reports the merged ceiling, or null when unlimited or absent", () => {
    expect(ceilingFor([GRANT({ maxPence: 500 }), GRANT({ maxPence: 2000 })], POS_DISCOUNT, MON_10)).toBe(2000);
    expect(ceilingFor([GRANT({ maxPence: null })], POS_DISCOUNT, MON_10)).toBeNull();
    expect(ceilingFor([], POS_DISCOUNT, MON_10)).toBeNull();
  });

  /** ⚠ An expired grant contributes nothing — the ceiling reflects what is live NOW. */
  it("ignores grants that are not live", () => {
    expect(ceilingFor([GRANT({ maxPence: 5000, validToUtc: "2020-01-01T00:00:00Z" })], POS_DISCOUNT, MON_10)).toBeNull();
  });
});
