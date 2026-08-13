// WP2.1/WP2.2 (2026-07-24): the v1 sale pipeline client. Everything that talks to
// /api/v1/* lives here — device enrolment + credential storage (WP2.2), device-token
// exchange, and the idempotent sale ingest POST the outbox drains into (WP2.1).
// Request/response shapes come from the generated OpenAPI types (src/api/types.gen.ts,
// regenerated from openapi.json — the frozen WP1.6 contract).

import type { components } from "./api/types.gen.ts";
import { getSession } from "./session.ts";

export type IngestSaleRequest = components["schemas"]["IngestSaleRequest"];
export type IngestLine = components["schemas"]["IngestLine"];
export type IngestTender = components["schemas"]["IngestTender"];

// ── token payload decoding (CompactToken: base64url(payloadJson).base64url(sig)) ──

function decodePayload(token: string): Record<string, unknown> | null {
  try {
    const body = token.split(".")[0].replace(/-/g, "+").replace(/_/g, "/");
    return JSON.parse(atob(body.padEnd(body.length + ((4 - (body.length % 4)) % 4), "=")));
  } catch {
    return null;
  }
}

/** Scopes carried by the signed-in operator's token (e.g. "pos.sell portal.tills.enrol"). */
export function sessionScopes(): string[] {
  const s = getSession();
  if (!s) return [];
  const payload = decodePayload(s.token);
  return typeof payload?.Scope === "string" ? payload.Scope.split(" ").filter(Boolean) : [];
}

export const canEnrolTills = () => sessionScopes().includes("portal.tills.enrol");

/** Loyalty usability: EDIT a customer, or set their tier — supervisors/managers only. */
export const canManageCustomers = () => sessionScopes().includes("customers.manage");

/**
 * WP12 / binding default 20 (Matt, 2026-08-13: *"Till operator to add new loyalty members"*):
 * may this operator SIGN A NEW MEMBER UP? Pure so it is testable — `canAddCustomers` supplies the
 * session's scopes.
 *
 * ⚠ ADDING IS A LOWER BAR THAN EDITING, deliberately. Signing someone up happens at the counter with
 * a queue behind them, so it reaches the Cashier via `pos.customers.add`; changing a member's email
 * quietly redirects their account and changing a tier changes every future basket, so both stay on
 * `customers.manage`. Use `canManageCustomers()` for those — not this.
 *
 * ⚠ `customers.manage` counts too, so nobody who could already add a member loses the ability. This
 * mirrors the server's `perm:customers.manage,pos.customers.add` (comma is OR), and the server is
 * the real gate — a till getting this wrong shows or hides a button, and the endpoint still refuses.
 */
export const mayAddCustomer = (scopes: readonly string[]) =>
  scopes.includes("pos.customers.add") || scopes.includes("customers.manage");

export const canAddCustomers = () => mayAddCustomer(sessionScopes());

/** WP6.3: manage this device's settings (receipt behaviour, carrier-bag barcode, printer). */
export const canManageSettings = () => sessionScopes().includes("pos.settings.manage");

// ── device credential (WP2.2) — one per browser, stored locally like the native till ──

export interface DeviceCredential {
  deviceId: string;
  clientSecret: string;
  tillId: string;
  tenantId: string;
  enrolledAt: string;
}

const DEVICE_KEY = "plutus.device";
const TOKEN_KEY = "plutus.deviceToken";

export function getDeviceCredential(): DeviceCredential | null {
  try {
    const raw = localStorage.getItem(DEVICE_KEY);
    return raw ? (JSON.parse(raw) as DeviceCredential) : null;
  } catch {
    return null;
  }
}

/** Forget the local credential (the server-side device stays until revoked from the portal). */
export function clearDeviceCredential(): void {
  localStorage.removeItem(DEVICE_KEY);
  localStorage.removeItem(TOKEN_KEY);
}

