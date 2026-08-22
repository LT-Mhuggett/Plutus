// Phase 9 (WP9.4): one auth facade over both operator-login modes so api/pages never branch.
//  - password mode (VITE_AUTH_MODE unset/"password"): the existing HMAC login (test env).
//  - oidc mode (VITE_AUTH_MODE="oidc"): Keycloak/Entra auth-code + PKCE, token in memory.
// The device enrolment token (pipeline.ts) is separate and unaffected by either mode.

import { clearSession, getSession } from "./session.ts";
import { getAccessToken as oidcToken, logout as oidcLogout, oidcMode } from "./oidc.ts";

export { oidcMode };

/** The operator bearer token for API calls, whichever mode is active (null when signed out). */
export function accessToken(): string | null {
  return oidcMode ? oidcToken() : (getSession()?.token ?? null);
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
  if (!oidcMode && !getSession()) { clearSession(); return; }
  if (oidcMode) {
    void oidcLogout(); // redirects to the IdP end-session endpoint
    return;
  }
  clearSession();
  window.location.reload();
}
