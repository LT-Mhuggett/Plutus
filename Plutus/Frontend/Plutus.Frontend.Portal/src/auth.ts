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

/** Scopes carried by the current token. Handles both shapes: the password-mode CompactToken
 *  (payload in segment [0], space-separated "Scope") and an OIDC JWT (segment [1], "scope"). */
function currentScopes(): string[] {
  const token = accessToken();
  if (!token) return [];
  for (const seg of [0, 1]) {
    try {
      const part = token.split(".")[seg];
      if (!part) continue;
      const b = part.replace(/-/g, "+").replace(/_/g, "/");
      const json = JSON.parse(decodeURIComponent(escape(atob(b.padEnd(b.length + ((4 - (b.length % 4)) % 4), "=")))));
      const raw = json.Scope ?? json.scope;
      if (typeof raw === "string") return raw.split(" ").filter(Boolean);
      if (Array.isArray(raw)) return (raw as string[]).filter(Boolean);
    } catch {
      /* try the next segment */
    }
  }
  return [];
}

/** WP13.4: reveal the Platform section only for operator tokens carrying platform-admin.
 *  (The backend 403s every /api/v1/platform/* route regardless — this is UI only.) */
export function isPlatformAdmin(): boolean {
  return currentScopes().includes("platform-admin");
}

export function signOut(): void {
  if (oidcMode) {
    void oidcLogout(); // redirects to the IdP end-session endpoint
    return;
  }
  clearSession();
  window.location.reload();
}
