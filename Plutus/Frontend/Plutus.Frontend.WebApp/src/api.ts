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
import { lineDiscountPence, type BasketLine } from "./till/basket.ts";
import { toPence } from "./money.ts";

// Phase 1: single-tenant test environment — the seeded Kapow business/store/till.
// Replaced by a full bootstrap flow in later phases (ids from the seed ETL).
export const BUSINESS_ID = "d5a31aac-159e-9a30-706b-02f9eb935600";
export const STORE_ID = 1;
export const TILL_ID = "f6bf8420-3d06-6b0b-4fd7-32d265b89bb8";
export const BUSINESS_NAME = "Kapow Comics ltd";

function headers(): Record<string, string> {
  const t = accessToken();
  return { BusinessId: BUSINESS_ID, ...(t ? { Authorization: `Bearer ${t}` } : {}) };
}

/** A 401 means the token expired or was revoked — drop the session and restart at login
 *  (or bounce to the IdP in OIDC mode). */
function handle401(res: Response): void {
  if (res.status === 401) signOut();
}

/** WP11.1: this till's current name. Uses the sales.ingest-gated endpoint so ANY signed-in
 *  operator (not just till admins) can read it. Null if unavailable. */
export async function fetchTillName(tillId: string): Promise<string | null> {
  const res = await fetch(`/api/v1/tills/${tillId}/name`, { headers: headers() });
  if (!res.ok) return null;
  const data = (await res.json()) as { id: string; name: string };
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
  const res = await fetch(url, { headers: headers() });
  handle401(res);
  if (!res.ok) throw new Error(`API ${res.status} ${res.statusText}`);
  return res.json();
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

export const fetchItems = (pageNumber: number, pageSize: number, search = "") =>
  get<Item[]>(
    `/api/Item/Index?PageNumber=${pageNumber}&PageSize=${pageSize}` +
      (search ? `&Search=${encodeURIComponent(search)}` : ""),
  );

/** Till search — network first, IndexedDB cache when offline. */
export async function searchItemsOfflineAware(term: string, limit = 8): Promise<Item[]> {
  try {
    return await fetchItems(1, limit, term);
  } catch {
    return cachedItemSearch(term, limit);
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
}
export interface CustomerDetail extends CustomerSummary {
  creditAccountId: string | null;
  creditBalancePence: number;
  membership: { tier: string; autoDiscountRate: number; renewalDay: string; expired: boolean } | null;
}

export const searchCustomers = (term: string) =>
  get<CustomerSummary[]>(`/api/v1/customers?take=10${term ? `&search=${encodeURIComponent(term)}` : ""}`);

export const getCustomer = (id: string) => get<CustomerDetail>(`/api/v1/customers/${id}`);

// WP15.1 in-app announcements (till shows Maintenance/Incident only).
export interface ActiveAnnouncement { id: string; severity: string; title: string; body: string; startsAtUtc: string; endsAtUtc: string }
export const fetchActiveAnnouncements = () => get<ActiveAnnouncement[]>("/api/v1/announcements/active");

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

/** Set/replace a customer's membership tier (customers.manage). autoDiscountRate is a fraction (0.1 = 10%). */
export async function setMembership(id: string, tier: string, autoDiscountRate: number): Promise<void> {
  await send("POST", `/api/v1/customers/${encodeURIComponent(id)}/membership`, { tier, autoDiscountRate });
}

/** Redeem store credit against a sale. Idempotent by entryId; throws on overdraw (400). */
export async function redeemCredit(customerId: string, amountPence: number, saleId: string, entryId: string): Promise<void> {
  await send("POST", `/api/v1/customers/${encodeURIComponent(customerId)}/credit/redeem`, {
    amountPence,
    saleId,
    entryId,
    reason: "till sale",
  });
}

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
export async function loadReceiptTemplate(): Promise<void> {
  try {
    const res = await fetch(`/api/v1/stores/${STORE_ID}/receipt-template`, { headers: headers() });
    if (!res.ok) return;
    const data = await res.json();
    _receiptTemplate = data?.receiptTemplateJson ? (JSON.parse(data.receiptTemplateJson) as ReceiptTemplate) : null;
    // Persist so an offline reload still prints with the last-known template.
    if (_receiptTemplate) localStorage.setItem("plutus.receiptTemplate", JSON.stringify(_receiptTemplate));
    else localStorage.removeItem("plutus.receiptTemplate");
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
export const fetchStoreInfo = () => get<StoreInfoView>(`/api/v1/stores/${STORE_ID}/info`);

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
  if (n.includes("credit")) return 3;
  return 1; // Card
};

export async function checkout(
  lines: BasketLine[],
  payments: CheckoutPayment[],
  totals: { totalPence: number; totalExTaxPence: number },
  opts?: { customerId?: string; creditRedeemPence?: number },
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
        vatRateBp: l.exPricePence > 0 ? Math.round((l.pricePence / l.exPricePence - 1) * 10000) : 0,
        vatAmountPence: lineGross - lineEx,
        overriddenFromPence: l.adjusted ? Math.round(l.item.price * 100) : null,
        // Projection metadata for the server's legacy bridge (shape documented there).
        // The members' auto-discount uses sentinel discountId 0 and is FILTERED OUT of the
        // bridge's discounts[] (which maps to legacy Transaction_Discount by real DiscountId —
        // a synthetic id would FK-fail). Its money still flows via discountPence above.
        discountsJson: JSON.stringify({
          itemIdOne: l.item.idOne,
          exUnitPence: l.exPricePence,
          discounts: l.discount && l.discount.discountId !== 0
            ? [{ id: l.discount.discountId, rate: l.discount.amount }]
            : undefined,
          return: l.isReturn && l.originSaleId ? { originSaleId: l.originSaleId } : undefined,
        }),
      };
    }),
  );

  const request: IngestSaleRequest = {
    saleId,
    deviceId: cred.deviceId,
    deviceSeq: await nextDeviceSeq(),
    channel: 1, // SaleChannel.WebPos
    businessDay: businessDay(),
    occurredAtUtc: new Date().toISOString(),
    grossPence: totals.totalPence,
    vatPence: totals.totalPence - totals.totalExTaxPence,
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
