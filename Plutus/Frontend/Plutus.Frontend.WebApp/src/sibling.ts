// ─────────────────────────────────────────────────────────────────────────────
// Jumping between the two Plutus web apps (till ⇄ portal).
//
// ⚠⚠ THE DERIVATION CHANGED 2026-08-25 AND THE OLD ONE IS NOW ACTIVELY WRONG.
// It used to be: the portal is the same host with an `admin.` prefix, so from the till at
// `plutus.example` you get `admin.plutus.example`. The till then MOVED to its own subdomain —
// `till.plutus.example` — and the old rule produced **`admin.till.plutus.example`**, a host that
// does not exist, on a button whose whole job is to get somebody out of the till.
//
// ⚠ The live build sets `VITE_PORTAL_URL`, so it was configured rather than derived and nothing
// broke. That is luck, not design: the derivation is the fallback, and a fallback that is confidently
// wrong is worse than one that returns null.
//
// ⚠ Its twin is the PORTAL's `auth.ts tillUrl()`, which had the mirror-image fault the same day and
// was fixed the same way (Matt found that one by clicking it). If you change the host layout, change
// both — they are the two ends of the same journey.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * ⚠ A configured value must be an absolute http(s) URL or it is ignored. Anything else — a bare
 * hostname, a path, a `javascript:` — would end up in an `href`, and a dead or hostile link on a
 * till is offered to somebody who is already stuck.
 */
function fromConfig(raw: string | undefined): string | null {
  const s = raw?.trim();
  return s && /^https?:\/\//i.test(s) ? s.replace(/\/+$/, "") : null;
}

/**
 * The derivation, pure so it can be tested without a browser — `window.location` does not exist in
 * the test environment, and this is the half that got the hostname wrong.
 */
export function portalHostFor(protocol: string, host: string): string | null {
  if (!host || host.startsWith("localhost") || /^\d+\.\d+\.\d+\.\d+/.test(host)) return null; // dev / bare IP

  // Already the portal.
  if (host.startsWith("admin.")) return `${protocol}//${host}`;
  // The till on its own subdomain: till.X → admin.X.
  if (host.startsWith("till.")) return `${protocol}//admin.${host.slice("till.".length)}`;

  // ⚠ NULL, not a guess. The bare host is the public LANDING page now, so "prepend admin." is no
  // longer a safe assumption about an unknown host — and the caller hides the button when this is
  // null, which is the recoverable outcome.
  return null;
}

/** The portal URL for this deployment, or null when it can't be worked out. */
export function portalUrl(): string | null {
  const configured = fromConfig(import.meta.env?.VITE_PORTAL_URL as string | undefined);
  if (configured) return configured;
  const { protocol, host } = window.location;
  return portalHostFor(protocol, host);
}

/** Exported for the tests — the validation a configured value has to survive. */
export const configuredPortalUrl = fromConfig;

/** Does the signed-in operator have anything to do in the portal? Any portal.* permission
 *  means the portal will let them in with something to see; otherwise the button is noise. */
export function hasPortalAccess(scopes: string[]): boolean {
  return scopes.some((s) => s.startsWith("portal.") || s === "platform-admin");
}
