// Phase 9 (WP9.4): hand-rolled OIDC auth-code + PKCE client (no library, per project
// discipline). Provider-agnostic — endpoints are read from the IdP's discovery document, so
// the same code serves Keycloak and Entra External ID; only authority + clientId differ.
//
// Security posture (the plan): the access token lives ONLY in memory (never localStorage), so a
// stolen disk / XSS-readable store yields nothing persistent. On reload the token is gone and we
// bounce through the IdP again — seamless when the IdP SSO session cookie is still valid (that
// cookie, set by the IdP, is the "refresh via cookie"). While the tab lives, a refresh_token
// (public-client, returned by Keycloak) renews the access token in the background with no redirect.

export interface OidcUser {
  name: string;
  email: string;
  accessToken: string;
}

interface TokenResponse {
  access_token: string;
  refresh_token?: string;
  id_token?: string;
  expires_in: number;
}

interface Discovery {
  authorization_endpoint: string;
  token_endpoint: string;
  end_session_endpoint?: string;
}

const AUTHORITY = import.meta.env.VITE_OIDC_AUTHORITY ?? "";
const CLIENT_ID = import.meta.env.VITE_OIDC_CLIENT_ID ?? "";
const SCOPE = "openid profile email";
const VERIFIER_KEY = "plutus.oidc.verifier";
const STATE_KEY = "plutus.oidc.state";

export const oidcMode = (import.meta.env.VITE_AUTH_MODE ?? "password") === "oidc";

// In-memory only.
let accessToken: string | null = null;
let idToken: string | null = null;
let refreshToken: string | null = null;
let expiresAtMs = 0;
let renewTimer: number | undefined;
let discovery: Discovery | null = null;

export const getAccessToken = (): string | null => accessToken;

async function discover(): Promise<Discovery> {
  if (discovery) return discovery;
  const res = await fetch(`${AUTHORITY.replace(/\/$/, "")}/.well-known/openid-configuration`);
  if (!res.ok) throw new Error(`OIDC discovery failed (${res.status}).`);
  discovery = (await res.json()) as Discovery;
  return discovery;
}

const b64url = (bytes: ArrayBuffer | Uint8Array): string => {
  const arr = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  let s = "";
  for (const b of arr) s += String.fromCharCode(b);
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
};

const randomString = () => b64url(crypto.getRandomValues(new Uint8Array(32)));

async function challengeFor(verifier: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier));
  return b64url(digest);
}

const redirectUri = () => `${window.location.origin}/`;

function claims(jwt: string): Record<string, unknown> {
  try {
    const payload = jwt.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    return JSON.parse(decodeURIComponent(escape(atob(payload))));
  } catch {
    return {};
  }
}

/** Redirect the browser to the IdP to sign in. */
export async function beginLogin(): Promise<void> {
  const { authorization_endpoint } = await discover();
  const verifier = randomString();
  const state = randomString();
  sessionStorage.setItem(VERIFIER_KEY, verifier);
  sessionStorage.setItem(STATE_KEY, state);
  const params = new URLSearchParams({
    response_type: "code",
    client_id: CLIENT_ID,
    redirect_uri: redirectUri(),
    scope: SCOPE,
    state,
    code_challenge: await challengeFor(verifier),
    code_challenge_method: "S256",
  });
  window.location.assign(`${authorization_endpoint}?${params}`);
}

function apply(tok: TokenResponse): OidcUser {
  accessToken = tok.access_token;
  idToken = tok.id_token ?? null;
  refreshToken = tok.refresh_token ?? refreshToken;
  expiresAtMs = Date.now() + tok.expires_in * 1000;
  scheduleRenew();
  const c = idToken ? claims(idToken) : {};
  return {
    accessToken: tok.access_token,
    name: (c.name as string) ?? (c.preferred_username as string) ?? (c.email as string) ?? "Signed in",
    email: (c.email as string) ?? "",
  };
}

async function exchange(body: Record<string, string>): Promise<TokenResponse> {
  const { token_endpoint } = await discover();
  const res = await fetch(token_endpoint, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: CLIENT_ID, ...body }),
  });
  if (!res.ok) throw new Error(`Token exchange failed (${res.status}).`);
  return (await res.json()) as TokenResponse;
}

function scheduleRenew() {
  if (renewTimer) clearTimeout(renewTimer);
  if (!refreshToken) return;
  // Renew 60s before expiry.
  const delay = Math.max(5_000, expiresAtMs - Date.now() - 60_000);
  renewTimer = window.setTimeout(() => void renew(), delay);
}

async function renew(): Promise<void> {
  if (!refreshToken) return;
  try {
    apply(await exchange({ grant_type: "refresh_token", refresh_token: refreshToken }));
  } catch {
    // Refresh failed (session ended) — drop to a fresh redirect login.
    accessToken = null;
    await beginLogin();
  }
}

/**
 * Called once at startup. If we're returning from the IdP (?code=&state=), completes the code
 * exchange and returns the user. Otherwise returns null (caller then calls beginLogin()).
 */
export async function completeLoginIfCallback(): Promise<OidcUser | null> {
  const url = new URL(window.location.href);
  const code = url.searchParams.get("code");
  const state = url.searchParams.get("state");
  if (!code || !state) return null;

  const expectedState = sessionStorage.getItem(STATE_KEY);
  const verifier = sessionStorage.getItem(VERIFIER_KEY);
  sessionStorage.removeItem(STATE_KEY);
  sessionStorage.removeItem(VERIFIER_KEY);
  // Strip the auth params from the address bar either way.
  window.history.replaceState({}, document.title, redirectUri());

  if (state !== expectedState || !verifier) throw new Error("OIDC state mismatch — please sign in again.");

  const user = apply(await exchange({
    grant_type: "authorization_code",
    code,
    redirect_uri: redirectUri(),
    code_verifier: verifier,
  }));
  return user;
}

export async function logout(): Promise<void> {
  const hint = idToken;
  accessToken = idToken = refreshToken = null;
  if (renewTimer) clearTimeout(renewTimer);
  const d = await discover().catch(() => null);
  if (d?.end_session_endpoint) {
    const params = new URLSearchParams({ post_logout_redirect_uri: redirectUri(), client_id: CLIENT_ID });
    if (hint) params.set("id_token_hint", hint);
    window.location.assign(`${d.end_session_endpoint}?${params}`);
  } else {
    window.location.reload();
  }
}