/** Redeem an enrolment code (WP1.2 flow): this browser becomes an enrolled device. */
export async function enrolDevice(code: string): Promise<DeviceCredential> {
  const res = await fetch(`/api/v1/tills/enrol`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ enrolmentCode: code.trim() }),
  });
  if (res.status === 410) throw new Error((await problemDetail(res)) ?? "Code unknown, expired or already used.");
  if (!res.ok) throw new Error(`Enrolment failed (${res.status}).`);
  const data = await res.json();
  const cred: DeviceCredential = {
    deviceId: data.deviceId,
    clientSecret: data.clientSecret,
    tillId: data.tillId,
    tenantId: data.tenantId,
    enrolledAt: new Date().toISOString(),
  };
  localStorage.setItem(DEVICE_KEY, JSON.stringify(cred));
  localStorage.removeItem(TOKEN_KEY);
  return cred;
}

/** Admin (scope portal.tills.enrol): create a till and get a single-use enrolment code. */
export async function createTillEnrolCode(storeId: number, name: string): Promise<{ tillId: string; enrolmentCode: string; expiresAtUtc: string }> {
  const s = getSession();
  const res = await fetch(`/api/v1/tills`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...(s ? { Authorization: `Bearer ${s.token}` } : {}) },
    body: JSON.stringify({ storeId, name }),
  });
  if (res.status === 401 || res.status === 403) throw new Error("Your login does not have till-enrolment rights.");
  if (!res.ok) throw new Error(`Creating an enrolment code failed (${res.status}).`);
  return res.json();
}

async function problemDetail(res: Response): Promise<string | null> {
  try {
    const body = await res.json();
    return typeof body?.detail === "string" ? body.detail : null;
  } catch {
    return null;
  }
}

// ── device token (12h HMAC, cached; refreshed from the stored credential) ──

interface CachedToken {
  token: string;
  expiresAt: number; // epoch ms
}

/** Thrown when the server says this device is revoked/unknown — the operator must re-enrol. */
export class DeviceRevokedError extends Error {
  constructor() {
    super("This device's enrolment was revoked — re-enrol from Settings → Till device.");
  }
}

export async function getDeviceToken(): Promise<string> {
  const cred = getDeviceCredential();
  if (!cred) throw new Error("This device is not enrolled — see Settings → Till device.");

  try {
    const raw = localStorage.getItem(TOKEN_KEY);
    if (raw) {
      const cached = JSON.parse(raw) as CachedToken;
      if (cached.expiresAt - Date.now() > 60_000) return cached.token;
    }
  } catch {
    /* fall through to refresh */
  }

  const res = await fetch(`/api/v1/tokens/device`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ deviceId: cred.deviceId, clientSecret: cred.clientSecret }),
  });
  if (res.status === 401) throw new DeviceRevokedError();
  if (!res.ok) throw new Error(`Device token exchange failed (${res.status}).`);
  const data = await res.json();
  localStorage.setItem(
    TOKEN_KEY,
    JSON.stringify({ token: data.accessToken, expiresAt: Date.now() + data.expiresInSeconds * 1000 } satisfies CachedToken),
  );
  return data.accessToken;
}

// ── ids ──

/** UUIDv7 (spec: client-minted saleIds sort by time). */
export function uuidv7(): string {
  const b = new Uint8Array(16);
  crypto.getRandomValues(b);
  const t = Date.now();
  b[0] = (t / 2 ** 40) & 0xff;
  b[1] = (t / 2 ** 32) & 0xff;
  b[2] = (t / 2 ** 24) & 0xff;
  b[3] = (t / 2 ** 16) & 0xff;
  b[4] = (t / 2 ** 8) & 0xff;
  b[5] = t & 0xff;
  b[6] = (b[6] & 0x0f) | 0x70;
  b[8] = (b[8] & 0x3f) | 0x80;
  return hexUuid(b);
}

/**
 * Deterministic v1 ItemId for a legacy catalogue item — the EXACT twin of
 * Plutus.SharedKernel.DeterministicGuid.ForItem (see its doc comment):
 * SHA-256("plutus:item:{businessId-lowercase}:{itemIdOne}")[0..16] with version-8 +
 * RFC-variant bits. Every device derives the same id with no mapping table.
 */
