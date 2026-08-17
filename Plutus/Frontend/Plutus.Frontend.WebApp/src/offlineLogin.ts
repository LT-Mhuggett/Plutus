// Verifying an operator's password against the cached roster, in the browser.
//
// ⚠ Started in W-P3 (the supervisor step-up needs it) and extended by W-P4 (offline sign-in). The
// PBKDF2 half below is shared by both.
//
// ⚠⚠ THE PARAMETERS ARE LOAD-BEARING AND DELIBERATE — C2 twin of `SharedKernel/Crypto.cs Pbkdf2`:
//
//     101010 iterations · SHA-1 · 64-byte hash · 32-byte salt · UTF-8 password
//
// **SHA-1 is not a mistake.** `Crypto.cs` says so outright: it preserves the LEGACY NatApp till hash
// byte-for-byte, because the old `Rfc2898DeriveBytes(string, byte[])` constructor UTF8-encoded the
// password and defaulted to SHA-1. A browser twin that "modernised" this to SHA-256 would refuse
// **every valid password ever set** — and would look like a wrong-password bug, not a hash mismatch.
// Do not change it here without changing the server, the MAUI till and every stored credential.
//
// ⚠ The password never leaves the machine: it is hashed locally and compared against the hash the
// roster already carries. Nothing is sent anywhere.

import { cachedRoster, type OperatorRoster, type TillOperator } from "./roster.ts";

/** Mirrors `Pbkdf2.Iterations`. */
export const PBKDF2_ITERATIONS = 101010;
/** Mirrors `Pbkdf2.HashBytes` (64 bytes = 512 bits). */
export const PBKDF2_HASH_BITS = 512;
/** ⚠ Mirrors `Crypto.cs`'s explicit choice. See the header before touching this. */
export const PBKDF2_HASH = "SHA-1";

const base64ToBytes = (b64: string): Uint8Array =>
  Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));

/** Constant-time-ish comparison. ⚠ Length-first then a full XOR sweep — never a `===` on a decoded
 *  string, and never an early return on the first differing byte. */
function bytesEqual(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
  return diff === 0;
}

/**
 * Derive the PBKDF2 hash for a password and salt, exactly as the platform does.
 *
 * ⚠ Uses WebCrypto, which is available on every browser this till supports — and only over HTTPS or
 * localhost (`crypto.subtle` is undefined on plain http). The till is served over HTTPS.
 */
export async function derive(password: string, salt: Uint8Array): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    "PBKDF2",
    false,
    ["deriveBits"],
  );

  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: PBKDF2_HASH, salt: salt as unknown as BufferSource, iterations: PBKDF2_ITERATIONS },
    key,
    PBKDF2_HASH_BITS,
  );

  return new Uint8Array(bits);
}

/**
 * Does this password match the operator's cached credential?
 *
 * ⚠ FALSE when the operator has **no** credential rather than throwing — an employee who has never
 * had a platform password set is a real state (the portal sets one), and `OperatorLogin` gives it its
 * own message rather than calling it a wrong password.
 *
 * ⚠ NEVER THROWS: a malformed base64 or an unavailable `crypto.subtle` is a refusal, not a crash on
 * the login path.
 */
