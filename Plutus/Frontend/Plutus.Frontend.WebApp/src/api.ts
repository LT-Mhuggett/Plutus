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

/** WP11.1: this till's current name (from the fleet list), or null if not visible/none. */
export async function fetchTillName(tillId: string): Promise<string | null> {
  const res = await fetch(`/api/v1/tills`, { headers: headers() });
  if (!res.ok) return null;
  const list = (await res.json()) as { id: string; name: string }[];
  return list.find((t) => t.id === tillId)?.name ?? null;
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

export const fetchSalesSummary = (from: Date, to: Date) =>
  get<SalesSummary>(`/api/Sale/Summary?minDate=${dateOnly(from)}&maxDate=${dateOnly(to)}`);

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

export const fetchSaleDetail = (id: string) => get<SaleDetail>(`/api/Sale/Detail/${id}`);

export interface VatIntegrity {
  offBandCount: number;
  offBandItems: { id: string; name: string; band: string; price: number; exPrice: number; expectedPrice: number }[];
}

export const fetchVatIntegrity = () => get<VatIntegrity>(`/api/Sale/VatIntegrity`);

/** All sales in the date range (IgnorePagination — ranges are shop-scale, not web-scale). */
export const fetchSales = (from: Date, to: Date) =>
  get<Sale[]>(
    `/api/Sale/Index?IgnorePagination=true&MinDateOfSale=${dateOnly(from)}&MaxDateOfSale=${dateOnly(to)}`,
  );

export async function downloadSalesReport(from: Date, to: Date): Promise<void> {
  const res = await fetch(`/api/Sale/SaleReport?minDate=${dateOnly(from)}&maxDate=${dateOnly(to)}`, {
    headers: headers(),
  });
  handle401(res);
  if (!res.ok) throw new Error(`Report failed: API ${res.status}`);
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `${dateOnly(from)}-${dateOnly(to)}-SalesReport.xls`;
  a.click();
  URL.revokeObjectURL(url);
}

/** Receipts show the live business name; cache it so the Receipt component stays sync. */
export const businessName = () => localStorage.getItem("plutus.businessName") || BUSINESS_NAME;

// WP11.2: per-store receipt template (header/footer/toggles), fetched from the server and cached
// so the Receipt component can read it synchronously. Read via the sales.ingest-gated endpoint.
export interface ReceiptTemplate {
  headerLines?: string[];
  footerLines?: string[];
  showVatNumber?: boolean;
  showOperator?: boolean;
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
