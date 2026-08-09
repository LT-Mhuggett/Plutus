// Thin typed layer over fetch — same-origin /api/* is reverse-proxied to the
// DBService by Caddy. No client library needed (see plan §3.5.1).

import { getSession, setSession, type Session } from "./session.ts";
import { accessToken, signOut } from "./auth.ts";
import {
  cachedItemById,
  cachedItemSearch,
  cachedMeta,
  cacheItems,
  cacheMeta,
  nextDeviceSeq,
  parkSale,
  queueSale,
  queuedSales,
  removeQueued,
} from "./offline.ts";
import { businessDay, getDeviceCredential, itemGuid, postSale, uuidv7, type IngestLine, type IngestSaleRequest } from "./pipeline.ts";
import { getPrefs } from "./prefs.ts";
import { lineDiscountPence, type BasketLine } from "./till/basket.ts";
import { toPence } from "./money.ts";

// Phase 1: single-tenant test environment — the seeded Kapow business/store/till.
// Replaced by a full bootstrap flow in later phases (ids from the seed ETL).
export const BUSINESS_ID = "d5a31aac-159e-9a30-706b-02f9eb935600";
export const STORE_ID = 1;
export const TILL_ID = "f6bf8420-3d06-6b0b-4fd7-32d265b89bb8";
export const BUSINESS_NAME = "Kapow Comics ltd";

/**
 * The store this till belongs to. An enrolled till learns its real storeId from
 * GET /tills/{id}/name (fetchTillName stores it); until then — and for un-enrolled
 * password-mode sessions — the Phase-1 seeded store stands in. This is what makes
 * receipts per-store: two tills in different stores print different addresses.
 */
export function effectiveStoreId(): number {
  const s = Number(localStorage.getItem("plutus.storeId"));
  return Number.isFinite(s) && s > 0 ? s : STORE_ID;
}

/** Exported for hardware.ts (FE3.0), which reports agent telemetry outside this module. */
export function headers(): Record<string, string> {
  const t = accessToken();
  return { BusinessId: BUSINESS_ID, ...(t ? { Authorization: `Bearer ${t}` } : {}) };
}

/** A 401 means the token expired or was revoked — drop the session and restart at login
 *  (or bounce to the IdP in OIDC mode). */
function handle401(res: Response): void {
  if (res.status === 401) signOut();
}

/** WP11.1: this till's current name. Uses the sales.ingest-gated endpoint so ANY signed-in
 *  operator (not just till admins) can read it. Null if unavailable.
 *  Also captures the till's storeId (additive server field) — the key that makes receipts
 *  per-store. When it changes, the receipt template is refetched for the right store. */
export async function fetchTillName(tillId: string): Promise<string | null> {
  const res = await fetch(`/api/v1/tills/${tillId}/name`, { headers: headers() });
  if (!res.ok) return null;
  const data = (await res.json()) as { id: string; name: string; storeId?: number | null };
  if (typeof data.storeId === "number" && data.storeId > 0 && data.storeId !== effectiveStoreId()) {
    localStorage.setItem("plutus.storeId", String(data.storeId));
    void loadReceiptTemplate(); // the boot-time load raced this; refetch for the right store
  }
  return data.name ?? null;
}

