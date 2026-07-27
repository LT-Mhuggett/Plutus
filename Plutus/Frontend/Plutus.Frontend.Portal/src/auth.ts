// Phase 9 (WP9.4): one auth facade over both modes so pages/api never branch on the provider.
//  - password mode (VITE_AUTH_MODE unset/"password"): the existing HMAC login (test env).
//  - oidc mode (VITE_AUTH_MODE="oidc"): Keycloak/Entra auth-code + PKCE, token in memory.

import { clearSession, getSession } from "./session.ts";
import { getAccessToken as oidcToken, logout as oidcLogout, oidcMode } from "./oidc.ts";

export { oidcMode };

/** The bearer token for API calls, whichever mode is active (null when signed out). */
export function accessToken(): string | null {
  return oidcMode ? oidcToken() : (getSession()?.token ?? null);
}

export function signOut(): void {
  if (oidcMode) {
    void oidcLogout(); // redirects to the IdP end-session endpoint
    return;
  }
  clearSession();
  window.location.reload();
}
