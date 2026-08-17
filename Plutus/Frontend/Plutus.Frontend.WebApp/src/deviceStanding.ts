// W-P1 — is this till still allowed to be a till?
//
// ⚠⚠ THE C2 TWIN of `src/Plutus.Client.Core/DeviceRevocation.cs`, which MAUI uses. Every rule below
// is deliberately identical to it, and its .NET tests (`DeviceRevocationTests.cs`, 19 cases) are
// mirrored in `deviceStanding.test.ts`. Two tills that disagree about whether a device is finished
// is the worst possible pair of answers: one shop keeps trading on a stolen machine.
//
// ⚠⚠ WHY IT MUST BE POLLED AT ALL. Device tokens are HMAC bearer tokens with NO server-side
// denylist, so revoking a device in the portal does not invalidate the token it already holds: a
// lost or stolen till keeps selling for up to its 12h TTL unless it asks. This is the only
// revocation signal that reaches a till.
//
// ⚠ ASKED VIA DEVICE STATUS, NEVER BY MINTING A TOKEN. The token endpoint is rate-limited to 5/min
// per IP, so polling that would make a healthy till report itself revoked (429) — and in a shop
// where several tills share one public IP, they would do it to each other.
//
// ⚠ Until 2026-08-17 the web till's whole notion of connection was `navigator.onLine` — the network
// INTERFACE, not the server. It said "online" in a shop whose broadband was down and could not tell
// a revoked till from a dead one.

import { headers } from "./api.ts";
import { getDeviceCredential, getDeviceToken } from "./pipeline.ts";
import { getTillState, putTillState, clearTillState } from "./offline.ts";

export type DeviceStanding = "trading" | "removalRequested" | "revoked";

/** ⚠ Names the TILL, not the account — otherwise somebody tries another login, then another, and
 *  concludes the staff accounts are broken. Verbatim the same sentence MAUI shows
 *  (`DeviceRevocation.RevokedMessage`). */
export const REVOKED_MESSAGE =
  "This till has been removed in Plutus and can no longer be used. " +
  "Speak to your manager — it can be re-enrolled from the portal.";

/**
 * Decide from one device-status poll.
 *
 * ⚠⚠ ONLY AN EXPLICIT "Revoked" STOPS A TILL. Nothing else here does, and the omissions are
 * deliberate:
 *
 *  • 401/403 are also what a clock-skewed till, a mis-issued token or a misconfigured gateway
 *    produce. Stopping a shop on an auth blip is the outage this exists to avoid.
 *  • 404 looks terminal and is not reliably so — it is equally "wrong device id" or a tenant
 *    filter that did not match, and a routing mistake would close every till at once.
 *    ⚠ Approving a removal sets `Status = Revoked`; it does NOT delete the row
 *    (`TillsController.DecideRemoval`), so a genuinely un-enrolled till DOES get the explicit
 *    answer and is caught by it.
 *  • A failed poll (code 0 / no body) is "we could not ask", never "you are finished".
 *
 * ⚠⚠ `PendingRemoval` KEEPS TRADING. Somebody has requested the till back and nobody has approved
 * it; halting on an unapproved *request* would turn un-enrolment into a way to take a shop down.
 */
export function checkStanding(code: number, body: { status?: string | null } | null): DeviceStanding {
  if (code !== 200 || body == null) return "trading";

  const status = (body.status ?? "").trim().toLowerCase();
  if (status === "revoked") return "revoked";
  if (status === "pendingremoval") return "removalRequested";

  // ⚠ A status this build has not been taught keeps trading. Guessing that an unknown word means
  // "stop" would let a backend rename close every till in the estate at once.
  return "trading";
}

/** ⚠ The only standing that stops a till. Named so no caller writes the comparison itself and
 *  quietly folds `removalRequested` into it. */
export const mustStop = (standing: DeviceStanding): boolean => standing === "revoked";

// ── the poll, and the latch ─────────────────────────────────────────────────

/** Has this till been explicitly revoked? ⚠ Read from IndexedDB, so it SURVIVES A RELOAD — a
 *  revoked till must not come back by pressing F5. */
export async function isRevoked(): Promise<boolean> {
  return (await getTillState<boolean>("deviceRevoked")) === true;
}

/**
 * Ask the platform where this till stands, and latch a revocation.
 *
 * Returns the standing, or null when there was nothing to ask about (un-enrolled) — which is a
 * legitimate state for a browser till and must not be treated as a fault.
 *
 * ⚠ NEVER THROWS. A poll that fails is a till that carries on.
 */
export async function pollDeviceStanding(): Promise<DeviceStanding | null> {
  const cred = getDeviceCredential();
  if (!cred?.deviceId) return null; // not enrolled — nothing to be revoked

  try {
    // ⚠ The DEVICE token: this endpoint is gated `sales.ingest`, the same gate the heartbeat uses,
    // so the check works with nobody signed in — which is exactly when a stolen till is revoked.
    const token = await getDeviceToken();
    const res = await fetch(`/api/v1/tills/devices/${cred.deviceId}/status`, {
      headers: { ...headers(), Authorization: `Bearer ${token}` },
    });

    let body: { status?: string | null } | null = null;
    try {
      if (res.ok) body = (await res.json()) as { status?: string | null };
    } catch {
      body = null; // answered, but not with anything usable
    }

    const standing = checkStanding(res.status, body);

    if (mustStop(standing)) await putTillState("deviceRevoked", true);
    // ⚠ AND IT CLEARS on any other explicit answer, so re-enrolling in the portal un-blocks the
    // till without somebody clearing browser storage by hand.
    else if (res.ok && body != null) await clearTillState("deviceRevoked");

    return standing;
  } catch {
    // ⚠ Offline, or the token could not be minted. Carry on trading — see `checkStanding`.
    return "trading";
  }
}
