import { describe, expect, it } from "vitest";
import { showsOnATill } from "./api.ts";

/**
 * `showsOnATill` is the web till's half of a C2 twin — `Plutus.Client.Core.NoticesClient.ShowsOnATill`
 * is MAUI's. The two decide **which platform announcements a human standing at a till is shown**, and
 * two tills in one shop disagreeing about an incident is precisely the drift till-design.md Part C
 * exists to prevent.
 *
 * ⚠⚠ This file exists because the twins HAD drifted. Until 2026-08-16 the web till used an inline
 * allow-list — `severity === "Maintenance" || severity === "Incident"` — while MAUI used a deny-list
 * on `Info`. The cases below are the ones where those two answers differ.
 */
describe("showsOnATill — the web till's half of the announcement rule", () => {
  it("shows the two severities a till has always shown", () => {
    expect(showsOnATill("Incident")).toBe(true);
    expect(showsOnATill("Maintenance")).toBe(true);
  });

  it("keeps Info off the till — it is portal-only", () => {
    // ⚠ The banner interrupts someone mid-transaction. Fill it with release notes and operators
    // learn to ignore it, and then the incident goes unread too.
    expect(showsOnATill("Info")).toBe(false);
  });

  it("⚠⚠ SHOWS a severity it has never heard of", () => {
    // This is the case the old allow-list got wrong, silently. A severity a later backend adds will
    // be at least as urgent as maintenance — defaulting to "hide" blanks exactly the messages worth
    // reading, on every till, until somebody ships a new build.
    expect(showsOnATill("Critical")).toBe(true);
    expect(showsOnATill("Emergency")).toBe(true);
  });

  it("⚠ is case-insensitive and trims", () => {
    // Whether a shop hears about an outage must not depend on a backend's capitalisation.
    expect(showsOnATill("incident")).toBe(true);
    expect(showsOnATill(" Incident ")).toBe(true);

    // ...and the same forgiveness has to apply to the one severity that is hidden, or "info" from a
    // future backend fills the till banner with release notes.
    expect(showsOnATill("info")).toBe(false);
    expect(showsOnATill(" INFO ")).toBe(false);
  });

  it("⚠ shows a notice with no severity at all", () => {
    // Same reasoning: a message the server sent without a severity is still a message somebody meant
    // a till to see, and swallowing it is the failure that cannot be noticed.
    expect(showsOnATill("")).toBe(true);
    expect(showsOnATill(null)).toBe(true);
    expect(showsOnATill(undefined)).toBe(true);
  });
});