export async function verifyAgainstRoster(operator: TillOperator | null | undefined, password: string): Promise<boolean> {
  if (!operator?.credentialHashBase64 || !operator?.credentialSaltBase64) return false;
  if (!password) return false;

  try {
    const salt = base64ToBytes(operator.credentialSaltBase64);
    const expected = base64ToBytes(operator.credentialHashBase64);
    return bytesEqual(await derive(password, salt), expected);
  } catch {
    return false;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// W-P4 — signing in with the network down, and how long a cached credential is trusted.
//
// ⚠⚠ C2 TWIN of `SharedKernel/OfflineCredentials.cs`. The five horizons, three trust tiers, sell
// floor and session cap are all mirrored from it — and its header states the shape they exist to
// produce: **selling stays alive for a long time, money-out expires quickly, and the till NEVER
// hard-locks.** A till that refused to sell because its roster was old would close a shop over a
// connection problem, which is the outage the whole offline design exists to prevent.
//
// ⚠⚠ WHY THE WEB TILL NEEDED IT: it could not sign anybody in offline at all. So the shop that lost
// its broadband lost its till.
// ─────────────────────────────────────────────────────────────────────────────

/** How far a till may trust a cached credential. ⚠ Mirrors `OfflineTrust`. */
export type OfflineTrust =
  /** Inside the money-out horizon: every cached permission applies. */
  | "full"
  /** Past money-out, inside sell: the till KEEPS SELLING with the floor set — refunds, cash-out,
   *  overrides and admin are withdrawn until it reconnects. */
  | "sellOnly"
  /** Past the sell horizon: offline sign-in is refused. ⚠ Reaching this means fleet alerting failed
   *  weeks ago. */
  | "refused";

/** ⚠⚠ The five horizons, mirroring `OfflineCredentialPolicy.Default` EXACTLY. They live in one place
 *  on each side so the till, the portal warning and the DPA statement cannot disagree. */
export const POLICY = {
  moneyOutMaxAgeMs: 7 * 24 * 3600_000,   // 7 days
  sellMaxAgeMs: 30 * 24 * 3600_000,      // 30 days
  warnAfterMs: 3 * 24 * 3600_000,        // 3 days
  idleLockMs: 15 * 60_000,               // 15 minutes — LOCK, never log out
  maxSessionMs: 12 * 3600_000,           // 12 hours
} as const;

/**
 * ⚠⚠ WHAT A STALE TILL KEEPS — an ALLOW-LIST, so it is default-deny. A permission added to the
 * catalogue later is withdrawn when stale until somebody deliberately adds it here; a new permission
 * that silently survived staleness would be the exact bug this tiering exists to prevent.
 *
 * ⚠ `support.tickets` is in the floor for the same reason it is seeded to every built-in role: a lone
 * cashier on a broken till must be able to shout for help, and that is doubly true when the thing
 * that is broken is the connection.
 */
export const SELL_FLOOR: ReadonlySet<string> = new Set(["pos.sell", "support.tickets"]);

/** Would this permission still work past the money-out horizon? ⚠ Everything outside the floor is
 *  withdrawn. */
export const survivesStaleness = (permissionCode: string | null | undefined): boolean =>
  !!permissionCode && SELL_FLOOR.has(permissionCode);

export interface OfflineAssessment {
  trust: OfflineTrust;
  ageMs: number;
  shouldWarn: boolean;
  message: string;
  maySignIn: boolean;
}

/**
 * Assess a cached credential's age.
 *
 * ⚠ `syncedAtUtc` is the ROSTER's `asOfUtc` — the **server's** clock, and the envelope's, not the
 * browser's. On MAUI the equivalent is `LocalOperator.UpdatedAtUtc` because its store is relational;
 * the web till caches the whole envelope, so the envelope's stamp IS "when this operator's record came
 * down". ⚠ Not "when the till last talked to the server for any reason" — a catalogue sync does not
 * refresh a roster.
 *
 * ⚠⚠ A CLOCK THAT HAS GONE BACKWARDS IS CLAMPED TO ZERO and treated as current, exactly as .NET does:
 * refusing to let staff in over a wrong clock fails in the one direction that stops a shop trading.
 */
export function assess(syncedAtUtc: string | null | undefined, now = new Date()): OfflineAssessment {
  // ⚠ No roster at all is NOT "fresh" — it is refused, because there is nothing to verify against.
  if (!syncedAtUtc) {
    return {
      trust: "refused", ageMs: 0, shouldWarn: true, maySignIn: false,
      message:
        "This till hasn't downloaded its staff list yet, so it can't verify logins offline. " +
        "Connect it to the internet and sign in once.",
    };
  }

  const parsed = Date.parse(syncedAtUtc);
  if (Number.isNaN(parsed)) {
    return {
      trust: "refused", ageMs: 0, shouldWarn: true, maySignIn: false,
      message:
        "This till's staff list can't be read, so it can't verify logins offline. " +
        "Connect it to the internet and sign in once.",
    };
  }

  const ageMs = Math.max(0, now.getTime() - parsed);
  const days = Math.floor(ageMs / 86_400_000);

  if (ageMs > POLICY.sellMaxAgeMs) {
    return {
      trust: "refused", ageMs, shouldWarn: true, maySignIn: false,
      message:
        `This till hasn't reached Plutus for ${days} days and can't verify staff logins any more. ` +
        "Connect it to the internet, or ask a manager for a temporary code.",
    };
  }

  if (ageMs > POLICY.moneyOutMaxAgeMs) {
    return {
      trust: "sellOnly", ageMs, shouldWarn: true, maySignIn: true,
      message:
        `Offline for ${days} days. Sales work normally — refunds, cash out and manager ` +
        "functions need a connection.",
    };
  }

  if (ageMs > POLICY.warnAfterMs) {
    return {
      trust: "full", ageMs, shouldWarn: true, maySignIn: true,
      message:
        `Offline for ${days} days. Reconnect soon to refresh staff logins — refunds and ` +
        `manager functions stop working after ${POLICY.moneyOutMaxAgeMs / 86_400_000} days.`,
    };
  }

  return { trust: "full", ageMs, shouldWarn: false, maySignIn: true, message: "" };
}

/**
 * ⚠ May this permission be used right now, given the till's staleness? Combine with `can(...)` — this
 * answers *"has the horizon withdrawn it?"*, not *"does the operator hold it?"*
 */
export const allowedWhileOffline = (trust: OfflineTrust, permissionCode: string): boolean =>
  trust === "full" ? true : trust === "sellOnly" ? survivesStaleness(permissionCode) : false;

/**
 * When a session must end, whatever the operator is doing: the earliest of the 12 h cap and the
 * business-day rollover.
 *
 * ⚠⚠ THE ROLLOVER IS THE LOAD-BEARING HALF, and the .NET header says why: a session spanning two
 * business days leaks yesterday's operator into today's X/Z breakdown, and a shift change with no
 * re-auth attributes the incoming person's sales to the outgoing one — silently, in exactly the
 * records HMRC would ask about.
 *
 * ⚠ The rollover is the TILL's own wall-clock midnight, not UTC's — a business day is a shop's day
 * (till-design Part C, the business-day rule).
 */
export function sessionExpiresAt(signedInAt: Date, now = new Date()): Date {
  const cap = new Date(signedInAt.getTime() + POLICY.maxSessionMs);
  const rollover = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1, 0, 0, 0, 0);

  return rollover < cap ? rollover : cap;
}

export interface OfflineLoginResult {
  ok: boolean;
  operator?: TillOperator;
  trust?: OfflineTrust;
  /** ⚠ Shown whether or not sign-in succeeded — a stale-but-usable till must say so. */
  message: string;
}

/**
 * Sign in against the cached roster, with no network.
 *
 * ⚠⚠ ONLY EVER CALLED AFTER A NETWORK FAILURE, never after a 401. A 401 is an ANSWER — the platform
 * has refused this password or this account — and falling back to a cached hash would let a disabled
 * operator sign in offline past their own refusal. The caller enforces that; this function cannot see
 * the difference.
 *
 * ⚠ Each refusal gets its OWN sentence, mirroring `OperatorLogin`'s distinctions: *"wrong password"*
 * for all of them once sent somebody hunting a typo that did not exist.
 */
export async function signInOffline(
  emailOrId: string,
  password: string,
  now = new Date(),
): Promise<OfflineLoginResult> {
  const roster: OperatorRoster | null = await cachedRoster();

  if (!roster || (roster.operators ?? []).length === 0) {
    return { ok: false, message: "This till has no staff list yet. Connect it to Plutus and sign in once." };
  }

  const staleness = assess(roster.asOfUtc, now);

  // ⚠ The horizon is checked BEFORE the password: past 30 days the answer is the same whatever they
  // type, and telling somebody their password is wrong when the till is merely too stale sends them
  // to reset a password that was never the problem.
  if (!staleness.maySignIn) return { ok: false, message: staleness.message };

  const wanted = emailOrId.trim().toLowerCase();
  const who = (roster.operators ?? []).find(
    (o) => (o.email ?? "").trim().toLowerCase() === wanted || (o.userId ?? "").toLowerCase() === wanted,
  );

  if (!who) return { ok: false, message: "No account on this till matches that." };

  if (!who.credentialHashBase64 || !who.credentialSaltBase64) {
    return {
      ok: false,
      message: `${who.displayName} doesn't have a Plutus password yet — an administrator sets one in the portal.`,
    };
  }

  if (!(await verifyAgainstRoster(who, password))) return { ok: false, message: "Wrong password." };

  return { ok: true, operator: who, trust: staleness.trust, message: staleness.message };
}
