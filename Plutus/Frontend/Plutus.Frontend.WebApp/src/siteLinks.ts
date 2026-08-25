/**
 * Links from the till to its sibling sites.
 *
 * ⚠⚠ CONFIGURED, NOT DERIVED. Matt, 2026-08-25: the till moves to its own subdomain and the login
 * screen needs a **"Switch to portal"**. It is tempting to compute the portal's host by swapping the
 * first label of this one — `till.plutus…` → `admin.plutus…` — and that rule is wrong the moment a
 * tenant is served from anywhere else, on the one screen somebody uses when they are already stuck.
 *
 * ⚠ Unset returns `null` and the caller **hides the link**. A missing link is recoverable; a dead one
 * on a login screen is somebody phoning the shop.
 *
 * ⚠ This is a deliberate small copy of the landing site's `appUrls()` (`Plutus.Frontend.Landing/
 * src/api.ts`) — same validation, same hide-when-unset contract. They are separate Vite apps with no
 * shared package, so the alternative to a copy is a build-time import across app boundaries. If a
 * third surface needs it, that is the moment to make it shared rather than the moment to copy again.
 */

/** Accept only an absolute http(s) URL; anything else is treated as unset. */
export function externalUrl(raw: string | undefined): string | null {
  const s = raw?.trim();
  return s && /^https?:\/\//i.test(s) ? s.replace(/\/+$/, "") : null;
}

/** Where the management portal lives, or `null` when the build did not say. */
export function portalUrl(): string | null {
  return externalUrl(import.meta.env?.VITE_PORTAL_URL);
}
