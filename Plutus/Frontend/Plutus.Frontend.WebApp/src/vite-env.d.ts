/// <reference types="vite/client" />

interface ImportMetaEnv {
  // Phase 9: "password" (default, HMAC login) | "oidc" (Keycloak/Entra PKCE).
  readonly VITE_AUTH_MODE?: "password" | "oidc";
  /** The badge in the app bar: any text shows it, unset/empty shows nothing. `test` on the test
   *  deploy; ⚠ deliberately UNSET in production, so a forgotten setting cannot label a live till. */
  readonly VITE_ENV_BADGE?: string;
  readonly VITE_OIDC_AUTHORITY?: string; // e.g. https://login.plutus.huggett.dscloud.me/realms/plutus
  readonly VITE_OIDC_CLIENT_ID?: string; // e.g. plutus-webpos
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