/** WP11.1: rename this till (tenant-unique; 409 surfaces as an Error). */
export async function renameTill(tillId: string, name: string): Promise<void> {
  const res = await fetch(`/api/v1/tills/${tillId}/name`, {
    method: "PUT",
    headers: { ...headers(), "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });
  handle401(res);
  if (!res.ok) {
    let detail = `${res.status}`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep status */ }
    throw new Error(detail);
  }
}

// WP6.2 un-enrol with portal approval: request removal (marks the device PendingRemoval; it keeps
// trading), and poll this device's status so the till forgets its credential once approved (Revoked).
export async function requestUnenrol(deviceId: string): Promise<{ status: string }> {
  const res = await send("POST", "/api/v1/tills/unenrol-request", { deviceId });
  return res.json();
}
export const fetchDeviceStatus = (deviceId: string) =>
  get<{ status: string }>(`/api/v1/tills/devices/${encodeURIComponent(deviceId)}/status`);

export async function login(email: string, password: string): Promise<Session> {
  const res = await fetch(`/api/Auth/Login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password }),
  });
  if (res.status === 401) throw new Error("Unknown email or wrong password.");
  if (!res.ok) throw new Error(`Login failed (${res.status}).`);
  const data = await res.json();
  const session: Session = { token: data.token, employeeId: data.employeeId, name: data.name, expiresAt: data.expiresAt };
  setSession(session);
  return session;
}

export interface Item {
  idOne: string; // EAN/UPC or legacy identifier
  name: string;
  brand: string;
  desc: string;
  cost: number;
  exPrice: number;
  price: number;
  taxId: number;
  catId: string;
  /** FE5.5 — stock isn't tracked for this item (bags, back-issues); shows ∞ instead of a count. */
  stockUntracked?: boolean;
  /** FE5.4 — binned items never reach the till (the API filters them); present for completeness. */
  binnedAtUtc?: string | null;
}

// FE5.2: on-hand quantity for a page of items — one call, not one per row.
export interface StockLevelLite { itemIdOne: string; untracked: boolean; quantity: number | null }
export async function fetchStockLevelsFor(itemIdOnes: string[]): Promise<StockLevelLite[]> {
  const res = await send("POST", "/api/v1/stock/levels/bulk", itemIdOnes);
  return res.json();
}

export interface PayMethod {
  id: number;
  name: string;
  charge: number;
  minimumCharge: number;
  isChangeable: boolean;
  isCashBackable: boolean;
}

export interface Discount {
  id: number;
  name: string;
  /** 0 = fixed £ off per unit; anything else = fraction off (amount 0.10 → 10%) */
  type: number;
  amount: number;
  allApplicable: boolean;
  canUseWithOtherDiscounts: boolean;
  autoApply: boolean;
}

export interface SaleTransaction {
  amount: number;
  itemCostExPrice: number;
  itemCostPrice: number;
  itemIdOne: string;
}

export interface ParkedTransaction {
  id: string;
  name: string;
  data: string;
}

async function get<T>(url: string): Promise<T> {
  return (await getPaged<T>(url)).rows;
}

/** FE4.2: the legacy `Index` endpoints have always returned the row count in `X-Pagination`, and
 *  every frontend threw it away — so pagers guessed ("Next" enabled whenever a full page came
 *  back). Surfacing it gives DataTable a true "X–Y of N" in server mode. total is null when the
 *  endpoint doesn't send the header. */
export interface Paged<T> { rows: T; total: number | null }

export async function getPaged<T>(url: string): Promise<Paged<T>> {
  const res = await fetch(url, { headers: headers() });
  handle401(res);
  if (!res.ok) throw new Error(`API ${res.status} ${res.statusText}`);
  const rows = (await res.json()) as T;
  let total: number | null = null;
  const raw = res.headers.get("X-Pagination");
  if (raw) {
    try {
      const meta = JSON.parse(raw) as { TotalCount?: number; totalCount?: number };
      const t = meta.TotalCount ?? meta.totalCount;
      if (typeof t === "number") total = t;
    } catch { /* header malformed — fall back to null */ }
  }
  return { rows, total };
}

async function send(method: string, url: string, body?: unknown, extraHeaders?: Record<string, string>): Promise<Response> {
  const res = await fetch(url, {
    method,
    headers: { ...headers(), "Content-Type": "application/json", ...extraHeaders },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  handle401(res);
  if (!res.ok) throw new Error(`API ${res.status} ${await res.text().catch(() => res.statusText)}`);
  return res;
}

const itemsUrl = (pageNumber: number, pageSize: number, search: string, catId: string) =>
  `/api/Item/Index?PageNumber=${pageNumber}&PageSize=${pageSize}` +
    (search ? `&Search=${encodeURIComponent(search)}` : "") +
    // device pref: match each word ("batman one" → "Batman Year One"); server default is whole-phrase
    (search && getPrefs().matchAllWords ? "&MatchAllWords=true" : "") +
    // FE5.0: server-side category filter (was client-side over one page — showed nothing)
    (catId ? `&CatId=${encodeURIComponent(catId)}` : "");

export const fetchItems = (pageNumber: number, pageSize: number, search = "", catId = "") =>
  get<Item[]>(itemsUrl(pageNumber, pageSize, search, catId));

/** FE4.2: rows + true total, for the inventory list's server-mode DataTable. */
export const fetchItemsPaged = (pageNumber: number, pageSize: number, search = "", catId = "") =>
  getPaged<Item[]>(itemsUrl(pageNumber, pageSize, search, catId));

/** Till scan-bar search — ALL matches (server-filtered, no paging: the generic Index caps
 *  PageSize at 50, so "all" needs IgnorePagination); IndexedDB cache when offline. */
export async function searchItemsOfflineAware(term: string): Promise<Item[]> {
  try {
    return await get<Item[]>(
      `/api/Item/Index?IgnorePagination=true&Search=${encodeURIComponent(term)}` +
        (getPrefs().matchAllWords ? "&MatchAllWords=true" : ""),
    );
  } catch {
    return cachedItemSearch(term, Number.POSITIVE_INFINITY, getPrefs().matchAllWords);
  }
}

/** Exact barcode/id lookup; null when unknown. Falls back to the offline cache. */
export async function findItemById(id: string): Promise<Item | null> {
  try {
    const res = await fetch(`/api/Item/${encodeURIComponent(id)}`, { headers: headers() });
    handle401(res);
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`API ${res.status} ${res.statusText}`);
    return res.json();
  } catch {
    return (await cachedItemById(id)) ?? null;
  }
}

/**
 * Duplicate-barcode guard for the Add-item dialog — NatApp parity (`AddEditInventoryViewModel`
 * calls FindById before Create and refuses with "Item already exists!").
 *
 * Deliberately NOT findItemById: that one hides binned items (the FE5.4 404 override) so a
 * scanned binned barcode behaves like an unknown one. A binned item still OWNS its barcode
 * row, so the composite PK (IdOne, IdTwo) rejects the insert — the generic POST has no
 * duplicate check and surfaces it as a raw 500. `includeBinned=true` lets the till say which
 * item is in the way and that it's in the bin.
 *
 * Returns null when the barcode is free. A network failure falls back to the offline
 * catalogue cache, and a cache miss returns null rather than blocking: the POST stays the
 * final authority, so a flaky connection can't veto a legitimate create.
 */
export async function findItemByBarcode(id: string): Promise<Item | null> {
  try {
    const res = await fetch(`/api/Item/${encodeURIComponent(id)}?includeBinned=true`, { headers: headers() });
    handle401(res);
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`API ${res.status} ${res.statusText}`);
    return res.json();
  } catch {
    return (await cachedItemById(id)) ?? null;
  }
}

export async function fetchPayMethods(): Promise<PayMethod[]> {
  try {
    const m = await get<PayMethod[]>(`/api/PaymentMethod/Index?PageNumber=1&PageSize=50`);
    void cacheMeta("payMethods", m);
    return m;
  } catch (e) {
    const cached = await cachedMeta<PayMethod[]>("payMethods");
    if (cached) return cached;
    throw e;
  }
}

export async function fetchDiscounts(): Promise<Discount[]> {
  try {
    const d = await get<Discount[]>(`/api/Discount/Index?PageNumber=1&PageSize=50`);
    void cacheMeta("discounts", d);
    return d;
  } catch (e) {
    const cached = await cachedMeta<Discount[]>("discounts");
    if (cached) return cached;
    throw e;
  }
}

// ── effective pricing (WP5.4 retrofit) ───────────────────────────────────────
// Resolve the sell price from the pricing engine (store override → central price
// list → legacy baseline) instead of the legacy catalogue price, so portal price
// changes reach the till. Offline / not-priced → the cached legacy price.

export async function effectivePriceFor(item: Item): Promise<{ pricePence: number; exPricePence: number }> {
  try {
    const res = await get<{ itemIdOne: string; pricePence: number; exPricePence: number }[]>(
      `/api/v1/prices/effective?items=${encodeURIComponent(item.idOne)}&storeId=${STORE_ID}`,
    );
    const p = res.find((x) => x.itemIdOne === item.idOne);
    if (p) return { pricePence: p.pricePence, exPricePence: p.exPricePence };
  } catch {
    /* offline or not priced — fall back to the legacy catalogue price */
  }
  return { pricePence: toPence(item.price), exPricePence: toPence(item.exPrice) };
}

// ── customers, store credit, membership (Phase 8 retrofit) ────────────────────

export interface CustomerSummary {
  id: string;
  name: string;
  email: string | null;
  phone: string | null;
  /** FE2: membership number — printed on the customer's card as a "C…" barcode. */
  memberNo: string | null;
}
export interface CustomerDetail extends CustomerSummary {
  /** FE2: the card's barcode payload ("C" + memberNo). */
  memberBarcode: string | null;
  creditAccountId: string | null;
  creditBalancePence: number;
  membership: { tierId: string | null; tier: string; autoDiscountRate: number; renewalDay: string; expired: boolean } | null;
}

export const searchCustomers = (term: string) =>
  get<CustomerSummary[]>(`/api/v1/customers?take=10${term ? `&search=${encodeURIComponent(term)}` : ""}`);

export const getCustomer = (id: string) => get<CustomerDetail>(`/api/v1/customers/${id}`);

// WP15.1 in-app announcements (till shows Maintenance/Incident only).
export interface ActiveAnnouncement { id: string; severity: string; title: string; body: string; startsAtUtc: string; endsAtUtc: string }
export const fetchActiveAnnouncements = () => get<ActiveAnnouncement[]>("/api/v1/announcements/active");

declare const __APP_VERSION__: string;

/**
 * Tell the platform this till is alive, and which BUILD it is running.
 *
 * ⚠ THE WEB TILL HAS NEVER DONE THIS. `/api/v1/heartbeat` has existed since WP5 and only the MAUI
 * till called it, so the portal's fleet list showed "version unknown" against every browser till
 * for ever — and there was no way to answer "is that till on the new build?" short of walking to
 * it. The agent chip beside it worked only because the AGENT reports separately.
 *
 * ⚠ Never throws and never blocks selling: presence is a convenience for the portal, and a till
 * whose heartbeat fails must carry on taking money. Same rule the MAUI cadence follows.
 *
 * ⚠ Silent when the till has no device credential — an un-enrolled browser has no identity to
 * report, and posting one would be inventing a till.
 */
export async function sendHeartbeat(): Promise<void> {
  const cred = getDeviceCredential();
  if (!cred?.deviceId) return;

  try {
    await send("POST", "/api/v1/heartbeat", {
      deviceId: cred.deviceId,
      appVersion: __APP_VERSION__,
      // ⚠ Zero, honestly, rather than omitted: the web till drains its outbox through its own
      // pipeline and does not expose a depth here. Reporting a real number is follow-up work —
      // reporting a made-up one would put "0 queued" beside a till that is holding sales.
      outboxDepth: 0,
      oldestUnsyncedAgeSeconds: null,
      deviceClockUtc: new Date().toISOString(),
    });
  } catch {
    // presence is not worth a single interrupted sale
  }
}

// 17.2 the tenant's card-payment setup: provider label + whether an integration is wired.
// "standalone" (the default) = external chip & pin, cashier confirms approval before completing.
export interface ActiveGateway { provider: string; label: string; integrated: boolean }
export const fetchActiveGateway = () => get<ActiveGateway>("/api/v1/payments/gateway/active");

// OP4 / WP6.3: support tickets from the till — raise + read history + reply (gated support.tickets).
export interface SupportTicket { id: string; subject: string; status: number; severity: number; raisedByName: string; createdAtUtc: string; updatedAtUtc: string }
export interface SupportMessage { fromOperator: boolean; authorName: string; body: string; atUtc: string }
export const SUPPORT_STATUS = ["Open", "Waiting on client", "Closed"];
export const SUPPORT_SEVERITY = ["Question", "Problem", "Urgent"];
export async function raiseTicket(subject: string, body: string, severity: number): Promise<void> {
  await send("POST", "/api/v1/support/tickets", { subject, body, severity });
}
export const fetchMyTickets = () => get<SupportTicket[]>("/api/v1/support/tickets");
export const fetchMyThread = (id: string) => get<SupportMessage[]>(`/api/v1/support/tickets/${id}/messages`);
export async function clientReply(id: string, body: string): Promise<void> {
  await send("POST", `/api/v1/support/tickets/${encodeURIComponent(id)}/messages`, { body });
}

/** Create a customer from the till (supervisors/managers — gated on customers.manage). */
export async function createCustomer(body: { name: string; email?: string; phone?: string }): Promise<{ id: string }> {
  const res = await send("POST", `/api/v1/customers`, body);
  return res.json();
}

/** Edit a customer's contact details (customers.manage). */
export async function updateCustomer(id: string, body: { name: string; email?: string; phone?: string }): Promise<void> {
  await send("PUT", `/api/v1/customers/${encodeURIComponent(id)}`, body);
}

/** FE1: assign a customer one of the tenant's loyalty tiers (customers.manage). The tier owns the
 *  discount and renewal length — the till no longer types a name/rate. */
export async function setMembership(id: string, tierId: string): Promise<void> {
  await send("POST", `/api/v1/customers/${encodeURIComponent(id)}/membership`, { tierId });
}

/** FE1: the tenant's loyalty tier catalogue (active only). Readable by any signed-in operator so
 *  the till's assign-tier picker works; tiers are DEFINED in the portal (Loyalty → Manage tiers). */
export interface LoyaltyTier {
  id: string; name: string; autoDiscountRate: number; durationMonths: number;
  active: boolean; sortOrder: number; memberCount: number;
}
export const fetchLoyaltyTiers = () => get<LoyaltyTier[]>(`/api/v1/loyalty/tiers`);

/** Redeem store credit against a sale. Idempotent by entryId; throws on overdraw (400). */
export async function redeemCredit(customerId: string, amountPence: number, saleId: string, entryId: string): Promise<void> {
  await send("POST", `/api/v1/customers/${encodeURIComponent(customerId)}/credit/redeem`, {
    amountPence,
    saleId,
    entryId,
    reason: "till sale",
  });
}

// ── FE7 gift cards ──────────────────────────────────────────────────────────
// A card is worthless until a till SELLS it (activate), then spendable as a TENDER (redeem).
// ⚠ Both need connectivity: the server is the balance authority and there is no offline queue for
// them — a card redeemed twice offline would be money given away. Ordinary sales stay offline-capable.

export interface GiftCardLookup {
  code: string;
  /** grouped for reading aloud: "K7QP-2M9W-XT4R-8" */
  pretty: string;
  balancePence: number;
  /** unsold | active | spent | expired | void */
  status: string;
  expiresAtUtc: string | null;
  customerId: string | null;
  /** the catalogue row an activation is rung through (stock-untracked) */
  itemIdOne: string;
  /** The tenant's declared HMRC voucher treatment — decides WHEN the card's VAT falls due.
   *  "single" (every item one rate): VAT charged when the card is SOLD, and a redemption reduces
   *  the sale's VAT-able total instead of acting as a plain tender. "multi" (mixed rates): no VAT
   *  at the card sale; VAT comes off the goods when the card is SPENT. */
  vatTreatment: "multi" | "single";
}

/** "What is this thing I just scanned?" — 404s on an unknown or mis-keyed code. */
export const lookupGiftCard = (code: string) =>
  get<GiftCardLookup>(`/api/v1/giftcards/${encodeURIComponent(code)}/lookup`);

/**
 * Gift-card write. Unlike `send`, this surfaces the server's own `detail` message: a refusal here is
 * something the CASHIER has to read and act on ("That card only has 12.50 left"), not a status code
 * to swallow.
 */
async function giftCardPost(code: string, action: string, body: unknown): Promise<{ entryId: string; code: string; balancePence: number }> {
  const res = await fetch(`/api/v1/giftcards/${encodeURIComponent(code)}/${action}`, {
    method: "POST",
    headers: { ...headers(), "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  handle401(res);
  if (!res.ok) {
    let detail = `Gift card ${action} failed (${res.status}).`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep the fallback */ }
    throw new Error(detail);
  }
  return await res.json();
}

/** Sell a card: load it with amountPence. Idempotent by entryId. 409 if it is already active. */
export const activateGiftCard = (code: string, amountPence: number, saleId: string, entryId: string, customerId?: string) =>
  giftCardPost(code, "activate", { amountPence, saleId, entryId, customerId });

/** Spend a card against a sale. Idempotent by entryId; 409 on over-redeem/expired/void/unsold. */
export const redeemGiftCard = (code: string, amountPence: number, saleId: string, entryId: string) =>
  giftCardPost(code, "redeem", { amountPence, saleId, entryId });

/** Background: pull the whole catalogue (no images) into IndexedDB for offline scanning. */
export async function syncCatalogue(onProgress?: (n: number) => void): Promise<number> {
  let page = 1;
  let total = 0;
  for (;;) {
    const batch = await fetchItems(page, 2000);
    if (batch.length === 0) break;
    await cacheItems(batch);
    total += batch.length;
    onProgress?.(total);
    if (batch.length < 2000) break;
    page++;
  }
  return total;
}

/** Lines of an existing sale. Quirk: for composite Transaction the "businessId"
 *  header slot is the second key — the SALE id. */
export async function fetchSaleLines(saleId: string): Promise<SaleTransaction[]> {
  const res = await fetch(`/api/Transaction/Index?PageNumber=1&PageSize=100`, {
    headers: { ...headers(), BusinessId: saleId },
  });
  handle401(res);
  if (!res.ok) throw new Error(`API ${res.status} ${res.statusText}`);
  return res.json();
}

// ── statistics / store info ─────────────────────────────────────────────────

export interface Sale {
  id: string;
  total: number;
  totalExTax: number;
  dateOfSale: string;
  employeeId: string;
}

export interface BusinessInfo {
  id: string;
  name: string;
  nameAbbr: string;
  vatIN: string;
  recMarkup: number | null;
}

export interface StoreInfo {
  id: number;
  adLine1: string;
  adLine2: string;
  city: string;
  postCode: string;
  country: string;
  fullAddress: string | null;
  contactNumber: string;
}

const dateOnly = (d: Date) => d.toISOString().slice(0, 10);

export interface SalesSummary {
  totalSales: number;
  totalSalesExTax: number;
  totalOrders: number;
  byDay: { date: string; total: number; totalExTax: number; orders: number }[];
  topItems: { itemId: string; name: string; quantity: number; gross: number; grossExTax: number }[];
  byPayMethod: { method: string; total: number }[];
  byTaxRate: { tax: string; gross: number; net: number; vat: number }[];
}

// Repointed to the v1 endpoint (reads SalesV2 — the full history) so the till Summary shows all
// sales, not the near-empty legacy Sales table. Same shape as the old /api/Sale/Summary.
export const fetchSalesSummary = (from: Date, to: Date) =>
  get<SalesSummary>(`/api/v1/reports/summary-rich?from=${dateOnly(from)}&to=${dateOnly(to)}`);

// ── v1 reports (parity with the portal — same endpoints, scoped to THIS store) ──
// Gated server-side on portal.reports.view / portal.financials.view via RBAC, so a cashier
// without the permission gets a 403 the Reporting page turns into a "no access" note.

export interface V1Summary {
  totals: { grossPence: number; vatPence: number; txnCount: number; avgBasketPence: number };
  buckets: { period: string; grossPence: number; vatPence: number; txnCount: number; avgBasketPence: number }[];
}
export const fetchV1Summary = (from: string, to: string, granularity = "day") =>
  get<V1Summary>(`/api/v1/reports/summary?level=store&id=${STORE_ID}&from=${from}&to=${to}&granularity=${granularity}`);

export interface V1Vat {
  totals: { grossPence: number; netPence: number; vatPence: number };
  buckets: { period: string; vatRateBp: number; grossPence: number; netPence: number; vatPence: number }[];
}
export const fetchV1Vat = (from: string, to: string, granularity = "month") =>
  get<V1Vat>(`/api/v1/reports/vat?level=store&id=${STORE_ID}&from=${from}&to=${to}&granularity=${granularity}`);

export interface V1ItemSoldRow {
  dateSold: string; itemIdOne: string; itemName: string; category: string | null; storeId: number; tillId: string;
  tillName: string; staffId: string; staffName: string;
  qty: number; unitPricePence: number; discountPence: number; lineGrossPence: number;
}
export interface V1ItemsSold {
  count: number; totals: { qty: number; grossPence: number; discountPence: number }; rows: V1ItemSoldRow[];
}
export const fetchV1ItemsSold = (from: string, to: string, operatorUserId?: string) =>
  get<V1ItemsSold>(`/api/v1/reports/items-sold?from=${from}&to=${to}&storeId=${STORE_ID}&take=2000` +
    (operatorUserId ? `&operatorUserId=${operatorUserId}` : ""));

export interface V1Staff { id: string; name: string }
export const fetchV1Staff = () => get<V1Staff[]>(`/api/v1/reports/staff?storeId=${STORE_ID}`);

export interface V1StockLevel { stockLocationId: string; location: string; itemIdOne: string; name: string | null; category: string | null; quantity: number }
export interface V1StockResp { totalCatalogueItems: number; inStock: number; matched: number; skip: number; take: number; rows: V1StockLevel[] }
export const fetchV1StockLevels = (search = "", skip = 0, take = 25, filter = "") =>
  get<V1StockResp>(`/api/v1/stock/levels?skip=${skip}&take=${take}${search ? `&search=${encodeURIComponent(search)}` : ""}${filter ? `&filter=${filter}` : ""}`);

// WP3.7/3.8 new reports (till). Tenant-wide (a till tenant is typically one store).
export interface V1CategorySalesRow { category: string; qty: number; grossPence: number; discountPence: number; sharePct: number }
export interface V1CategorySales { totals: { grossPence: number; qty: number; categories: number }; rows: V1CategorySalesRow[] }
export const fetchV1CategorySales = (from: string, to: string) =>
  get<V1CategorySales>(`/api/v1/reports/category-sales?from=${from}&to=${to}`);
export interface V1BestSellerRow { rank: number; itemIdOne: string; itemName: string; category: string | null; qty: number; grossPence: number; sharePct: number }
export const fetchV1BestSellers = (from: string, to: string, by: "qty" | "gross", take = 25) =>
  get<{ rows: V1BestSellerRow[] }>(`/api/v1/reports/best-sellers?from=${from}&to=${to}&by=${by}&take=${take}`);

export interface V1LoyaltyRow {
  id: string; name: string; email: string | null; phone: string | null;
  memberNo: string | null;
  tierId: string | null;
  tier: string | null; autoDiscountRate: number | null; renewalDay: string | null; expired: boolean; creditBalancePence: number;
}
export const fetchLoyalty = (search = "") =>
  get<{ count: number; rows: V1LoyaltyRow[] }>(`/api/v1/loyalty${search ? `?search=${encodeURIComponent(search)}` : ""}`);

export interface SaleDetail {
  id: string;
  dateOfSale: string;
  total: number;
  totalExTax: number;
  employee: string | null;
  lines: {
    itemId: string;
    name: string;
    quantity: number;
    unitPrice: number;
    unitExPrice: number;
    priceAdjusted: boolean;
    discounts: { name: string; rate: number }[];
  }[];
  payments: { method: string; amount: number; change: number }[];
  refunds: { itemId: string; name: string; quantity: number; reason: string; originalSaleId: string }[];
  notes: string[];
}

/** WP12.2: read sale detail from v1 (SalesV2 — the source of truth), not the legacy
 *  /api/Sale/Detail (bridge-fed). Maps the v1 shape → the existing SaleDetail the view dialog and
 *  return flow consume; prices are pence → pounds, ex-VAT derived from the line's VAT rate, and
 *  the barcode is the enriched itemIdOne (so returns still key on the item's natural id). */
interface V1SaleDetail {
  id: string; occurredAtUtc: string; grossPence: number; vatPence: number; operatorName: string | null; note: string | null;
  lines: { itemId: string; itemIdOne: string | null; itemName: string | null; qty: number; unitPricePence: number; vatRateBp: number; overriddenFromPence: number | null }[];
  tenders: { tenderType: string; amountPence: number; changePence: number }[];
  adjustments: { type: string; itemId: string | null; qty: number | null; amountPence: number; reason: string }[];
}
export async function fetchSaleDetail(id: string): Promise<SaleDetail> {
  const s = await get<V1SaleDetail>(`/api/v1/sales/${id}`);
  const exUnit = (pence: number, rateBp: number) => Math.round(pence * 10000 / (10000 + rateBp)) / 100;
  return {
    id: s.id,
    dateOfSale: s.occurredAtUtc,
    total: s.grossPence / 100,
    totalExTax: (s.grossPence - s.vatPence) / 100,
    employee: s.operatorName,
    lines: s.lines.map((l) => ({
      itemId: l.itemIdOne ?? l.itemId,            // barcode — what the return flow needs
      name: l.itemName ?? l.itemIdOne ?? "(item)",
      quantity: l.qty,
      unitPrice: l.unitPricePence / 100,
      unitExPrice: exUnit(l.unitPricePence, l.vatRateBp),
      priceAdjusted: l.overriddenFromPence != null,
      discounts: [],
    })),
    payments: s.tenders.map((t) => ({ method: t.tenderType, amount: t.amountPence / 100, change: t.changePence / 100 })),
    refunds: s.adjustments.filter((a) => a.type === "Refund").map((a) => ({
      itemId: a.itemId ?? "", name: "", quantity: a.qty ?? 0, reason: a.reason, originalSaleId: s.id,
    })),
    notes: s.note ? [s.note] : [],
  };
}

export interface VatIntegrity {
  offBandCount: number;
  offBandItems: { id: string; name: string; band: string; price: number; exPrice: number; expectedPrice: number }[];
}

// WP12 tidy-up (2026-07-27): the off-band VAT check reads the catalogue, not sales — moved off
// the legacy /api/Sale/VatIntegrity to its v1 equivalent (identical shape).
export const fetchVatIntegrity = () => get<VatIntegrity>(`/api/v1/reports/vat-integrity`);

/** WP12.1: the Custom report reads the v1 sales list (SalesV2 — real data), not the near-empty
 *  legacy /api/Sale/Index. Pence from the server, mapped to the existing Sale shape. */
export async function fetchSales(from: Date, to: Date): Promise<Sale[]> {
  const rows = await get<Array<{
    id: string; occurredAtUtc: string; grossPence: number; vatPence: number; operatorUserId: string | null;
  }>>(`/api/v1/sales?from=${dateOnly(from)}&to=${dateOnly(to)}&take=500`);
  return rows.map((r) => ({
    id: r.id,
    total: r.grossPence / 100,
    totalExTax: (r.grossPence - r.vatPence) / 100,
    dateOfSale: r.occurredAtUtc,
    employeeId: r.operatorUserId ?? "",
  }));
}

/** WP12.1: CSV export built client-side from the v1 rows — drops the legacy /api/Sale/SaleReport
 *  .xls dependency (the last legacy reader in the Custom report). */
export async function downloadSalesReport(from: Date, to: Date): Promise<void> {
  const sales = await fetchSales(from, to);
  const rows = [["sale_id", "date", "net_ex_vat", "vat", "total_inc_vat"]];
  for (const s of sales)
    rows.push([s.id, s.dateOfSale, s.totalExTax.toFixed(2), (s.total - s.totalExTax).toFixed(2), s.total.toFixed(2)]);
  const csv = rows.map((r) => r.map((c) => (c.includes(",") ? `"${c}"` : c)).join(",")).join("\n");
  const url = URL.createObjectURL(new Blob([csv], { type: "text/csv" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = `${dateOnly(from)}-${dateOnly(to)}-sales.csv`;
  a.click();
  URL.revokeObjectURL(url);
}

/** Receipts show the live business name; cache it so the Receipt component stays sync. */
export const businessName = () => localStorage.getItem("plutus.businessName") || BUSINESS_NAME;

// WP11.2: per-store receipt template (header/footer/toggles), fetched from the server and cached
// so the Receipt component can read it synchronously. Read via the sales.ingest-gated endpoint.
// Ported from the NatApp receipt (store name, address, phone, VAT number), all editable.
export interface ReceiptTemplate {
  storeName?: string;      // overrides the business name at the top
  addressLines?: string[]; // shop address block
  phone?: string;
  vatNumber?: string;      // shown as "VAT No: …" when showVatNumber
  headerLines?: string[];  // e.g. "Thank you for shopping with us"
  footerLines?: string[];  // e.g. returns policy
  showVatNumber?: boolean;
  showOperator?: boolean;  // print the operator's name
  showBarcode?: boolean;
}
let _receiptTemplate: ReceiptTemplate | null = (() => {
  try { const r = localStorage.getItem("plutus.receiptTemplate"); return r ? JSON.parse(r) : null; } catch { return null; }
})();
export const getReceiptTemplateCached = (): ReceiptTemplate | null => _receiptTemplate;

/**
 * The cache holds the EFFECTIVE template: the store's saved template with the store's real
 * details (address, phone, VAT number, name — from /stores/{id}/info) filled into any field
 * the template leaves blank. A store with an untouched template therefore still prints its
 * own address — previously it printed none, while the portal's preview pretended otherwise.
 * Both renderers (Receipt.tsx and receiptDoc.ts) read this cache, so they stay in lockstep.
 * Toggles (showVatNumber/showOperator/showBarcode) come from the saved template only.
 */
function mergeTemplate(stored: ReceiptTemplate | null, info: StoreInfoView | null): ReceiptTemplate | null {
  if (!info) return stored;
  const address = [info.adLine1, info.adLine2, info.city, info.postCode].filter((l) => l && l.trim());
  return {
    ...(stored ?? {}),
    storeName: stored?.storeName || info.name || undefined,
    phone: stored?.phone || info.contactNumber || undefined,
    vatNumber: stored?.vatNumber || info.vatNumber || undefined,
    addressLines: stored?.addressLines?.length ? stored.addressLines : address,
  };
}

export async function loadReceiptTemplate(): Promise<void> {
  try {
    const sid = effectiveStoreId();
    const [res, info] = await Promise.all([
      fetch(`/api/v1/stores/${sid}/receipt-template`, { headers: headers() }),
      fetchStoreInfo().catch(() => null), // fallback source only — its absence never blocks
    ]);
    if (!res.ok) return;
    const data = await res.json();
    const stored = data?.receiptTemplateJson ? (JSON.parse(data.receiptTemplateJson) as ReceiptTemplate) : null;
    _receiptTemplate = mergeTemplate(stored, info);
    // Persist so an offline reload still prints with the last-known template.
    if (_receiptTemplate) localStorage.setItem("plutus.receiptTemplate", JSON.stringify(_receiptTemplate));
    else localStorage.removeItem("plutus.receiptTemplate");
  } catch { /* keep last-known */ }
}

// ── WP2c: the portal's published VAT bands ──────────────────────────────────
// THE PORTAL IS THE SOURCE OF VAT TRUTH. A till receives bands, applies them, and reports what it
// charged — it never holds a VAT rule of its own. Before this, the single-purpose gift-card
// redemption line divided by a literal 1.2, so a standard-rate change would have silently
// mis-stated the VAT embedded in every card spent, on every till, with nothing to catch it.
//
// ⚠ The whole effective-dated TIMELINE is cached, not just today's rate. That is what lets a till
// which is offline across a rate change start charging the new rate on the day it lands; caching
// only "the rate right now" is exactly the failure WP2b quarantines.

export interface VatBand {
  key: string;
  displayName: string;
  vatClass: string;         // Standard | Reduced | Zero | Exempt | OutsideScope
  rateBp: number;           // in force when the server answered
  effectiveFromUtc: string;
  rates: { rateBp: number; effectiveFromUtc: string }[];
  /** Which legacy tax rows mean this band. An item carries a taxId, so this is how the till knows
   *  an item is EXEMPT rather than merely 0% — a distinction no rate can carry. */
  legacyTaxIds?: number[];
}

let _vatBands: VatBand[] = (() => {
  try { const r = localStorage.getItem("plutus.vatBands"); return r ? (JSON.parse(r) as VatBand[]) : []; } catch { return []; }
})();

export const getVatBandsCached = (): VatBand[] => _vatBands;

/** The rate in force for a band right now, from the cached timeline. Null if the band is unknown
 *  — callers must decide what to do rather than be handed a plausible default. */
export function vatRateBpFor(key: string, at: Date = new Date()): number | null {
  const band = _vatBands.find((b) => b.key.toLowerCase() === key.toLowerCase());
  if (!band) return null;
  const applicable = (band.rates ?? [])
    .filter((p) => new Date(p.effectiveFromUtc) <= at)
    .sort((a, b) => +new Date(a.effectiveFromUtc) - +new Date(b.effectiveFromUtc));
  return applicable.length ? applicable[applicable.length - 1].rateBp : null;
}

/** The standard rate, for the one place a till still needs a specific band: a single-purpose
 *  gift card, whose VAT is pinned by the voucher treatment rather than by the catalogue. */
export const standardRateBp = (at?: Date) => vatRateBpFor("standard", at);

/**
 * Which VAT BAND an item belongs to, from its legacy tax row.
 *
 * ⚠ THIS IS NOT DERIVABLE FROM THE PRICE. Zero-rated and exempt items both price at 0% VAT and are
 * different in law — exempt supplies block recovery of input tax attributable to them, zero-rated
 * ones don't (HMRC Notice 706). The band therefore has to travel with the sale line, or a shop that
 * sells both can never work out its recoverable proportion from its own takings.
 *
 * Null when the portal hasn't said which band a tax row means AND the rate is ambiguous — exactly
 * the zero-vs-exempt case. Sending nothing is correct there: the server falls back to snapping the
 * rate, and the portal shows the tax row as needing a decision. Guessing would put a number on a
 * VAT return that nobody chose.
 */
export function vatBandForTaxId(taxId: number): string | null {
  // ⚠ EXACTLY ONE, not the first one. This used to `return b.key` on the first band that claimed
  // the tax row, which meant that when two bands claimed it the answer was decided by the order the
  // server happened to serialise them in — and the comment above was already describing the
  // behaviour below rather than the behaviour that was here.
  //
  // Two bands claiming one tax row is not a tie to be broken, it is a mapping that is wrong, and
  // the ambiguous case is precisely the dangerous one: zero and exempt are BOTH 0%, so first-match
  // silently attributes exempt takings to zero-rated (or the reverse) and the totals still add up.
  // Nothing downstream can detect it. Null routes it to the server's *unclassified* path, where a
  // human is asked — see till-design.md C2, and Plutus.Client.Core VatBandCache, which is the
  // other half of this same rule and has always required a single match.
  const matches = _vatBands.filter((b) => (b.legacyTaxIds ?? []).includes(taxId));
  return matches.length === 1 ? matches[0].key : null;
}

/**
 * The standard rate, or a hard stop.
 *
 * ⚠ DELIBERATELY THROWS rather than falling back to 20%. This is reached only on a single-purpose
 * gift-card line, where the number IS the VAT declared on the sale — a plausible-looking default
 * would put a wrong figure on a VAT return silently, which is the failure mode this whole work
 * package exists to remove. Blocking is recoverable in one reconnect; a wrong return is not.
 *
 * In practice it cannot fire in normal trade: the bands are cached at boot and on the 60s sync,
 * the server seeds them for any tenant that has none, and a till that has never connected has no
 * device token and so cannot sell at all.
 */
function requireStandardRateBp(): number {
  const bp = standardRateBp();
  if (bp == null)
    throw new Error(
      "This till hasn't received the VAT bands from the portal yet, so it can't work out the VAT " +
      "on a gift card. Reconnect once and try again.");
  return bp;
}

export async function loadVatBands(): Promise<void> {
  try {
    const res = await fetch(`/api/v1/vat/bands`, { headers: headers() });
    if (!res.ok) return; // keep the last-known set — an offline till must still be able to sell
    const data = (await res.json()) as { bands?: VatBand[] };
    if (!data?.bands?.length) return;
    _vatBands = data.bands;
    localStorage.setItem("plutus.vatBands", JSON.stringify(_vatBands));
  } catch { /* keep last-known */ }
}

export const fetchBusiness = () =>
  get<BusinessInfo>(`/api/Business/${BUSINESS_ID}`).then((b) => {
    localStorage.setItem("plutus.businessName", b.name);
    return b;
  });
export const fetchStore = () => get<StoreInfo>(`/api/Store/${STORE_ID}`);

// WP6.1: read-only store info from the v1 endpoint (the portal is the source of truth for edits).
// Gated on sales.ingest so the till's operator/device token can read it.
export interface StoreInfoView {
  storeId: number; name: string | null; businessName: string | null; vatNumber: string | null;
  adLine1: string; adLine2: string; city: string; postCode: string; country: string;
  contactNumber: string; openingHoursJson: string | null;
}
export const fetchStoreInfo = () => get<StoreInfoView>(`/api/v1/stores/${effectiveStoreId()}/info`);

// PUT binds the full entity — fetch, merge edits, echo back. MVC validation demands
// the collection navigations be non-null (and Store its Business nav), so they're
// stubbed with empties — the server maps scalar fields onto the tracked row.
const emptyBusinessNavs = { items: [], roles: [], taxes: [], categories: [], discounts: [], employees: [], stores: [] };

export async function updateBusiness(edits: Partial<BusinessInfo>): Promise<void> {
  const current = await get<Record<string, unknown>>(`/api/Business/${BUSINESS_ID}`);
  await send("PUT", `/api/Business/${BUSINESS_ID}`, { ...current, ...emptyBusinessNavs, ...edits });
  if (edits.name) localStorage.setItem("plutus.businessName", edits.name);
}

export async function updateStore(edits: Partial<StoreInfo>): Promise<void> {
  const [current, business] = await Promise.all([
    get<Record<string, unknown>>(`/api/Store/${STORE_ID}`),
    get<Record<string, unknown>>(`/api/Business/${BUSINESS_ID}`),
  ]);
  await send("PUT", `/api/Store/${STORE_ID}`, {
    ...current,
    sales: [],
    employees: [],
    stocks: [],
    tills: [],
    business: { ...business, ...emptyBusinessNavs },
    ...edits,
  });
}

// ── employees ───────────────────────────────────────────────────────────────

export interface Employee {
  id: string;
  fName: string;
  lName: string;
  email: string;
  mobile: string;
  active: boolean;
}

export const fetchEmployees = () =>
  get<Employee[]>(`/api/Employee/Index?PageNumber=1&PageSize=100`);

export async function createEmployee(e: { fName: string; lName: string; email: string; mobile: string }): Promise<string> {
  const id = crypto.randomUUID();
  await send("POST", `/api/Employee`, {
    id,
    fName: e.fName,
    lName: e.lName,
    email: e.email,
    mobile: e.mobile || "-",
    nin: "-",
    wage: 0,
    contractedHours: 0,
    storeId: STORE_ID,
    businessId: BUSINESS_ID,
    active: true,
    adLine1: "-",
    adLine2: "",
    city: "-",
    postCode: "-",
    country: "-",
  });
  return id;
}

export const setEmployeePassword = (employeeId: string, email: string, password: string) =>
  send("POST", `/api/Auth/SetPassword`, { employeeId, email, password });

// ── item management ─────────────────────────────────────────────────────────

export interface Tax {
  idOne: number;
  name: string;
  rate: number; // stored as multiplier, e.g. 1.2 for 20%
}

export interface Category {
  idOne: string;
  name: string;
}

export const fetchTaxes = () => get<Tax[]>(`/api/Tax/Index?PageNumber=1&PageSize=50`);
export const fetchCategories = () => get<Category[]>(`/api/Category/Index?PageNumber=1&PageSize=100`);

export interface ItemInput {
  id: string;
  name: string;
  brand: string;
  desc: string;
  cost: number;
  price: number;
  exPrice: number;
  taxId: number;
  catId: string;
  /** FE5.5 — carried through an edit so the flag survives (see itemBody). */
  stockUntracked?: boolean;
  binnedAtUtc?: string | null;
}

const itemBody = (i: ItemInput) => ({
  id: i.id,
  // PUT binds the full entity — composite keys must be present
  idOne: i.id,
  idTwo: BUSINESS_ID,
  name: i.name,
  brand: i.brand || "-",
  desc: i.desc ?? "",
  cost: i.cost,
  exPrice: i.exPrice,
  price: i.price,
  image: null,
  amount: 0,
  taxId: i.taxId,
  catId: i.catId,
  businessId: BUSINESS_ID,
  // FE5.4/5.5: the PUT binds the WHOLE entity, so these must be echoed back — otherwise an
  // ordinary edit here would clear the untracked flag or silently un-bin a binned item.
  stockUntracked: i.stockUntracked ?? false,
  binnedAtUtc: i.binnedAtUtc ?? null,
});

export const createItem = (i: ItemInput) => send("POST", `/api/Item`, itemBody(i));
export const updateItem = (i: ItemInput) => send("PUT", `/api/Item/${encodeURIComponent(i.id)}`, itemBody(i));

export const createStock = (itemId: string, quantity: number) =>
  send("POST", `/api/Stock`, {
    id: itemId,
    itemIdOne: itemId,
    quantity,
    bussinessId: BUSINESS_ID, // sic — matches the StockBody property name
    storeId: STORE_ID,
  });

// ── parked (saved) transactions ─────────────────────────────────────────────

export const fetchParked = () =>
  get<ParkedTransaction[]>(`/api/SavedTransaction/Index?PageNumber=1&PageSize=50`);

export const parkTransaction = (name: string, data: string) =>
  send("POST", `/api/SavedTransaction`, { id: crypto.randomUUID(), name, data });

export const deleteParked = (id: string) => send("DELETE", `/api/SavedTransaction/${id}`);

// ── checkout (WP2.1: the v1 pipeline) ───────────────────────────────────────
//
// Decision 2026-07-24: checkout ALWAYS enqueues the ready-to-send v1 IngestSaleRequest
// into the IndexedDB outbox first (durable before any network attempt), then drains
// immediately. Online, the drain sends it in the same call; offline it stays queued and
// the reconnect drain delivers it. saleId idempotency makes redelivery safe. The legacy
// POST /api/Sale write and the client-side stock patches are GONE — the server's legacy
// bridge consumer projects each recorded sale into the legacy tables (incl. stock) until
// the Phase-3 reporting projections replace them.

export interface CheckoutPayment {
  payId: number;
  name: string;
  amountPence: number;
  changePence: number;
}

export interface CompletedSale {
  saleId: string;
  /** true when the sale is still in the outbox (offline) and will sync on reconnect */
  queued: boolean;
}

const tenderTypeFor = (methodName: string): number => {
  const n = methodName.toLowerCase();
  if (n.includes("cash")) return 0; // TenderType.Cash
  if (n.includes("online")) return 2;
  // FE7: gift card BEFORE credit — "Gift card" must not fall into the store-credit bucket, and the
  // payment-split report groups by this value.
  if (n.includes("gift")) return 4; // TenderType.GiftCard
  if (n.includes("credit")) return 3;
  return 1; // Card
};

export async function checkout(
  lines: BasketLine[],
  payments: CheckoutPayment[],
  totals: { totalPence: number; totalExTaxPence: number },
  opts?: {
    customerId?: string;
    creditRedeemPence?: number;
    giftCardRedeem?: { code: string; amountPence: number; treatment: "multi" | "single" };
  },
): Promise<CompletedSale> {
  const session = getSession();
  if (!session) throw new Error("Not signed in.");
  const cred = getDeviceCredential();
  if (!cred) throw new Error("This till is not enrolled as a device — see Settings → Till device.");

  const saleId = uuidv7();

  // Store credit (Phase 8): redeem FIRST so an overdraw/again aborts before the sale is
  // recorded. Idempotent by entryId, so a queued-then-drained sale stays consistent — the
  // credit tender below carries the same money. Online-only (the redeem needs a live balance).
  if (opts?.customerId && opts.creditRedeemPence && opts.creditRedeemPence > 0) {
    await redeemCredit(opts.customerId, opts.creditRedeemPence, saleId, uuidv7());
  }

  // FE7 gift cards, same order and for the same reason: the server owns the balance, so anything it
  // will refuse must be refused BEFORE the sale is recorded.
  //  • REDEEM first — an expired/over-redeemed card must abort the sale, not leave it short-tendered.
  //  • ACTIVATE the cards being sold — a card that cannot be loaded (already active) must abort
  //    before the customer is charged for it.
  // Both are idempotent by entryId, so a queued-then-drained sale stays consistent.
  const gift = opts?.giftCardRedeem;
  if (gift && gift.amountPence > 0) {
    await redeemGiftCard(gift.code, gift.amountPence, saleId, uuidv7());
  }
  for (const line of lines.filter((l) => l.giftCardCode)) {
    await activateGiftCard(line.giftCardCode!, line.pricePence, saleId, uuidv7(), opts?.customerId);
  }
  const ingestLines: IngestLine[] = await Promise.all(
    lines.map(async (l) => {
      // Same arithmetic as basketTotals so the header/line invariants reconcile exactly.
      const disc = lineDiscountPence(l);
      const qty = l.isReturn ? -l.quantity : l.quantity;
      const lineGross = l.pricePence * qty - (l.isReturn ? 0 : disc);
      const ratio = l.pricePence > 0 ? l.exPricePence / l.pricePence : 1;
      const lineEx = (l.exPricePence * l.quantity - Math.round(disc * ratio)) * (l.isReturn ? -1 : 1);
      return {
        itemId: await itemGuid(BUSINESS_ID, l.item.idOne),
        qty,
        unitPricePence: l.pricePence,
        discountPence: l.isReturn ? 0 : disc,
        lineGrossPence: lineGross,
        // FE7: a gift-card ACTIVATION line's band is pinned by the tenant's voucher treatment —
        // "multi" priced ex==price (0 VAT, VAT falls due at redemption), "single" priced with VAT in
        // (round-tripping pence through the generic ratio would wobble the band to 1998–2002bp and
        // scatter the VAT report; the treatment says it IS the standard rate, so state it).
        // WP2c: "the standard rate" is now whatever the PORTAL publishes, not a literal 2000.
        vatRateBp: l.giftCardCode
          ? (l.exPricePence === l.pricePence ? 0 : requireStandardRateBp())
          : l.exPricePence > 0 ? Math.round((l.pricePence / l.exPricePence - 1) * 10000) : 0,
        vatAmountPence: lineGross - lineEx,
        overriddenFromPence: l.adjusted ? Math.round(l.item.price * 100) : null,
        // Projection metadata for the server's legacy bridge (shape documented there).
        // The members' auto-discount uses sentinel discountId 0 and is FILTERED OUT of the
        // bridge's discounts[] (which maps to legacy Transaction_Discount by real DiscountId —
        // a synthetic id would FK-fail). Its money still flows via discountPence above.
        discountsJson: JSON.stringify({
          itemIdOne: l.item.idOne,
          exUnitPence: l.exPricePence,
          // WP2c-exempt: WHICH BAND this line was rung up under. The rate cannot tell zero-rated
          // from exempt (both 0%), so without this a business selling both can never derive its
          // partial-exemption position. Omitted when the portal hasn't resolved the tax row — the
          // server then falls back to snapping the rate, which is right for every unambiguous band.
          //
          // ⚠ A SINGLE-purpose gift-card activation is standard-rated BY THE VOUCHER TREATMENT, not
          // by its catalogue row (which sits on a zero band) — so its band is the standard one, the
          // same source as its rate above. A MULTI-purpose activation declares no VAT and takes its
          // catalogue band like any other line.
          vatBand: l.giftCardCode && l.exPricePence !== l.pricePence
            ? "standard"
            : (vatBandForTaxId(l.item.taxId) ?? undefined),
          discounts: l.discount && l.discount.discountId !== 0
            ? [{ id: l.discount.discountId, rate: l.discount.amount }]
            : undefined,
          return: l.isReturn && l.originSaleId ? { originSaleId: l.originSaleId } : undefined,
        }),
      };
    }),
  );

  // FE7 single-purpose redemption: the card's VAT was declared when it was SOLD, so spending it must
  // not declare VAT again. A plain tender would (the goods lines keep their VAT), so under "single"
  // the card is a NEGATIVE standard-rated line instead — it reduces the sale's VAT-able consideration
  // by exactly the VAT embedded in the card, and the remaining tenders cover the reduced gross.
  // ("multi" keeps the tender mechanics: goods VAT is genuinely due at redemption.)
  let grossPence = totals.totalPence;
  let vatPence = totals.totalPence - totals.totalExTaxPence;
  if (gift && gift.amountPence > 0 && gift.treatment === "single") {
    // WP2c: the rate comes from the portal's published standard band. This line used to divide by
    // a literal 1.2 — the last hard-coded VAT rule on any till. A standard-rate change would have
    // silently mis-stated the VAT embedded in every card spent, with nothing to catch it.
    const bp = requireStandardRateBp();
    const giftVat = gift.amountPence - Math.round(gift.amountPence / (1 + bp / 10000));
    ingestLines.push({
      itemId: await itemGuid(BUSINESS_ID, "GIFT-CARD"),
      qty: 1,
      unitPricePence: -gift.amountPence,
      discountPence: 0,
      lineGrossPence: -gift.amountPence,
      vatRateBp: bp,
      vatAmountPence: -giftVat,
      overriddenFromPence: null,
      // Standard-rated by the voucher treatment (the card's VAT was declared when it was sold), so
      // the band is stated rather than inferred — the same source as the rate above.
      discountsJson: JSON.stringify({
        itemIdOne: "GIFT-CARD", exUnitPence: -(gift.amountPence - giftVat), vatBand: "standard",
      }),
    });
    grossPence -= gift.amountPence;
    vatPence -= giftVat;
  }

  const request: IngestSaleRequest = {
    saleId,
    deviceId: cred.deviceId,
    deviceSeq: await nextDeviceSeq(),
    channel: 1, // SaleChannel.WebPos
    businessDay: businessDay(),
    occurredAtUtc: new Date().toISOString(),
    grossPence,
    vatPence,
    operatorUserId: session.employeeId,
    lines: ingestLines,
    tenders: payments.map((p) => ({
      tenderType: tenderTypeFor(p.name),
      amountPence: p.amountPence,
      changePence: p.changePence,
      providerRef: JSON.stringify({ payId: p.payId }), // legacy PayMethod mapping (interim)
    })),
  } as IngestSaleRequest;

  // Durable first, network second: the sale survives a crash/refresh mid-send.
  await queueSale({ saleId, request, queuedAt: new Date().toISOString() });
  notifyOutboxChanged();
  await drainOutbox();

  const stillQueued = (await queuedSales()).some((q) => q.saleId === saleId);
  return { saleId, queued: stillQueued };
}

// ── outbox drain ────────────────────────────────────────────────────────────

const outboxListeners = new Set<() => void>();
export const onOutboxChanged = (fn: () => void) => {
  outboxListeners.add(fn);
  return () => outboxListeners.delete(fn);
};
const notifyOutboxChanged = () => outboxListeners.forEach((fn) => fn());

let draining = false;

/** Replay queued sales into POST /api/v1/sales, oldest first. Safe to call repeatedly.
 *  Retryable failures stop the drain (order preserved); permanent rejections are parked
 *  locally (client-side dead-letter, visible in Settings) so they never block the queue. */
export async function drainOutbox(): Promise<{ sent: number; remaining: number }> {
  if (draining) return { sent: 0, remaining: (await queuedSales()).length };
  draining = true;
  let sent = 0;
  let changed = false;
  try {
    const queue = (await queuedSales()).sort((a, b) => a.queuedAt.localeCompare(b.queuedAt));
    for (const q of queue) {
      const outcome = await postSale(q.request);
      if (outcome.kind === "recorded" || outcome.kind === "quarantined") {
        await removeQueued(q.saleId);
        sent++;
        changed = true;
      } else if (outcome.kind === "rejected") {
        await parkSale({ ...q, reason: outcome.detail, parkedAt: new Date().toISOString() });
        await removeQueued(q.saleId);
        changed = true;
      } else {
        break; // offline / server unavailable — keep it queued, stop the drain
      }
    }
  } finally {
    draining = false;
    if (changed) notifyOutboxChanged();
  }
  return { sent, remaining: (await queuedSales()).length };
}

// ---- Phase 6: pick-from-floor notifications (a web sale sold stock on the shop floor) ----
export interface PickNotification {
  id: string;
  message: string;
  wooOrderId: number;
  storeId: number | null;
  createdAtUtc: string;
}
export async function fetchPickNotifications(): Promise<PickNotification[]> {
  const res = await fetch(`/api/v1/notifications?unackedOnly=true`, { headers: headers() });
  if (!res.ok) return [];   // quietly absent when unauthorised/offline — the till keeps trading
  return (await res.json()) as PickNotification[];
}
export async function ackPickNotification(id: string): Promise<void> {
  const res = await fetch(`/api/v1/notifications/${id}/ack`, { method: "POST", headers: headers() });
  handle401(res);
  if (!res.ok) throw new Error(`ack failed (${res.status})`);
}
