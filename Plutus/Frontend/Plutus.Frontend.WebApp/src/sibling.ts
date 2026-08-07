// ─────────────────────────────────────────────────────────────────────────────
// Jumping between the two Plutus web apps (till ⇄ portal).
//
// Deployment convention: the portal is the SAME host with an `admin.` prefix
// (plutus.example / admin.plutus.example), so the sibling URL is derived rather
// than configured — one less thing to get wrong per environment. VITE_PORTAL_URL
// overrides it where that convention doesn't hold.
// ─────────────────────────────────────────────────────────────────────────────

/** The portal URL for this deployment, or null when it can't be worked out. */
export function portalUrl(): string | null {
  const configured = import.meta.env?.VITE_PORTAL_URL as string | undefined;
  if (configured) return configured.replace(/\/$/, "");
  const { protocol, host } = window.location;
  if (!host || host.startsWith("localhost") || /^\d+\.\d+\.\d+\.\d+/.test(host)) return null; // dev / bare IP
  return host.startsWith("admin.") ? `${protocol}//${host}` : `${protocol}//admin.${host}`;
}

/** Does the signed-in operator have anything to do in the portal? Any portal.* permission
 *  means the portal will let them in with something to see; otherwise the button is noise. */
export function hasPortalAccess(scopes: string[]): boolean {
  return scopes.some((s) => s.startsWith("portal.") || s === "platform-admin");
}