export async function itemGuid(businessId: string, itemIdOne: string): Promise<string> {
  const name = `plutus:item:${businessId.toLowerCase()}:${itemIdOne}`;
  const hash = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(name)));
  const b = hash.slice(0, 16);
  b[6] = (b[6] & 0x0f) | 0x80;
  b[8] = (b[8] & 0x3f) | 0x80;
  return hexUuid(b);
}

function hexUuid(b: Uint8Array): string {
  const h = [...b].map((x) => x.toString(16).padStart(2, "0")).join("");
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

/** The local business day (a till's day is wall-clock, not UTC). */
export function businessDay(d = new Date()): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

// ── the ingest POST (drained from the outbox) ──

export type PostSaleOutcome =
  | { kind: "recorded" }   // 201, or 200 duplicate — the sale is durably on the server
  | { kind: "quarantined" } // 202 — the server kept it aside; ops resolve it (never re-send)
  | { kind: "rejected"; detail: string } // 4xx — permanent, parked locally for inspection
  | { kind: "retry"; detail: string };   // offline / 5xx / 429 / auth hiccup — try again later

export async function postSale(request: IngestSaleRequest): Promise<PostSaleOutcome> {
  let token: string;
  try {
    token = await getDeviceToken();
  } catch (e) {
    if (e instanceof DeviceRevokedError) return { kind: "rejected", detail: e.message };
    return { kind: "retry", detail: String(e) };
  }

  let res: Response;
  try {
    res = await fetch(`/api/v1/sales`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
        "Idempotency-Key": String(request.saleId),
      },
      body: JSON.stringify(request),
    });
  } catch (e) {
    return { kind: "retry", detail: String(e) }; // network — the classic offline case
  }

  if (res.status === 201 || res.status === 200) return { kind: "recorded" };
  if (res.status === 202) return { kind: "quarantined" };
  if (res.status === 401) {
    // token went stale between cache-check and send — force refresh, retry next drain
    localStorage.removeItem(TOKEN_KEY);
    return { kind: "retry", detail: "device token expired" };
  }
  if (res.status === 429 || res.status >= 500) return { kind: "retry", detail: `server ${res.status}` };
  return { kind: "rejected", detail: (await problemDetail(res)) ?? `server ${res.status}` };
}

// ── cash sessions (WP7.2) ────────────────────────────────────────────────────
// Cash events go through the same device-token path as sales. Kept simple: the
// drawer is a low-frequency, connectivity-assumed operation (float/paid-in/out at
// the start/middle of a shift, Z at the end), so — unlike checkout — these post
// directly rather than through the offline outbox.

export type CashEventType = "OpenFloat" | "PaidIn" | "PaidOut" | "XSnapshot" | "ZClose";

export interface CashEventResult {
  type: CashEventType;
  amountPence: number;
  countedPence?: number | null;
  expectedPence?: number | null;
  variancePence?: number | null;
}

export async function postCashEvent(input: {
  type: CashEventType;
  amountPence?: number;
  countedPence?: number;
  reason?: string;
}): Promise<CashEventResult> {
  const cred = getDeviceCredential();
  if (!cred) throw new Error("This till is not enrolled as a device — see Settings → Till device.");
  const token = await getDeviceToken();

  const res = await fetch(`/api/v1/cash-events`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({
      eventId: uuidv7(),
      deviceId: cred.deviceId,
      type: input.type,
      businessDay: businessDay(),
      occurredAtUtc: new Date().toISOString(),
      amountPence: input.amountPence ?? 0,
      countedPence: input.countedPence ?? null,
      reason: input.reason ?? null,
    }),
  });
  if (res.status === 409) throw new Error((await problemDetail(res)) ?? "The drawer is already closed for today.");
  if (!res.ok) throw new Error((await problemDetail(res)) ?? `Cash event failed (${res.status}).`);
  return res.json();
}
