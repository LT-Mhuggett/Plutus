import { afterEach, describe, expect, it } from "vitest";
import { apiClock, apiDate, apiDateTime, apiDay, apiMs, apiTime, businessToday, setDisplayZone, getDisplayZone, readerZoneDiffers } from "./apiTime.ts";

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

/**
 * WP-TZ — rendering on the SHOP's clock rather than the reader's.
 *
 * ⚠⚠ THE POINT IS THE PORTAL: opened from anywhere, it rendered every timestamp in the browser's
 * zone, so the same sale read 14:32 on the shop floor and 15:32 in Madrid with nothing saying which.
 *
 * ⚠ THESE ASSERT AGAINST A FIXED ZONE, not against the machine's — the whole feature is "stop
 * depending on the machine's", so a test that depended on it would be testing the wrong thing.
 */
describe("the display zone", () => {
  // ⚠ 14:30 UTC on a SUMMER day: 15:30 London (BST), 16:30 Madrid (CEST), 10:30 New York (EDT).
  // ⚠ EDT is UTC−4, not −5 — I wrote 09:30 here first and the test caught it. Northern-hemisphere
  // summer is exactly when a hard-coded winter offset looks right and is not.
  const SUMMER = "2026-08-21T14:30:00Z";

  afterEach(() => { setDisplayZone(null); });

  it("renders on the device's clock until a zone is set", () => {
    expect(getDisplayZone()).toBeNull();
  });

  it("renders a shop zone whatever the reader's machine is on", () => {
    setDisplayZone("Europe/London");
    expect(apiTime(SUMMER)).toBe("15:30");

    setDisplayZone("Europe/Madrid");
    expect(apiTime(SUMMER)).toBe("16:30");

    setDisplayZone("America/New_York");
    expect(apiTime(SUMMER)).toBe("10:30");
  });

  /** ⚠ THE DATE MOVES WITH IT, and that is the half that matters for a day-drill: 23:30 UTC is
   *  already tomorrow in Sydney and still today in London. */
  it("moves the DATE too, not just the time", () => {
    setDisplayZone("Australia/Sydney");
    expect(apiDay("2026-08-21T23:30:00Z")).toBe("22/08/2026");

    setDisplayZone("Europe/London");
    expect(apiDay("2026-08-21T23:30:00Z")).toBe("22/08/2026");

    setDisplayZone("America/New_York");
    expect(apiDay("2026-08-21T23:30:00Z")).toBe("21/08/2026");
  });

  /** ⚠ DAYLIGHT SAVING IS THE PLATFORM'S PROBLEM, NOT OURS — the same zone renders differently in
   *  January and August, and hard-coding an offset anywhere would be wrong twice a year. */
  it("follows daylight saving", () => {
    setDisplayZone("Europe/London");
    expect(apiTime("2026-01-21T14:30:00Z")).toBe("14:30");   // GMT
    expect(apiTime("2026-08-21T14:30:00Z")).toBe("15:30");   // BST
  });

  /**
   * ⚠⚠ AN UNKNOWN ZONE IS REFUSED, NOT STORED. `toLocaleString` throws a RangeError on a bad
   * `timeZone`, so accepting one would not render an hour wrong — it would blank every date on the
   * page. Rejecting at the door leaves the platform rendering device-local.
   */
  it("refuses a zone it cannot resolve, and keeps rendering", () => {
    expect(setDisplayZone("Mars/Olympus_Mons")).toBe(false);
    expect(getDisplayZone()).toBeNull();
    expect(apiDateTime(SUMMER)).toMatch(/\d{2}\/\d{2}\/\d{4}/);
  });

  it("clears back to the device", () => {
    expect(setDisplayZone("Europe/Madrid")).toBe(true);
    expect(setDisplayZone(null)).toBe(true);
    expect(getDisplayZone()).toBeNull();
  });

  /** ⚠ COMPARED AS RENDERED TIMES, NOT ZONE NAMES: London and Dublin are one clock under two names,
   *  and warning about those would train people to ignore the banner. */
  it("does not call two names for the same clock a difference", () => {
    setDisplayZone("Europe/Dublin");
    const sameClock = readerZoneDiffers(new Date(SUMMER));

    setDisplayZone("Pacific/Kiritimati");
    expect(readerZoneDiffers(new Date(SUMMER))).toBe(true);

    // Dublin only differs from the test machine if the test machine is not on UK time.
    expect(typeof sameClock).toBe("boolean");
  });

  /** ⚠ THE INSTANT NEVER MOVES — only the wall clock it is printed against. */
  it("does not change what instant a timestamp is", () => {
    const before = apiDate(SUMMER)!.getTime();
    setDisplayZone("Asia/Tokyo");
    expect(apiDate(SUMMER)!.getTime()).toBe(before);
  });
});

/**
 * ⚠⚠ "Today" IS THE SHOP'S TODAY, and the whole reason this helper exists is the hour when the two
 * disagree. Britain runs an hour ahead of UTC all summer, so `toISOString().slice(0, 10)` — the
 * idiom this replaced — answers YESTERDAY from midnight until 01:00 BST. A "Today's sales" button
 * that shows an empty screen while a late shop is still cashing up is the worst possible moment to
 * be wrong by a day.
 */
describe("businessToday", () => {
  afterEach(() => setDisplayZone(null));

  /** 00:30 BST on the 23rd is still 23:30 UTC on the 22nd — the bug, in one assertion. */
  it("is the shop's day, not the UTC day, in the hour they disagree", () => {
    const justAfterMidnightInLondon = new Date("2026-08-22T23:30:00Z");

    setDisplayZone("Europe/London");
    expect(businessToday(justAfterMidnightInLondon)).toBe("2026-08-23");

    // ⚠ What the old idiom would have said, kept as the contrast rather than as a rule.
    expect(justAfterMidnightInLondon.toISOString().slice(0, 10)).toBe("2026-08-22");
  });

  /** ⚠ A manager abroad must still drill into the SHOP's day, not their own. */
  it("follows the shop's zone, not the reader's", () => {
    const middayUtc = new Date("2026-08-22T12:00:00Z");

    // Kiritimati is UTC+14 — already the 23rd there while London is still on the 22nd.
    setDisplayZone("Pacific/Kiritimati");
    expect(businessToday(middayUtc)).toBe("2026-08-23");

    setDisplayZone("Europe/London");
    expect(businessToday(middayUtc)).toBe("2026-08-22");
  });

  /** ⚠ No shop zone → the device's own day, which is what every screen did before WP-TZ. */
  it("falls back to the device when no shop zone is set", () => {
    setDisplayZone(null);
    const at = new Date("2026-08-22T12:00:00Z");
    const expected = new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit" }).format(at);
    expect(businessToday(at)).toBe(expected);
  });

  /** ⚠ Zero-padded, always — a date input silently ignores `2026-8-5`. */
  it("zero-pads month and day", () => {
    setDisplayZone("Europe/London");
    expect(businessToday(new Date("2026-01-05T12:00:00Z"))).toBe("2026-01-05");
    expect(businessToday(new Date("2026-01-05T12:00:00Z"))).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});
