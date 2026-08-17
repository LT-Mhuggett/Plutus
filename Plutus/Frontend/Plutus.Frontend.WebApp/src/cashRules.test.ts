import { describe, expect, it } from "vitest";
import { lastMarkClosesDay, terminalRefusal, zMayGo } from "./cashRules.ts";

/**
 * W-P5 — the rules that decide whether a day's banking reconciles.
 *
 * ⚠⚠ C2 TWIN of `Services/Sync/CashPushService.cs`. These three are pulled out of the IndexedDB
 * plumbing precisely so they can be tested without a browser — the queue itself is I/O, but every way
 * this feature can lose or hide money is a decision, and the decisions are here.
 */

describe("lastMarkClosesDay — one Z per business day, enforced locally", () => {
  /**
   * ⚠⚠ THE LATEST MARK WINS, never `some(ZClose)`. `Any(ZClose)` would refuse a reopened day for
   * ever — the same rule `CashDay.IsClosed` states server-side, and it is why the reopen is a
   * COMPENSATING event rather than a deletion.
   */
  it("is closed after a Z", () => {
    expect(lastMarkClosesDay(["OpenFloat", "ZClose"])).toBe(true);
  });

  it("is OPEN again after a reopen", () => {
    expect(lastMarkClosesDay(["OpenFloat", "ZClose", "ZReopen"])).toBe(false);
  });

  it("is closed again after a second Z following a reopen", () => {
    expect(lastMarkClosesDay(["ZClose", "ZReopen", "ZClose"])).toBe(true);
  });

  it("is open when nothing has closed it", () => {
    expect(lastMarkClosesDay([])).toBe(false);
    expect(lastMarkClosesDay(["OpenFloat", "PaidIn", "XSnapshot"])).toBe(false);
  });

  /** ⚠ An X read is not a mark — it counts the drawer without closing it, so it must not affect this. */
  it("ignores X reads entirely", () => {
    expect(lastMarkClosesDay(["ZClose", "XSnapshot"])).toBe(true);
    expect(lastMarkClosesDay(["ZClose", "ZReopen", "XSnapshot"])).toBe(false);
  });
});

describe("zMayGo — a Z waits for its own day's sales", () => {
  /**
   * ⚠⚠ THE MONEY RULE. The platform's expected drawer is float + **cash takings** + ins − outs, so a Z
   * that overtakes queued sales reports a shortage equal to every sale still waiting: a till that
   * traded £400 through an outage would tell the person who counted it correctly that they were £400
   * down. ⚠ And the Z is TERMINAL server-side, so it would then refuse those very sales — putting the
   * day's real takings into quarantine behind their own close.
   */
  it("holds the Z while that day still has sales queued", () => {
    expect(zMayGo(3)).toBe(false);
    expect(zMayGo(1)).toBe(false);
  });

  it("lets the Z go once the day's sales have drained", () => {
    expect(zMayGo(0)).toBe(true);
  });
});

describe("terminalRefusal — which answers must never be retried", () => {
  /**
   * ⚠⚠ 409 AND 400 ARE TERMINAL. Retrying cannot help, and a till that retries them for ever **looks
   * healthy while quietly never banking** — which is the failure mode that hides money rather than
   * losing it, and therefore the one nobody notices.
   */
  it.each([409, 400])("is terminal for %i", (code) => {
    expect(terminalRefusal(code)).toBe(true);
  });

  /** ⚠ Everything else is a RETRY. A 5xx, a 429 or an auth hiccup are all temporary, and treating one
   *  as terminal would drop a float on the floor. */
  it.each([0, 401, 403, 408, 429, 500, 502, 503])("is retryable for %i", (code) => {
    expect(terminalRefusal(code)).toBe(false);
  });
});
