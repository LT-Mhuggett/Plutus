// Phase 9 (WP9.4): one auth facade over both modes so pages/api never branch on the provider.
//  - password mode (VITE_AUTH_MODE unset/"password"): the existing HMAC login (test env).
//  - oidc mode (VITE_AUTH_MODE="oidc"): Keycloak/Entra auth-code + PKCE, token in memory.

import { clearSession, getSession, setSession, type Session } from "./session.ts";
import { getAccessToken as oidcToken, logout as oidcLogout, oidcMode } from "./oidc.ts";

const OPERATOR_STASH = "plutus.portal.session.operator";

export { oidcMode };

/** The bearer token for API calls, whichever mode is active (null when signed out). */
export function accessToken(): string | null {
  return oidcMode ? oidcToken() : (getSession()?.token ?? null);
}

/** The current token's payload claims. Handles both shapes: the password-mode CompactToken
 *  (payload in segment [0]) and an OIDC JWT (payload in segment [1]) — picks the segment that
 *  looks like a payload (a JWT header {alg,typ} is skipped). */
function tokenClaims(): Record<string, unknown> | null {
  const token = accessToken();
  if (!token) return null;
  for (const seg of [0, 1]) {
    try {
      const part = token.split(".")[seg];
      if (!part) continue;
      const b = part.replace(/-/g, "+").replace(/_/g, "/");
      const json = JSON.parse(decodeURIComponent(escape(atob(b.padEnd(b.length + ((4 - (b.length % 4)) % 4), "=")))));
      if (json && typeof json === "object" &&
          (json.Scope || json.scope || json.EmployeeId || json.sub || json.Impersonating !== undefined))
        return json;
    } catch {
      /* try the next segment */
    }
  }
  return null;
}

function currentScopes(): string[] {
  const raw = tokenClaims()?.Scope ?? tokenClaims()?.scope;
  if (typeof raw === "string") return raw.split(" ").filter(Boolean);
  if (Array.isArray(raw)) return (raw as string[]).filter(Boolean);
  return [];
}

/** WP13.4: reveal the Platform section only for operator tokens carrying platform-admin.
 *  (The backend 403s every /api/v1/platform/* route regardless — this is UI only.) */
export function isPlatformAdmin(): boolean {
  return currentScopes().includes("platform-admin");
}

/** WP14.1: the impersonated user's name when the session is an impersonation token, else null. */
export function impersonatingAs(): string | null {
  const c = tokenClaims();
  return c && c.Impersonating === true ? (typeof c.Name === "string" ? c.Name : "user") : null;
}

/** WP14.1: swap the operator's session for an impersonation token (password mode); the operator's
 *  own session is stashed so Stop can restore it. */
export function beginImpersonation(s: Session): void {
  const op = localStorage.getItem("plutus.portal.session");
  if (op) localStorage.setItem(OPERATOR_STASH, op);
  setSession(s);
  window.location.reload();
}

/** WP14.1: end impersonation — restore the operator's stashed session. */
export function stopImpersonation(): void {
  const op = localStorage.getItem(OPERATOR_STASH);
  if (op) { localStorage.setItem("plutus.portal.session", op); localStorage.removeItem(OPERATOR_STASH); }
  else clearSession();
  window.location.reload();
}

export function signOut(): void {
  if (oidcMode) {
    void oidcLogout(); // redirects to the IdP end-session endpoint
    return;
  }
  clearSession();
  window.location.reload();
}
