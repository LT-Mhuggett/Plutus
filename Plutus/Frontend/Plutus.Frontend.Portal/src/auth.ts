// Phase 9 (WP9.4): one auth facade over both modes so pages/api never branch on the provider.
//  - password mode (VITE_AUTH_MODE unset/"password"): the existing HMAC login (test env).
//  - oidc mode (VITE_AUTH_MODE="oidc"): Keycloak/Entra auth-code + PKCE, token in memory.

import { clearSession, getSession, setSession, type Session } from "./session.ts";
import { getAccessToken as oidcToken, logout as oidcLogout, oidcConfigured, oidcMode } from "./oidc.ts";

const OPERATOR_STASH = "plutus.portal.session.operator";

export { oidcMode, oidcConfigured };

/** The bearer token for API calls. The portal is email-first, so BOTH session kinds can occur in
 *  one build: an OIDC login holds its token in memory; a password login stores a Session. Prefer
 *  the in-memory OIDC token (the active kind right after a Keycloak sign-in), else the stored one. */
export function accessToken(): string | null {
  return oidcToken() ?? getSession()?.token ?? null;
}

/** True when the current session came from the IdP (Keycloak) — used to show self-service links
 *  like "Account & MFA" that only make sense for an OIDC login. */
export function isOidcSession(): boolean {
  return oidcToken() != null;
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
  const c = tokenClaims();
  const raw = c?.Scope ?? c?.scope;
  const scopes =
    typeof raw === "string" ? raw.split(" ").filter(Boolean)
    : Array.isArray(raw) ? (raw as string[]).filter(Boolean)
    : [];
  // WP18.1: a Keycloak JWT carries realm roles in realm_access.roles (the backend maps the
  // platform-admin role to the scope server-side; mirrored here for the UI-only tab check).
  const roles = (c?.realm_access as { roles?: unknown[] } | undefined)?.roles;
  if (Array.isArray(roles)) scopes.push(...roles.filter((r): r is string => typeof r === "string"));
  return scopes;
}

/** OP1: a PURE operator — platform-admin with no tenant identity (no tid). Such a session sees
 *  the operator portal ONLY; the client tabs are hidden and the backend 403s their tenant APIs.
 *  Impersonation tokens carry a Tid, so they are NOT operator-only (client view returns). Handles
 *  both token casings: HMAC CompactToken uses `Tid`, an OIDC JWT uses `tid`/none. */
export function isOperatorOnly(): boolean {
  if (!isPlatformAdmin()) return false;
  const c = tokenClaims();
  return !(c?.tid ?? c?.Tid);
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

/**
 * End the session and put the user back on the login screen.
 *
 * ⚠⚠ IT RELOADS THE PAGE, WHICH IS WHY THE GUARD BELOW EXISTS — added 2026-08-22 after this
 * function took the whole portal down. `api.ts` calls `signOut()` on ANY 401, so a single authed
 * call made before sign-in becomes: login screen → 401 → reload → login screen, for ever. It was a
 * boot-time `fetchVatPeriods()` that did it, but ANY authed call on the login screen would.
 *
 * ⚠ SIGNING OUT OF NOTHING IS NOT A SIGN-OUT. If there is no session, the user is already where a
 * sign-out would send them, so the reload achieves nothing and costs everything. Clear and return.
 * This is the belt to the App-side braces: the fix there stops today's loop, this stops the next.
 */
export function signOut(): void {
  // ⚠ No session → nothing to sign out of, and reloading would only re-run whatever 401'd.
  if (!getSession() && !isOidcSession()) { clearSession(); return; }
  if (isOidcSession()) {
    void oidcLogout(); // redirects to the IdP end-session endpoint
    return;
  }
  clearSession();
  window.location.reload();
}

/** FE5.3: does this session hold `inventory.bulk`? UI-only — the backend gates every bulk
 *  endpoint on the same permission regardless, so hiding the toolbar is convenience, not
 *  security. (Same pattern as isPlatformAdmin above.) */
export function canBulkEditInventory(): boolean {
  return currentScopes().includes("inventory.bulk");
}

/** Can this operator actually sell? Drives the "Switch to Till" button — a pure back-office
 *  or platform user has no till to switch to. */
export function canUseTill(): boolean {
  return currentScopes().includes("pos.sell");
}

/**
 * The till URL for this deployment. Convention: the portal is the same host with an `admin.`
 * prefix (plutus.example / admin.plutus.example), so it's derived rather than configured —
 * VITE_TILL_URL overrides where that doesn't hold. Null on localhost / bare IPs (dev).
 */
export function tillUrl(): string | null {
  const configured = import.meta.env?.VITE_TILL_URL as string | undefined;
  if (configured) return configured.replace(/\/$/, "");
  const { protocol, host } = window.location;
  if (!host || host.startsWith("localhost") || /^\d+\.\d+\.\d+\.\d+/.test(host)) return null;
  return host.startsWith("admin.") ? `${protocol}//${host.slice("admin.".length)}` : null;
}
