import { describe, expect, it } from "vitest";
import { dayText, parseOpeningHours, unknownDayKeys } from "./openingHours.ts";

/**
 * ⚠⚠ THE ONE THAT MATTERS IS `unreadable` vs `unset`. Both tills printed the same sentence for both
 * states, which is how "the portal shows hours and the till says there are none" became a report
 * nobody could act on. Every test below that asserts `unreadable` is asserting that the screen tells
 * the truth about which of the two it is.
 *
 * ⚠ C2 twin of `Plutus.Client.Core/OpeningHours.cs`; `OpeningHoursTests` uses these same vectors.
 * ⚠ And per the 2026-08-17 lesson (§5b W-P7): these are STRING-parsing vectors, so they pin the same
 * thing in both languages — there is no `decimal`/`double` asymmetry hiding in them.
 */

const portal = '{"mon":[{"open":"09:00","close":"17:30"}],"tue":[{"open":"09:00","close":"17:30"}]}';

describe("nothing stored", () => {
  it.each([null, undefined, "", "   "])("reads %p as unset", (json) => {
    expect(parseOpeningHours(json).state).toBe("unset");
  });

  it("reads a stored literal null as unset", () => {
    // ⚠ The portal's editor sends `null` when the last day is unticked, so this is the real
    // "cleared" value, not a hypothetical.
    expect(parseOpeningHours("null").state).toBe("unset");
  });

  it("reads an empty object as unset", () => {
    expect(parseOpeningHours("{}").state).toBe("unset");
  });
});

describe("what the portal's simple editor writes", () => {
  it("reads it, and closed days are CLOSED rather than unknown", () => {
    const hours = parseOpeningHours(portal);
    expect(hours.state).toBe("set");
    if (hours.state !== "set") return;

    expect(hours.week.map((d) => d.label)).toEqual([
      "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
    ]);
    expect(dayText(hours.week[0])).toBe("09:00–17:30");
    expect(dayText(hours.week[2])).toBe("Closed");
  });

  it("keeps the shop's week order, never Sunday-first", () => {
    // ⚠ `Date`'s own day numbering starts on Sunday. Using it would silently reorder every shop's
    // week, and a week that starts on the wrong day looks like a data error rather than a bug here.
    const hours = parseOpeningHours('{"sun":[{"open":"11:00","close":"16:00"}]}');
    if (hours.state !== "set") throw new Error("expected set");
    expect(hours.week[0].label).toBe("Monday");
    expect(hours.week[6].label).toBe("Sunday");
    expect(dayText(hours.week[6])).toBe("11:00–16:00");
  });

  it("shows two spans for a day that shuts for lunch", () => {
    const hours = parseOpeningHours('{"wed":[{"open":"09:00","close":"12:30"},{"open":"13:30","close":"17:00"}]}');
    if (hours.state !== "set") throw new Error("expected set");
    expect(dayText(hours.week[2])).toBe("09:00–12:30, 13:30–17:00");
  });
});

