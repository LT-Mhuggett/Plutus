/// <reference types="vite/client" />

interface ImportMetaEnv {
  // Phase 9: "password" (default, HMAC login) | "oidc" (Keycloak/Entra PKCE).
  readonly VITE_AUTH_MODE?: "password" | "oidc";
  readonly VITE_OIDC_AUTHORITY?: string; // e.g. https://login.plutus.huggett.dscloud.me/realms/plutus
  readonly VITE_OIDC_CLIENT_ID?: string; // e.g. plutus-webpos
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
