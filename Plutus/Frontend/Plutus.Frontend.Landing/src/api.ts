// The landing site's entire server surface.
//
// ⚠⚠ FOUR CALLS, AND THERE MUST NOT BE A FIFTH. WP-landing §0: this work package is a UI over
// endpoints WP-SIGNUP already built and tested; if it starts growing endpoints, something has gone
// wrong. Every call here is anonymous by design and none of them creates a tenant — a signup writes
// one `TenantApplication` row and an operator decides the rest.
//
// ⚠ NO TOKENS, NO SESSION, NO STORAGE. Architecture §12c: the "login" is a LINK to the two apps that
// already authenticate. A third auth implementation "would be the C2 problem in a place where
// getting it wrong is a breach rather than an hour."

/** What the front door says about itself. ⚠ 404 means the whole thing is switched off. */
export type DoorState =
  | { state: "closed" }                                   // signup.public flag is off → 404
  | { state: "no-agreement" }                             // open, but no DPA published → 409
  | { state: "open"; dpa: DpaView };                      // ready → 200

export interface DpaView {
  version: string;
  title: string;
  body: string;
  publishedAtUtc: string;
}

export interface ApplyResult {
  applicationId: string;
  detail: string;
}

/**
 * ⚠⚠ THE ERROR PATH IS THE POINT, so it is not a thin wrapper. This page is the first thing a
 * stranger sees; a raw "TypeError: Failed to fetch" on it is worse than no page. Every failure
 * becomes a sentence a shopkeeper can act on.
 */
async function call<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response;
  try {
    res = await fetch(path, {
      ...init,
      headers: { "Content-Type": "application/json", ...(init?.headers ?? {}) },
    });
  } catch {
    throw new Error("We couldn't reach Plutus just then. Check your connection and try again.");
  }

  if (res.ok) return (await res.json()) as T;

  // ⚠ RFC 9110 problem+json is what the backend returns for every handled refusal, and its `detail`
  // is written for a person — the disposable-address message, the name-taken message, the
  // no-agreement message. Prefer it over anything invented here.
  let detail = "";
  try {
    const body = (await res.json()) as { detail?: string; title?: string };
    detail = body?.detail ?? body?.title ?? "";
  } catch {
    /* not JSON — fall through to the status-based wording */
  }

  if (detail) throw new Error(detail);
  if (res.status === 429) throw new Error("That's a lot of tries in a short time. Wait a minute and try again.");
  throw new Error(`Something went wrong (${res.status}). Please try again.`);
}

/**
 * Is signup open, and is there an agreement to accept?
 *
 * ⚠⚠ THIS ONE CALL DRIVES WHETHER THE FORM APPEARS AT ALL. WP-landing §4: the call to action is
 * hidden when `signup.public` is off, so the page never offers a door that answers 404.
 *
 * ⚠ It does NOT throw. A landing page whose marketing copy fails to render because a status probe
 * errored is a worse outcome than one that quietly hides its signup form, so anything unexpected is
 * treated as closed.
 */
export async function doorState(): Promise<DoorState> {
  try {
    const res = await fetch("/api/v1/signup/dpa");
    if (res.status === 404) return { state: "closed" };
    if (res.status === 409) return { state: "no-agreement" };
    if (!res.ok) return { state: "closed" };
    return { state: "open", dpa: (await res.json()) as DpaView };
  } catch {
    return { state: "closed" };
  }
}

export const apply = (body: {
  businessName: string; contactName: string; contactEmail: string; phone: string; region: string;
}) => call<ApplyResult>("/api/v1/signup", { method: "POST", body: JSON.stringify(body) });

export const verifyEmail = (token: string) =>
  call<{ applicationId: string; businessName: string; verified: boolean }>(
    `/api/v1/signup/verify?token=${encodeURIComponent(token)}`, { method: "POST" });

export const acceptDpa = (applicationId: string, version: string) =>
  call<{ accepted: boolean; version: string }>(
    "/api/v1/signup/dpa/accept", { method: "POST", body: JSON.stringify({ applicationId, version }) });

/**
 * Where the two existing apps live.
 *
 * ⚠⚠ CONFIGURED, NOT DERIVED, AND THAT IS DELIBERATE. It is tempting to compute the sibling hosts
 * from this one — the portal does something like it — but **the landing domain is not chosen yet**
 * (WP-landing §5), so there is no rule to derive from. `www.plutus.huggett.dscloud.me` would give
 * `plutus.plutus.huggett.dscloud.me`, which is a broken link on the one page a stranger sees first.
 *
 * ⚠ So: set `VITE_TILL_URL` and `VITE_PORTAL_URL` at build time. Unset returns null and the caller
 * **hides the button** — a missing button is recoverable, a dead link on a marketing page is not.
 *
 * ⚠ When the domain is decided, set the two vars in the build. Do NOT add a derivation rule here to
 * save typing them: a guess that is right on one host and silently wrong on the next is exactly the
 * failure this comment exists to prevent.
 */
export function appUrls(): { till: string | null; portal: string | null } {
  const clean = (v?: string) => {
    const s = v?.trim();
    return s && /^https?:\/\//i.test(s) ? s.replace(/\/+$/, "") : null;
  };
  return {
    till: clean(import.meta.env?.VITE_TILL_URL),
    portal: clean(import.meta.env?.VITE_PORTAL_URL),
  };
}
