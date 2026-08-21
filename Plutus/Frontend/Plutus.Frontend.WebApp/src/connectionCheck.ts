// WP16a — "am I connected?", on the LOGIN SCREEN, in the words MAUI already uses.
//
// ⚠⚠ THE C2 TWIN of `src/Plutus.Client.Core/Connectivity.cs` (`ConnectivityProbe`), which MAUI runs
// via `Services.Connectivity.TillConnectionCheck`. Every state, sentence and ordering decision below
// is deliberately identical to it, and `connectionCheck.test.ts` mirrors `ConnectivityProbeTests.cs`
// for the cases this surface can reach.
//
// ⚠⚠ WHY IT EXISTS, 2026-08-21. MAUI's login screen has had a connection badge — three states, a
// colour, a diagnostic line, a clock-skew warning and tap-to-refresh — since WP16a. The web till's
// `LoginPage.tsx` was 111 lines with NO indicator at all, so a shop whose backend was down got a
// failed sign-in indistinguishable from a wrong password. §7 recorded this the wrong way round
// ("0 references on LoginView/LoginViewModel"), because it grepped for `ConnectivityProbe` and MAUI
// reaches it through a wrapper. The gap was always this side.
//
// ⚠⚠ "OFFLINE" IS THREE DIFFERENT FAULTS WEARING ONE WORD, and the person standing at the till is
// the one who has to act on the difference: no network (their cable or wifi), no server (nothing
// they can do at the till), or this till has been revoked (a manager's job, and no amount of
// rebooting the router fixes it). A single red badge sends shops to reboot routers over a portal
// setting.
//
// ⚠ THIS SURFACE ASKS ONLY THE FIRST QUESTION — `verifyIdentity: false`, exactly as
// `TillConnectionCheck` passes on MAUI's login screen. Whether this device is still enrolled is
// `deviceStanding.ts`'s job and belongs where there is something an operator can do about it.
//
// ⚠ NEVER `navigator.onLine` ALONE. It reports the network INTERFACE, and says "online" in a shop
// whose broadband is down. It is used here for step 0 only — as the cheap "there is demonstrably no
// network" shortcut, which is the same role `MauiNetworkAvailability` plays in the .NET probe.

/** ⚠ Same members and same meanings as `Plutus.Client.Core.TillConnection`. */
export type TillConnection =
  /** The device reports no network at all. */
  | "noNetwork"
  /** The network is up and nothing Plutus answered. */
  | "noServer"
  /** Reachable, and too old to serve a till of this generation. */
  | "serverTooOld"
  /** Reachable, current, and answering. */
  | "online";

export interface ConnectionStatus {
  state: TillConnection;
  /** For the operator. */
  summary: string;
  /** For whoever they ring afterwards — status code, error, server version. */
  detail: string | null;
  /** The server's clock minus ours, in ms, or null if it did not say. */
  clockSkewMs: number | null;
}

/** ⚠ Verbatim `ConnectivityProbe.ClockSkewTolerance` — a till an hour out can have a day's takings
 *  judged against a different instant, because device tokens expire and VAT bands are
 *  effective-dated. Nothing else in the app would ever mention it. */
const CLOCK_SKEW_WARNING_MS = 2 * 60 * 1000;

/** ⚠ Matches `ConnectivityProbe.Timeout`. A login screen that hangs on a dead network is a till
 *  nobody can sign into during an outage — which is exactly when a shop most needs to keep
 *  selling. */
const TIMEOUT_MS = 5000;

export const clockIsSuspect = (s: ConnectionStatus): boolean =>
  s.clockSkewMs !== null && Math.abs(s.clockSkewMs) > CLOCK_SKEW_WARNING_MS;

/** The traffic-light for a state. ⚠ Amber is reserved for "reachable, and cannot serve this till" —
 *  a deploy fixes it, not a cable. Same split as `TillConnectionCheck.ColourFor`. */
