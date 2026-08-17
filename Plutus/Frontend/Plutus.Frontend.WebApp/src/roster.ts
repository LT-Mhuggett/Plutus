// W-P2 — this till's operator roster, cached, and the rule that puts a disabled operator out.
//
// ⚠⚠ THIS IS THE SPINE. W-P3 (the discount ceiling) reads its grants, W-P4 (offline sign-in) reads
// its credential hashes and `asOfUtc`, and W-P5's reopen gate reads its grants too. Everything below
// is written to be read by those three, not just by the revocation check.
//
// ⚠⚠ WHY THE WEB TILL NEEDED THIS. It signed an operator out only REACTIVELY — `api.ts` `handle401`,
// `if (res.status === 401) signOut()` — and login tokens are cached for 12 HOURS carrying their
// permission set. So a disabled operator kept a working session until something happened to 401.
// MAUI has dropped them inside 60 seconds since till 1.41.0. Matt, 2026-08-11: *"If a user is
// disabled, the user needs immediately logging out with an information message."*
//
// ⚠ The server already refuses a deactivated employee at LOGIN (AuthController, FE9.2 fix
// 2026-08-08). The gap this closes is the operator who was already signed in when they were
// disabled — which is the case that actually happens.

import { headers } from "./api.ts";
import { getDeviceCredential, getDeviceToken } from "./pipeline.ts";
import { getTillState, putTillState } from "./offline.ts";

/** One grant on an operator. ⚠ Mirrors `OperatorGrantDto` — W-P3 resolves ceilings from these. */
export interface OperatorGrant {
  code: string;
  maxPence: number | null;
  validFromUtc: string | null;
  validToUtc: string | null;
  daysOfWeekMask: number | null;
  windowStartLocal: string | null;
  windowEndLocal: string | null;
}

/** ⚠ `credentialHashBase64` is a PBKDF2 hash, never a password — but it IS the operator's platform
 *  credential, which is why W-P4's staleness horizons exist to bound how long it is trusted. */
export interface TillOperator {
  userId: string;
  displayName: string;
  email: string | null;
  credentialHashBase64: string | null;
  credentialSaltBase64: string | null;
  grants: OperatorGrant[];
}

/**
 * The whole envelope, as the wire sent it.
 *
 * ⚠⚠ STORED AND PASSED AROUND WHOLE, never as a bare operator array. `asOfUtc` is roster-level and is
 * the **SERVER's** clock by contract — *"a till with a wrong clock would otherwise decide its own
 * credentials were fresh for ever"* — and W-P4 measures every staleness horizon from it. Flattening
 * this to rows would silently substitute the browser's clock.
 */
export interface OperatorRoster {
  tillId: string;
  asOfUtc: string;
  operators: TillOperator[];
}

/** ⚠ Matt's wording, verbatim (2026-08-11). It names the ACCOUNT, not the till — *"you have been
 *  logged out"* sends somebody to reboot the machine instead of to their manager. */
export const DISABLED_MESSAGE = "Your account has been disabled, please speak to your manager";

/**
 * Is the signed-in operator still allowed at this till?
 *
 * ⚠⚠ THE LOAD-BEARING RULE, copied exactly from `Client.Core/OperatorRevocation.Check`:
 * **a roster that could not be fetched is NOT an empty roster.**
 *
 * `null` means "we could not ask" and must be treated as "carry on". If it were treated as "not on
 * the list", **every dropped connection would sign the whole shop out mid-sale**, with a message
 * accusing the operator of being disabled — a worse outage than the one this prevents, and it would
 * happen on the flakiest sites first.
 *
 * ⚠ An **empty** roster the server actually SENT is a real answer: nobody is assigned to this till
 * any more, and that does sign the operator out, correctly.
 *
 * ⚠ Nobody signed in → nothing to revoke. The login path re-reads the roster on its way in.
 */
export function isStillPermitted(signedInUserId: string | null | undefined, roster: OperatorRoster | null): boolean {
  if (!signedInUserId) return true;
  if (roster == null) return true; // ⚠⚠ see above — no answer is not an empty answer

  const operators = roster.operators ?? [];

  // ⚠ Case-insensitive: a Guid may arrive in either case from either end, and a casing mismatch
  // would sign out every operator in the shop.
  const wanted = signedInUserId.toLowerCase();
  return operators.some((o) => (o.userId ?? "").toLowerCase() === wanted);
}

/** The cached roster, or null if there has never been one. ⚠ W-P4 signs people in offline from this. */
export const cachedRoster = (): Promise<OperatorRoster | null> =>
  getTillState<OperatorRoster>("operatorRoster").then((r) => r ?? null);

/**
 * Fetch the roster and cache it. Returns null when it could not be asked — ⚠ which callers must
 * pass straight to `isStillPermitted`, NOT flatten to an empty roster.
 *
 * ⚠ Never throws.
 */
export async function refreshRoster(): Promise<OperatorRoster | null> {
  const cred = getDeviceCredential();
  if (!cred?.tillId) return null; // not enrolled — no roster to fetch

  try {
    // ⚠ The DEVICE token. This endpoint is gated `sales.ingest`, so the roster can be refreshed with
    // nobody signed in — which is what lets W-P4 sign the FIRST person in offline.
    const token = await getDeviceToken();
    const res = await fetch(`/api/v1/tills/${cred.tillId}/operators`, {
      headers: { ...headers(), Authorization: `Bearer ${token}` },
    });
    if (!res.ok) return null;

    const roster = (await res.json()) as OperatorRoster;

    // ⚠ Only cache something that looks like the envelope. A truncated body would otherwise replace a
    // good roster with rubbish, and offline sign-in depends on this cache.
    if (!roster || typeof roster.asOfUtc !== "string" || !Array.isArray(roster.operators)) return null;

    await putTillState("operatorRoster", roster);
    return roster;
  } catch {
    return null; // offline, or the token could not be minted
  }
}
