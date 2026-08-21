import { describe, expect, it } from "vitest";
import { checkConnection, clockIsSuspect, toneFor } from "./connectionCheck.ts";

/**
 * ⚠⚠ THE MIRROR OF `tests/Plutus.Tests.Unit/ConnectivityProbeTests.cs`, for the cases this surface
 * can reach. MAUI's login screen runs `ConnectivityProbe.CheckAsync(verifyIdentity: false)`; this
 * one runs `checkConnection`. Two tills that name the same fault differently send a shop to reboot
 * a router over a portal setting.
 *
 * ⚠ The identity half (`Rejected` / `NotEnrolled`) is deliberately absent: the login screen passes
 * `verifyIdentity: false` on both tills, because whether THIS device is enrolled belongs where
 * there is something an operator can do about it. On this till that is `deviceStanding.ts`.
 */
const ok = (body: unknown, status = 200): typeof fetch =>
  (async () =>
    new Response(JSON.stringify(body), {
      status,
      headers: { "content-type": "application/json" },
    })) as unknown as typeof fetch;

const status = (code: number): typeof fetch =>
  (async () => new Response("", { status: code })) as unknown as typeof fetch;

const throws = (e: Error): typeof fetch =>
  (async () => {
    throw e;
  }) as unknown as typeof fetch;

const at = (iso: string) => () => new Date(iso);

describe("checkConnection", () => {
  /**
   * ⚠ Step 0. Skipping the network call when there is demonstrably no network keeps a shop with a
   * dead router off a five-second timeout on every screen that asks — and it must never be the
   * ONLY signal, because `navigator.onLine` reports the interface, not the server.
   */
  it("no network short-circuits before any request is made", async () => {
    let called = false;
    const s = await checkConnection(
      at("2026-08-21T09:00:00Z"),
      (async () => {
        called = true;
        return new Response("");
      }) as unknown as typeof fetch,
      () => false,
    );

    expect(called).toBe(false);
    expect(s.state).toBe("noNetwork");
    expect(s.summary).toBe("No network — this till is offline.");
  });

  /**
   * ⚠⚠ THE DISTINCTION THE WHOLE FEATURE EXISTS FOR. The network is up and the server did not
   * answer — which is not the operator's cable and not their problem to fix at the till.
   */
  it("a dead socket is 'no server', not 'no network'", async () => {
    const s = await checkConnection(
      at("2026-08-21T09:00:00Z"),
      throws(new TypeError("Failed to fetch")),
      () => true,
    );

    expect(s.state).toBe("noServer");
    expect(s.summary).toBe("Can't reach Plutus — the network is up but the server didn't answer.");
    expect(s.detail).toContain("Failed to fetch");
  });

  it("connects, and reports the server version as the diagnostic line", async () => {
    const s = await checkConnection(
      at("2026-08-21T09:00:00Z"),
      ok({ ok: true, utcNow: "2026-08-21T09:00:00Z", apiVersion: "1.20.0" }),
      () => true,
    );

    expect(s.state).toBe("online");
    expect(s.summary).toBe("Connected to Plutus.");
    expect(s.detail).toBe("Server v1.20.0");
    expect(clockIsSuspect(s)).toBe(false);
  });

  /**
   * ⚠ Reachable but TOO OLD, and stopping here is the honest answer: without /ping this server also
   * has no heartbeat and no catalogue feed, so a confident "Connected" would leave an operator
   * watching every feature 404 with no idea why. The fix is a deploy.
   */
  it("a 404 on /ping is 'too old', which is amber and not red", async () => {
    const s = await checkConnection(at("2026-08-21T09:00:00Z"), status(404), () => true);

    expect(s.state).toBe("serverTooOld");
    expect(s.summary).toBe("Connected, but this Plutus server is too old for this till.");
    expect(toneFor(s.state)).toBe("warn");
  });

  /**
   * ⚠ A 500 is a SICK server, not an absent one — "check your cable" would waste the one person who
   * could ring support. Still reachable, and the code goes in the diagnostic line.
   */
  it("a 500 is still reachable, with the code in the detail line", async () => {
    const s = await checkConnection(at("2026-08-21T09:00:00Z"), status(500), () => true);

    expect(s.state).toBe("online");
    expect(s.detail).toBe("Server reached but answered HTTP 500.");
  });

  /**
   * ⚠ A captive portal or a proxy returning an HTML login page. Something answered, but it was not
   * Plutus — and to a till that is the same as nothing being there.
   */
  it("something that answers but is not Plutus reads as no server", async () => {
    const notJson = (async () =>
      new Response("<html>Sign in to hotel wifi</html>", {
        status: 200,
        headers: { "content-type": "text/html" },
      })) as unknown as typeof fetch;

    const s = await checkConnection(at("2026-08-21T09:00:00Z"), notJson, () => true);

    expect(s.state).toBe("noServer");
    expect(s.detail).toBe("Something answered that address, but it was not Plutus.");
  });
});

describe("clockIsSuspect", () => {
  /**
   * ⚠⚠ WHY THIS IS ON A LOGIN SCREEN AT ALL. Device tokens expire and VAT bands are effective-dated,
   * so a till an hour out can have a day's takings judged against a different instant — and nothing
   * else in the app would ever mention it.
   */
  it("an hour of drift is suspect", async () => {
    const s = await checkConnection(
      at("2026-08-21T09:00:00Z"),
      ok({ ok: true, utcNow: "2026-08-21T10:00:00Z", apiVersion: "1.20.0" }),
      () => true,
    );

    expect(clockIsSuspect(s)).toBe(true);
    expect(s.clockSkewMs).toBe(60 * 60 * 1000);
  });

  /** ⚠ Drift is signless — a till an hour BEHIND is as wrong as one an hour ahead. */
  it("drift the other way is equally suspect", async () => {
    const s = await checkConnection(
      at("2026-08-21T10:00:00Z"),
      ok({ ok: true, utcNow: "2026-08-21T09:00:00Z", apiVersion: "1.20.0" }),
      () => true,
    );

    expect(clockIsSuspect(s)).toBe(true);
    expect(s.clockSkewMs).toBe(-60 * 60 * 1000);
  });

  /**
   * ⚠ A slow link must not read as a broken clock. The tolerance is two minutes, matching
   * `ConnectivityProbe.ClockSkewTolerance` — and the measurement starts BEFORE the call, so
   * latency lands inside it rather than being mistaken for drift.
   */
  it("a second of latency is not drift", async () => {
    const s = await checkConnection(
      at("2026-08-21T09:00:00Z"),
      ok({ ok: true, utcNow: "2026-08-21T09:00:01Z", apiVersion: "1.20.0" }),
      () => true,
    );

    expect(clockIsSuspect(s)).toBe(false);
  });

  /** ⚠ A server that does not say is not a server that is wrong. */
  it("no clock in the answer is not suspect", async () => {
    const s = await checkConnection(
      at("2026-08-21T09:00:00Z"),
      ok({ ok: true, apiVersion: "1.20.0" }),
      () => true,
    );

    expect(s.clockSkewMs).toBeNull();
    expect(clockIsSuspect(s)).toBe(false);
  });
});

describe("toneFor", () => {
  /**
   * ⚠ Amber is reserved for "reachable, and cannot serve this till" — a deploy fixes it, not a
   * cable. Colouring one indicator by two unrelated states is how a screen stops being trusted.
   */
  it("only serverTooOld is amber", () => {
    expect(toneFor("online")).toBe("good");
    expect(toneFor("serverTooOld")).toBe("warn");
    expect(toneFor("noServer")).toBe("bad");
    expect(toneFor("noNetwork")).toBe("bad");
  });
});
