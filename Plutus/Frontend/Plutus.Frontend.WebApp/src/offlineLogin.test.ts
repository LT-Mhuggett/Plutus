import { describe, expect, it } from "vitest";
import {
  allowedWhileOffline, assess, PBKDF2_HASH, PBKDF2_ITERATIONS, POLICY,
  SELL_FLOOR, sessionExpiresAt, survivesStaleness,
} from "./offlineLogin.ts";

/**
 * W-P4 — signing in with the network down, and how long a cached credential is trusted.
 *
 * ⚠⚠ C2 TWIN of `SharedKernel/OfflineCredentials.cs` (+ `Crypto.cs Pbkdf2`), mirroring
 * `OfflineCredentialsTests.cs`.
 *
 * ⚠ The shape these rules exist to produce, from the .NET header: **selling stays alive for a long
 * time, money-out expires quickly, and the till NEVER hard-locks.** A till that refused to sell
 * because its roster was old would close a shop over a connection problem — which is the outage the
 * whole offline design exists to prevent.
 */

const DAY = 86_400_000;
const NOW = new Date("2026-08-17T12:00:00Z");
const agedDays = (d: number) => new Date(NOW.getTime() - d * DAY).toISOString();

describe("the horizons — mirrored EXACTLY from OfflineCredentialPolicy.Default", () => {
  /** ⚠ These five numbers live in one place on each side so the till, the portal warning and the DPA
   *  statement cannot disagree. If one changes on either side, this test is the tripwire. */
  it("are 7d money-out · 30d sell · 3d warn · 15min idle-lock · 12h session", () => {
    expect(POLICY.moneyOutMaxAgeMs).toBe(7 * DAY);
    expect(POLICY.sellMaxAgeMs).toBe(30 * DAY);
    expect(POLICY.warnAfterMs).toBe(3 * DAY);
    expect(POLICY.idleLockMs).toBe(15 * 60_000);
    expect(POLICY.maxSessionMs).toBe(12 * 3600_000);
  });
});

describe("assess — the three tiers and their boundaries", () => {
  it("is fresh and silent inside the warn horizon", () => {
    const a = assess(agedDays(1), NOW);
    expect(a.trust).toBe("full");
    expect(a.shouldWarn).toBe(false);
    expect(a.message).toBe("");
    expect(a.maySignIn).toBe(true);
  });

  /** ⚠ The warning PRECEDES the loss, deliberately — that is what `WarnAfter` is for. Still full
   *  trust; the operator is being told what is coming. */
  it("warns past 3 days while keeping full trust", () => {
    const a = assess(agedDays(4), NOW);
    expect(a.trust).toBe("full");
    expect(a.shouldWarn).toBe(true);
    expect(a.message).toContain("Reconnect soon");
  });

  /**
   * ⚠⚠ PAST MONEY-OUT THE TILL KEEPS SELLING. This is the whole point of the tiering: refunds, cash
   * out and manager functions go; selling does not, because a till that stopped selling would close
   * the shop over a connection problem.
   */
  it("withdraws money-out past 7 days but keeps selling", () => {
    const a = assess(agedDays(8), NOW);
    expect(a.trust).toBe("sellOnly");
    expect(a.maySignIn).toBe(true);
    expect(a.message).toContain("Sales work normally");
  });

  it("refuses offline sign-in past 30 days", () => {
    const a = assess(agedDays(31), NOW);
    expect(a.trust).toBe("refused");
    expect(a.maySignIn).toBe(false);
    expect(a.message).toContain("temporary code");
  });

  /** ⚠ Boundaries are `>`, not `>=` — exactly on a horizon is still inside it, matching .NET. */
  it("treats a value exactly on a horizon as inside it", () => {
    expect(assess(new Date(NOW.getTime() - POLICY.warnAfterMs).toISOString(), NOW).shouldWarn).toBe(false);
    expect(assess(new Date(NOW.getTime() - POLICY.moneyOutMaxAgeMs).toISOString(), NOW).trust).toBe("full");
    expect(assess(new Date(NOW.getTime() - POLICY.sellMaxAgeMs).toISOString(), NOW).trust).toBe("sellOnly");
  });

  /**
   * ⚠⚠ A CLOCK THAT HAS GONE BACKWARDS IS CLAMPED TO ZERO and treated as current — the same choice
   * .NET makes, and for the stated reason: refusing to let staff in over a wrong clock fails in the
   * one direction that stops a shop trading.
   */
  it("clamps a future-stamped roster to fresh rather than refusing", () => {
    const a = assess(new Date(NOW.getTime() + 5 * DAY).toISOString(), NOW);
    expect(a.trust).toBe("full");
    expect(a.ageMs).toBe(0);
    expect(a.maySignIn).toBe(false || true); // signs in — the point is it is not refused
  });

  /** ⚠ NO roster is refused, not "fresh". There is nothing to verify against, and treating absence as
   *  freshness would let anybody in on a till that had never synced. */
  it.each([null, undefined, "", "not a date"])("refuses when there is no usable roster stamp (%s)", (stamp) => {
    const a = assess(stamp as string | null, NOW);
    expect(a.maySignIn).toBe(false);
    expect(a.trust).toBe("refused");
  });

  it("counts whole days down in the message", () => {
    expect(assess(agedDays(9.5), NOW).message).toContain("9 days");
  });
});