describe("⚠ what a person types into the ADVANCED JSON box", () => {
  it("accepts capitalised and long day names", () => {
    const hours = parseOpeningHours('{"Monday":[{"open":"09:00","close":"17:30"}],"TUE":"09:00-17:30"}');
    if (hours.state !== "set") throw new Error("expected set");
    expect(dayText(hours.week[0])).toBe("09:00–17:30");
    expect(dayText(hours.week[1])).toBe("09:00–17:30");
  });

  it("accepts one span written as a bare object rather than a list", () => {
    const hours = parseOpeningHours('{"mon":{"open":"09:00","close":"17:30"}}');
    if (hours.state !== "set") throw new Error("expected set");
    expect(dayText(hours.week[0])).toBe("09:00–17:30");
  });

  it("accepts from/to instead of open/close", () => {
    const hours = parseOpeningHours('{"mon":{"from":"09:00","to":"17:30"}}');
    if (hours.state !== "set") throw new Error("expected set");
    expect(dayText(hours.week[0])).toBe("09:00–17:30");
  });

  it("pads a single-digit hour and drops seconds so the column lines up", () => {
    const hours = parseOpeningHours('{"mon":{"open":"9:00","close":"17:30:00"}}');
    if (hours.state !== "set") throw new Error("expected set");
    expect(dayText(hours.week[0])).toBe("09:00–17:30");
  });

  it("accepts an en dash, which is what a pasted range carries", () => {
    const hours = parseOpeningHours('{"mon":"09:00–17:30"}');
    if (hours.state !== "set") throw new Error("expected set");
    expect(dayText(hours.week[0])).toBe("09:00–17:30");
  });

  it('treats "closed" and an empty list as CLOSED, not as junk', () => {
    const hours = parseOpeningHours('{"mon":"closed","tue":[]}');
    expect(hours.state).toBe("set");
    if (hours.state !== "set") return;
    expect(dayText(hours.week[0])).toBe("Closed");
    expect(dayText(hours.week[1])).toBe("Closed");
  });

  it("merges two spellings of the same day rather than losing one", () => {
    // ⚠ Dropping the second would lose an afternoon and say nothing about it.
    const hours = parseOpeningHours('{"mon":"09:00-12:30","Monday":"13:30-17:00"}');
    if (hours.state !== "set") throw new Error("expected set");
    expect(dayText(hours.week[0])).toBe("09:00–12:30, 13:30–17:00");
  });
});

describe("⚠⚠ stored but unreadable — the state that used to masquerade as 'not set'", () => {
  it("reports malformed JSON, with the parser's own message", () => {
    const hours = parseOpeningHours('{"mon":[{"open":"09:00","close":"17:30"},]}');
    expect(hours.state).toBe("unreadable");
    if (hours.state !== "unreadable") return;
    // ⚠ The parser's words, verbatim — "invalid JSON" tells nobody which character to look at.
    expect(hours.detail.length).toBeGreaterThan(0);
  });

  it("reports unquoted keys, which is what a hand-typed object looks like", () => {
    expect(parseOpeningHours('{mon: "09:00-17:30"}').state).toBe("unreadable");
  });

  it("reports a list where an object of days belongs", () => {
    const hours = parseOpeningHours('[{"open":"09:00","close":"17:30"}]');
    expect(hours.state).toBe("unreadable");
    if (hours.state !== "unreadable") return;
    expect(hours.detail).toContain("a list");
  });

  it.each(['"09:00-17:30"', "42", "true"])("reports the bare value %s", (json) => {
    expect(parseOpeningHours(json).state).toBe("unreadable");
  });

  it("reports a blob whose keys are not days at all", () => {
    const hours = parseOpeningHours('{"weekdays":"09:00-17:30","weekend":"closed"}');
    expect(hours.state).toBe("unreadable");
    if (hours.state !== "unreadable") return;
    expect(hours.detail).toContain("weekdays");
  });

  it("reports days whose values carry no times", () => {
    const hours = parseOpeningHours('{"mon":{"opens":"09:00"},"tue":{"opens":"09:00"}}');
    expect(hours.state).toBe("unreadable");
    if (hours.state !== "unreadable") return;
    expect(hours.detail).toContain("times");
  });
});

describe("leftover keys are named, not dropped", () => {
  it("renders the days it understood and names the rest", () => {
    // ⚠ A key nobody reads is a setting somebody believes is in effect — bank holidays, in this case.
    const json = '{"mon":"09:00-17:30","holidays":"closed"}';
    const hours = parseOpeningHours(json);
    expect(hours.state).toBe("set");
    expect(unknownDayKeys(json)).toEqual(["holidays"]);
  });

  it("has nothing to name for a clean blob", () => {
    expect(unknownDayKeys(portal)).toEqual([]);
  });

  it("has nothing to name for junk (the unreadable message covers it)", () => {
    expect(unknownDayKeys("{oops")).toEqual([]);
  });
});
