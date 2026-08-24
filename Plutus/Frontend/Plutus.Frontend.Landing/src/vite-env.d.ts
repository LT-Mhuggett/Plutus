/// <reference types="vite/client" />

declare const __APP_VERSION__: string;
declare const __BUILD_TIME__: string;

interface ImportMetaEnv {
  /** Where the web till lives. ⚠ Unset in production — `tillUrl()` derives it from the hostname,
   *  the same convention the portal already uses. Set it for local dev. */
  readonly VITE_TILL_URL?: string;
  /** Where the portal lives. Same rule. */
  readonly VITE_PORTAL_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