describe("the sell floor — default-deny", () => {
  /**
   * ⚠⚠ AN ALLOW-LIST, so it is default-deny: a permission added to the catalogue later is withdrawn
   * when stale until somebody deliberately adds it here. A new permission that silently survived
   * staleness would be the exact bug this tiering exists to prevent.
   */
  it("holds selling and support tickets, and nothing else", () => {
    expect([...SELL_FLOOR].sort()).toEqual(["pos.sell", "support.tickets"]);
  });

  /** ⚠ Support tickets survive for the same reason they are seeded to every role: a lone cashier on a
   *  broken till must be able to shout for help — doubly so when the broken thing is the connection. */
  it.each(["pos.sell", "support.tickets"])("keeps %s while stale", (code) => {
    expect(survivesStaleness(code)).toBe(true);
  });

  it.each(["pos.refund", "pos.discount", "pos.cash.reopen", "inventory.bulk", "customers.manage"])(
    "withdraws %s while stale",
    (code) => {
      expect(survivesStaleness(code)).toBe(false);
    },
  );

  it.each([null, undefined, ""])("withdraws an absent code (%s)", (code) => {
    expect(survivesStaleness(code as string | null)).toBe(false);
  });
});

describe("allowedWhileOffline — the two questions kept apart", () => {
  /** ⚠ This answers *"has the horizon withdrawn it?"*, never *"does the operator hold it?"* — the
   *  caller combines it with `can(...)`. Conflating them would let staleness grant a permission. */
  it("allows everything at full trust", () => {
    expect(allowedWhileOffline("full", "pos.refund")).toBe(true);
    expect(allowedWhileOffline("full", "anything.at.all")).toBe(true);
  });

  it("allows only the floor at sellOnly", () => {
    expect(allowedWhileOffline("sellOnly", "pos.sell")).toBe(true);
    expect(allowedWhileOffline("sellOnly", "pos.refund")).toBe(false);
  });

  it("allows nothing when refused", () => {
    expect(allowedWhileOffline("refused", "pos.sell")).toBe(false);
  });
});

describe("sessionExpiresAt — the cap and the rollover", () => {
  /**
   * ⚠⚠ THE ROLLOVER IS THE LOAD-BEARING HALF. A session spanning two business days leaks yesterday's
   * operator into today's X/Z breakdown, and a shift change with no re-auth attributes the incoming
   * person's sales to the outgoing one — silently, in exactly the records HMRC would ask about.
   */
  it("ends at the business-day rollover when that comes first", () => {
    const signedIn = new Date(2026, 7, 17, 22, 0, 0); // 22:00 local — 12h cap would be 10:00 tomorrow
    const expires = sessionExpiresAt(signedIn, signedIn);

    expect(expires.getDate()).toBe(18);
    expect(expires.getHours()).toBe(0);
    expect(expires.getMinutes()).toBe(0);
  });

  it("ends at the 12h cap when that comes first", () => {
    const signedIn = new Date(2026, 7, 17, 2, 0, 0); // 02:00 local — cap 14:00, rollover midnight
    const expires = sessionExpiresAt(signedIn, signedIn);

    expect(expires.getHours()).toBe(14);
    expect(expires.getDate()).toBe(17);
  });

  /** ⚠ The rollover is the TILL's own wall-clock midnight, not UTC's — a business day is a shop's day
   *  (till-design Part C). */
  it("uses local midnight, not UTC midnight", () => {
    const signedIn = new Date(2026, 7, 17, 23, 30, 0);
    const expires = sessionExpiresAt(signedIn, signedIn);

    expect(expires.getHours()).toBe(0);
    expect(expires.getDate()).toBe(18);
  });
});

describe("the PBKDF2 parameters", () => {
  /**
   * ⚠⚠ SHA-1 IS DELIBERATE, and this test exists to stop somebody "modernising" it. `Crypto.cs` says
   * so outright: it preserves the LEGACY NatApp till hash byte-for-byte. A SHA-256 twin would refuse
   * **every valid password ever set**, and would present as a wrong-password bug rather than a hash
   * mismatch — so it would be debugged in entirely the wrong place.
   */
  it("are 101010 iterations of SHA-1", () => {
    expect(PBKDF2_ITERATIONS).toBe(101010);
    expect(PBKDF2_HASH).toBe("SHA-1");
  });
});
