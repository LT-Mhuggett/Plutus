import { describe, expect, it } from "vitest";
import { apiClock, apiDate, apiDateTime, apiDay, apiMs, apiTime } from "./apiTime.ts";

/**
 * The UTC rule — TypeScript half.
 *
 * ⚠⚠ C2 TWIN of `ApiTimeTests` in `Plutus.Tests.Unit`. **THE SAME VECTORS RUN IN BOTH LANGUAGES**,
 * and that is the only thing stopping the three copies drifting: this bug existed in TypeScript and
 * in .NET simultaneously, in the same feature, and each was found separately. Add a case here, add it
 * there.
 *
 * ⚠ EVERY ASSERTION IS ABOUT AN INSTANT, never a rendered wall clock — a test that asserted "14:30"
 * would pass or fail on the CI machine's timezone, which is precisely the class of mistake under
 * test. The two rendering tests below assert only that SOMETHING sensible came out, not what.
 */
describe("apiDate", () => {
  const AT_1430_UTC = Date.UTC(2026, 7, 21, 14, 30, 0);

  /** ⚠⚠ THE BUG ITSELF. A bare stamp is what EF + System.Text.Json produce for a MySQL `datetime`,
   *  and `new Date()` reads it as LOCAL — an hour early everywhere the browser is not on UTC. */
  it("reads a bare stamp as UTC", () => {
    expect(apiDate("2026-08-21T14:30:00")?.getTime()).toBe(AT_1430_UTC);
  });

  /** ⚠ THE OTHER HALF OF THE 2026-08-12 FAULT: `+ "Z"` on a value that already said `Z` produced
   *  `…ZZ`, and every till in the estate rendered "Invalid Date". */
  it("leaves a stamp that already says UTC alone", () => {
    expect(apiDate("2026-08-21T14:30:00Z")?.getTime()).toBe(AT_1430_UTC);
  });

  it("honours an explicit offset rather than relabelling it", () => {
    expect(apiDate("2026-08-21T15:30:00+01:00")?.getTime()).toBe(AT_1430_UTC);
    expect(apiDate("2026-08-21T09:30:00-0500")?.getTime()).toBe(AT_1430_UTC);
  });

  it("is idempotent — it will be applied to values that have been through it", () => {
    const once = apiDate("2026-08-21T14:30:00")!.toISOString();
    expect(apiDate(once)?.getTime()).toBe(AT_1430_UTC);
  });

  /** ⚠⚠ NOTHING IS `null`, NOT 1970. `new Date(null)` is the epoch and `new Date(undefined)` is an
   *  Invalid Date; both have been rendered to an operator by this platform, and "01/01/1970" reads
   *  as data rather than as an absence. */
  it("answers null for nothing, and for rubbish", () => {
    expect(apiDate(null)).toBeNull();
    expect(apiDate(undefined)).toBeNull();
    expect(apiDate("")).toBeNull();
    expect(apiDate("not a date")).toBeNull();
  });

  it("gives NaN rather than a wrong number for arithmetic", () => {
    expect(apiMs("2026-08-21T14:30:00")).toBe(AT_1430_UTC);
    expect(Number.isNaN(apiMs(null))).toBe(true);
  });
});

describe("the formatters", () => {
  /** ⚠ A DASH, NOT A BLANK. A column that is sometimes empty and sometimes a date reads as a
   *  rendering fault; a dash reads as "nothing recorded", which is what it means. */
  it("render a dash for nothing", () => {
    expect(apiDateTime(null)).toBe("—");
    expect(apiDay(null)).toBe("—");
    expect(apiTime(undefined)).toBe("—");
  });

  /** ⚠ Asserts SHAPE, not value — the value is the CI machine's timezone and asserting it is how a
   *  test starts failing in October. */
  it("render something date-shaped for a real stamp", () => {
    expect(apiDateTime("2026-08-21T14:30:00")).toMatch(/\d{2}\/\d{2}\/\d{4}/);
    expect(apiDay("2026-08-21T14:30:00")).toMatch(/^\d{2}\/\d{2}\/\d{4}$/);
    expect(apiTime("2026-08-21T14:30:00")).toMatch(/^\d{2}:\d{2}$/);
  });

  /** ⚠⚠ THE REGRESSION, STATED AS A ROUND TRIP: the bare and the suffixed form of the SAME instant
   *  must render identically. This is the assertion that would have caught Matt's hour. */
  it("render the two wire formats of one instant identically", () => {
    expect(apiDateTime("2026-08-21T14:30:00")).toBe(apiDateTime("2026-08-21T14:30:00Z"));
    expect(apiTime("2026-08-21T14:30:00")).toBe(apiTime("2026-08-21T15:30:00+01:00"));
  });
});

/** ⚠ PORTAL-ONLY — the Dashboard's live sale feed is the one place seconds are wanted. The web
 *  till's copy of this file has no `apiClock`, and that is the only difference between them. */
describe("apiClock", () => {
  it("renders to the second, and a dash for nothing", () => {
    expect(apiClock("2026-08-21T14:30:05")).toMatch(/^\d{2}:\d{2}:\d{2}$/);
    expect(apiClock(null)).toBe("—");
  });
});
