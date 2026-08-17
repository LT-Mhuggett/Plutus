import { describe, expect, it } from "vitest";
import { checkStanding, mustStop, REVOKED_MESSAGE } from "./deviceStanding.ts";

/**
 * W-P1 — whether this till is still allowed to be a till.
 *
 * ⚠⚠ THE C2 TWIN of `Plutus.Client.Core/DeviceRevocation.cs`. These cases mirror
 * `tests/Plutus.Tests.Unit/DeviceRevocationTests.cs` one for one, because the two tills disagreeing
 * about whether a device is finished is the worst possible pair of answers: one shop keeps trading on
 * a stolen machine.
 *
 * ⚠ The rule can be wrong in two directions and both are serious. Too lax and a lost till keeps
 * selling for twelve hours (device tokens have no server-side denylist). Too strict and a dropped
 * connection closes a shop mid-sale — on the worst-connected sites first. Most of these are about the
 * second one, because it is the failure that gets blamed on something else.
 */
describe("checkStanding — the one case that stops a till", () => {
  it("stops on an explicit revocation", () => {
    expect(checkStanding(200, { status: "Revoked" })).toBe("revoked");
    expect(mustStop("revoked")).toBe(true);
  });

  it("⚠ stops case-insensitively — whether a shop's till stops must not depend on capitalisation", () => {
    expect(checkStanding(200, { status: "revoked" })).toBe("revoked");
    expect(checkStanding(200, { status: "REVOKED" })).toBe("revoked");
    expect(checkStanding(200, { status: " Revoked " })).toBe("revoked");
  });
});

describe("checkStanding — the cases that must NOT stop a till", () => {
  /**
   * ⚠⚠ PendingRemoval KEEPS TRADING. Somebody has asked for the till back and nobody has approved it.
   * Stopping here would turn un-enrolment into a way to take a shop down — request removal of a till
   * you do not own and it stops serving customers before any human looks at it.
   */
  it("does not stop on a removal REQUEST", () => {
    expect(checkStanding(200, { status: "PendingRemoval" })).toBe("removalRequested");
    expect(mustStop("removalRequested")).toBe(false);
  });

  it("trades when the device is active", () => {
    expect(checkStanding(200, { status: "Active" })).toBe("trading");
  });

  /**
   * ⚠⚠ THE LOAD-BEARING CASE. No answer is not a revocation. If a failed poll stopped the till,
   * every dropped connection would close a shop mid-sale — a worse outage than the one this
   * prevents, and the same rule `OperatorRevocation` is built on.
   */
  it.each([0, 408, 500, 502, 503, 429])("does not stop on a failed poll (%i)", (code) => {
    expect(checkStanding(code, null)).toBe("trading");
    expect(mustStop(checkStanding(code, null))).toBe(false);
  });

  /**
   * ⚠⚠ AND NEITHER DO 401/403/404, deliberately. Each looks terminal and none reliably is: a
   * clock-skewed till, a mis-issued token, a wrong device id or a tenant filter that did not match
   * all land here, and a routing mistake would otherwise close every till at once. A genuinely
   * un-enrolled device gets the explicit "Revoked" instead — approving a removal sets the status, it
   * does not delete the row.
   */
  it.each([401, 403, 404])("does not stop on an auth or unknown-device answer (%i)", (code) => {
    expect(mustStop(checkStanding(code, null))).toBe(false);
  });

  it("does not stop on a 200 with no usable body", () => {
    expect(checkStanding(200, null)).toBe("trading");
  });

  /**
   * ⚠ A status this build has not been taught keeps trading. Guessing that an unknown word means
   * "stop" would let a backend rename break every till in the estate at once; the opposite failure is
   * bounded by the token's 12h life and is visible on the Settings screen.
   */
  it.each(["Suspended", "Quarantined", "", null, undefined])(
    "keeps trading on an unrecognised status (%s)",
    (status) => {
      expect(checkStanding(200, { status: status as string | null })).toBe("trading");
    },
  );
});

describe("mustStop", () => {
  /** ⚠ The named door, precisely so no caller writes its own comparison and quietly folds
   *  `removalRequested` into it. */
  it("is true for revoked and nothing else", () => {
    expect(mustStop("revoked")).toBe(true);
    expect(mustStop("removalRequested")).toBe(false);
    expect(mustStop("trading")).toBe(false);
  });
});

describe("the message", () => {
  /** ⚠ It blames the TILL, not the account — otherwise somebody tries another login, then another,
   *  and concludes the staff accounts are broken. ⚠ And it must be word-for-word MAUI's, so two
   *  tills in one shop say the same thing. */
  it("names the till and points somewhere", () => {
    expect(REVOKED_MESSAGE).toMatch(/till/i);
    expect(REVOKED_MESSAGE).toMatch(/manager/i);
    expect(REVOKED_MESSAGE).toContain("re-enrolled from the portal");
  });
});