export const toneFor = (state: TillConnection): "good" | "warn" | "bad" =>
  state === "online" ? "good" : state === "serverTooOld" ? "warn" : "bad";

interface PingBody {
  ok?: boolean;
  utcNow?: string;
  apiVersion?: string | null;
}

/**
 * Check now. ⚠ NEVER THROWS and never waits longer than the timeout — see `TIMEOUT_MS`.
 *
 * @param now injectable clock, so the skew arithmetic is testable without waiting for real time.
 * @param fetchImpl injectable transport, for the same reason.
 */
export async function checkConnection(
  now: () => Date = () => new Date(),
  fetchImpl: typeof fetch = fetch,
  hasNetwork: () => boolean = () => navigator.onLine,
): Promise<ConnectionStatus> {
  // Step 0 — the OS. Skipping the network call when there is demonstrably no network keeps a shop
  // with a dead router off a 5-second timeout on every screen that asks.
  if (!hasNetwork()) {
    return {
      state: "noNetwork",
      summary: "No network — this till is offline.",
      detail: "The device reports no network connection.",
      clockSkewMs: null,
    };
  }

  const startedAt = now();
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);

  try {
    // ⚠ Same-origin and relative, like every other call in `api.ts` — `/api/*` is reverse-proxied
    // to the backend, so there is no base URL to get wrong. ⚠ And NO TOKEN: `/api/v1/ping` is
    // anonymous on purpose, so it answers for a till that has not enrolled or has been revoked.
    const res = await fetchImpl("/api/v1/ping", { signal: controller.signal });

    // ⚠ Reachable but TOO OLD. Stopping here is the honest answer: without /ping this server also
    // has no heartbeat and no catalogue feed, so a confident "Connected" would leave an operator
    // watching every feature 404 with no idea why. The fix is a deploy.
    if (res.status === 404) {
      return {
        state: "serverTooOld",
        summary: "Connected, but this Plutus server is too old for this till.",
        detail:
          "This server predates /api/v1/ping, so it also has no heartbeat or catalogue feed for this till.",
        clockSkewMs: null,
      };
    }

    // ⚠ Still an answer, so still reachable — a 500 or a 502 is a sick server, not an absent one,
    // and "check your cable" would waste the one person who could ring support.
    if (!res.ok) {
      return {
        state: "online",
        summary: "Connected to Plutus.",
        detail: `Server reached but answered HTTP ${res.status}.`,
        clockSkewMs: null,
      };
    }

    let body: PingBody;
    try {
      body = (await res.json()) as PingBody;
    } catch {
      // A captive portal or a proxy returning an HTML login page. Something answered, but it was
      // not Plutus — and to a till that is the same as nothing being there.
      return {
        state: "noServer",
        summary: "Can't reach Plutus — the network is up but the server didn't answer.",
        detail: "Something answered that address, but it was not Plutus.",
        clockSkewMs: null,
      };
    }

    // ⚠ Measured from the START of the call, so a slow link reads as latency rather than drift.
    const skew = body.utcNow ? new Date(body.utcNow).getTime() - startedAt.getTime() : null;

    return {
      state: "online",
      summary: "Connected to Plutus.",
      detail: body.apiVersion ? `Server v${body.apiVersion}` : null,
      clockSkewMs: Number.isFinite(skew as number) ? skew : null,
    };
  } catch (e) {
    // Timeouts, DNS failures, TLS failures, refused connections — all of them mean the same thing
    // to a till: nothing is there.
    return {
      state: "noServer",
      summary: "Can't reach Plutus — the network is up but the server didn't answer.",
      detail:
        (e as Error)?.name === "AbortError"
          ? `No reply within ${TIMEOUT_MS / 1000}s.`
          : `${(e as Error)?.name ?? "Error"}: ${(e as Error)?.message ?? String(e)}`,
      clockSkewMs: null,
    };
  } finally {
    clearTimeout(timer);
  }
}
