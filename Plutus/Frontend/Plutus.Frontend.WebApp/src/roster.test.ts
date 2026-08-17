import { describe, expect, it } from "vitest";
import { DISABLED_MESSAGE, isStillPermitted, type OperatorRoster, type TillOperator } from "./roster.ts";

/**
 * W-P2 — is the signed-in operator still allowed at this till?
 *
 * ⚠⚠ THE C2 TWIN of `Plutus.Client.Core/OperatorRevocation.cs`, mirroring
 * `tests/Plutus.Tests.Unit/OperatorRevocationTests.cs`.
 *
 * ⚠⚠ THIS RULE HAS ONE CATASTROPHIC WAY TO BE WRONG, and it looks identical to the right one at a
 * glance: treating "we could not fetch the roster" as "you are not on the roster". That version signs
 * **the whole shop out, mid-sale, every time the broadband hiccups** — with a message accusing the
 * operator of being disabled — and it fails first on the worst-connected sites. Most of the cases
 * below exist to pin the difference.
 */

const OP = (userId: string): TillOperator => ({
  userId,
  displayName: "Ann Shah",
  email: "ann@kapow.example",
  credentialHashBase64: "aGFzaA==",
  credentialSaltBase64: "c2FsdA==",
  grants: [],
});

const ROSTER = (...operators: TillOperator[]): OperatorRoster => ({
  tillId: "11111111-1111-1111-1111-111111111111",
  asOfUtc: "2026-08-17T09:00:00Z",
  operators,
});

const ANN = "aaaaaaaa-1111-2222-3333-444444444444";
const BOB = "bbbbbbbb-1111-2222-3333-444444444444";

describe("isStillPermitted — the load-bearing distinction", () => {
  /**
   * ⚠⚠ A ROSTER THAT COULD NOT BE FETCHED IS NOT AN EMPTY ROSTER. `refreshRoster` returns null on
   * ANY failure, and null must mean "carry on". This is the single most important case in the file.
   */
  it("keeps the operator signed in when the roster could not be fetched", () => {
    expect(isStillPermitted(ANN, null)).toBe(true);
  });

  /**
   * ⚠ But an EMPTY roster the server actually SENT is a real answer — nobody is assigned to this till
   * any more — and that does sign the operator out, correctly. Both halves matter; getting either
   * backwards breaks the other.
   */
  it("signs the operator out on an empty roster the server really sent", () => {
    expect(isStillPermitted(ANN, ROSTER())).toBe(false);
  });
});

describe("isStillPermitted — ordinary cases", () => {
  it("keeps an operator who is on the roster", () => {
    expect(isStillPermitted(ANN, ROSTER(OP(ANN), OP(BOB)))).toBe(true);
  });

  it("signs out an operator who has dropped off it", () => {
    expect(isStillPermitted(ANN, ROSTER(OP(BOB)))).toBe(false);
  });

  /** ⚠ Nobody signed in → nothing to revoke. The login path re-reads the roster on its way in, and
   *  the server already refuses a deactivated employee at login (AuthController, FE9.2). */
  it.each([null, undefined, ""])("has nothing to revoke when nobody is signed in (%s)", (who) => {
    expect(isStillPermitted(who as string | null, ROSTER())).toBe(true);
  });

  /**
   * ⚠ CASE-INSENSITIVE. A Guid can arrive in either case from either end, and a casing mismatch
   * would sign out **every operator in the shop** while the roster was perfectly correct — a fault
   * that would look exactly like the server having emptied the roster.
   */
  it("matches the operator id regardless of case", () => {
    expect(isStillPermitted(ANN.toUpperCase(), ROSTER(OP(ANN.toLowerCase())))).toBe(true);
    expect(isStillPermitted(ANN.toLowerCase(), ROSTER(OP(ANN.toUpperCase())))).toBe(true);
  });

  /** ⚠ A malformed row must not throw on the sign-out path — a crash here would take the till down
   *  rather than sign one person out. */
  it("survives a roster row with no userId", () => {
    const broken = { ...OP(ANN), userId: undefined as unknown as string };
    expect(isStillPermitted(ANN, ROSTER(broken, OP(ANN)))).toBe(true);
    expect(isStillPermitted(BOB, ROSTER(broken))).toBe(false);
  });

  /** ⚠ And an envelope whose operators array is missing entirely is still an ANSWER — it revokes,
   *  the same as an empty array, because the server said something. */
  it("treats a missing operators array as an answer, not as no answer", () => {
    const noArray = { tillId: "t", asOfUtc: "2026-08-17T09:00:00Z" } as unknown as OperatorRoster;
    expect(isStillPermitted(ANN, noArray)).toBe(false);
  });
});

describe("the message", () => {
  /** ⚠ Matt's wording, verbatim (2026-08-11) — and it names the ACCOUNT, not the till: *"you have
   *  been logged out"* sends somebody to reboot the machine instead of to their manager. MAUI shows
   *  the identical sentence. */
  it("is Matt's wording exactly", () => {
    expect(DISABLED_MESSAGE).toBe("Your account has been disabled, please speak to your manager");
  });
});
