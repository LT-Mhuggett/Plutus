/// <reference types="vite/client" />

interface ImportMetaEnv {
  // Phase 9: "password" (default, HMAC login) | "oidc" (Keycloak/Entra PKCE).
  readonly VITE_AUTH_MODE?: "password" | "oidc";
  /** The badge in the app bar: any text shows it, unset/empty shows nothing. `test` on the test
   *  deploy; ⚠ deliberately UNSET in production, so a forgotten setting cannot label a live till. */
  readonly VITE_ENV_BADGE?: string;
  readonly VITE_OIDC_AUTHORITY?: string; // e.g. https://login.plutus.huggett.dscloud.me/realms/plutus
  readonly VITE_OIDC_CLIENT_ID?: string; // e.g. plutus-webpos
  /** Where the management portal lives, for the login screen's "Switch to portal" (2026-08-25).
   *  ⚠ Absolute http(s) or it is ignored — see `sibling.ts`. Unset HIDES the link, deliberately:
   *  a dead link on a login screen is offered to somebody who is already stuck.
   *  e.g. https://admin.plutus.huggett.dscloud.me */
  readonly VITE_PORTAL_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
