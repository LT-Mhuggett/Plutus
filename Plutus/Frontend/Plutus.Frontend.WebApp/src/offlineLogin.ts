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

import type { TillOperator } from "./roster.ts";

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
