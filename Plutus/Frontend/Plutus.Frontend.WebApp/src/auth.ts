// Phase 9 (WP9.4): one auth facade over both operator-login modes so api/pages never branch.
//  - password mode (VITE_AUTH_MODE unset/"password"): the existing HMAC login (test env).
//  - oidc mode (VITE_AUTH_MODE="oidc"): Keycloak/Entra auth-code + PKCE, token in memory.
// The device enrolment token (pipeline.ts) is separate and unaffected by either mode.

import { clearSession, getSession } from "./session.ts";
import { getAccessToken as oidcToken, logout as oidcLogout, oidcMode } from "./oidc.ts";

export { oidcMode };

/** The operator bearer token for API calls, whichever mode is active (null when signed out). */
/**
 * ⚠⚠ HAS A TOKEN EVER EXISTED ON THIS PAGE? — added 2026-08-23, and it is the difference between
 * two situations that look identical to `getSession()`.
 *
 * `signOut()` reloads, and it must: an expired session has to put the operator back at the login
 * screen. But it must NOT reload when nobody has signed in yet, or a stray authed call on the login
 * screen becomes login → 401 → reload → login, for ever.
 *
 * ⚠ THE FIRST ATTEMPT AT THAT GUARD USED `!getSession()` AND WAS WRONG IN THE WORST WAY.
 * `getSession()` returns **null for an EXPIRED session** — it deletes it and answers null — so the
 * guard fired precisely when an operator's token died mid-shift. Instead of being returned to the
 * login screen they were left looking at a live-looking " . $app . " whose every call answered 401.
 * Matt hit it retrieving a parked basket: *"Error: API 401"*.
 *
 * ⚠ So the question is not "is there a session" but "was there ever one on this page". A 401 after a
 * token has been in play means it died → reload. A 401 with no token ever → we are already at the
 * login screen → do not reload.
 */
let everHadAToken = false;

export function accessToken(): string | null {
  // ⚠ THE ONLY PLACE EVERY TOKEN PASSES THROUGH, which is why the latch lives here rather than at
  // each sign-in path. It records that authentication has happened at least once on this page — see
  // `everHadAToken` for the bug that makes the distinction matter.
  const token = oidcMode ? oidcToken() : (getSession()?.token ?? null);
  if (token) everHadAToken = true;
  return token;
}

/**
 * End the session and restart at the login screen.
 *
 * ⚠⚠ THE GUARD IS NOT DECORATION — added 2026-08-22 after the identical shape took the PORTAL down
 * for every user. `api.ts` calls `signOut()` on any 401, and `signOut()` reloads, so one authed
 * call made before sign-in becomes login → 401 → reload → login, for ever. On the portal it was a
 * boot-time timezone fetch; on a till the same mistake would stop a shop selling.
 *
 * ⚠ Signing out of nothing is not a sign-out: with no session the operator is already where the
 * reload would put them, so it re-runs whatever 401'd and achieves nothing else.
 */
export function signOut(): void {
  // ⚠ No session → nothing to sign out of, and reloading would only re-run whatever 401'd.
  if (!everHadAToken) { clearSession(); return; }
  if (oidcMode) {
    void oidcLogout(); // redirects to the IdP end-session endpoint
    return;
  }
  clearSession();
  window.location.reload();
}
