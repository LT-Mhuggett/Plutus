// Thin typed layer over fetch — same-origin /api/* is reverse-proxied to the
// DBService by Caddy. No client library needed (see plan §3.5.1).

import { clearSession, getSession, setSession, type Session } from "./session.ts";
import {
  cachedItemById,
  cachedItemSearch,
  cachedMeta,
  cacheItems,
  cacheMeta,
  queueSale,
  queuedSales,
  removeQueued,
} from "./offline.ts";

// Phase 1: single-tenant test environment — the seeded Kapow business/store/till.
// Replaced by a full bootstrap flow in later phases (ids from the seed ETL).
export const BUSINESS_ID = "d5a31aac-159e-9a30-706b-02f9eb935600";
export const STORE_ID = 1;
export const TILL_ID = "f6bf8420-3d06-6b0b-4fd7-32d265b89bb8";
export const BUSINESS_NAME = "Kapow Comics ltd";

function headers(): Record<string, string> {
  const s = getSession();
  return { BusinessId: BUSINESS_ID, ...(s ? { Authorization: `Bearer ${s.token}` } : {}) };
}

/** A 401 means the token expired or was revoked — drop the session and restart at login. */
function handle401(res: Response): void {
  if (res.status === 401) {
    clearSession();
    window.location.reload();
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

// ── checkout ────────────────────────────────────────────────────────────────

export interface SaleLine {
  itemId: string;
  quantity: number;
  /** unit prices in POUNDS (API decimals) */
  unitPrice: number;
  unitExPrice: number;
  /** set when the operator adjusted the price at the till */
  adjusted?: { price: number; exPrice: number };
  /** applied discount — recorded as Transaction_Discount, price stays original */
  discount?: { discountId: number; discountRate: number };
  /** return line: quantity comes back into stock, value subtracts, refund row created */
  isReturn?: boolean;
  originSaleId?: string;
}

export interface Payment {
  payId: number;
  amount: number;
  change: number;
}

export interface CompletedSale {
  saleId: string;
  total: number;
  change: number;
  /** true when the sale was queued offline and will sync on reconnect */
  queued?: boolean;
}

export async function checkout(lines: SaleLine[], payments: Payment[], totals: { total: number; totalExTax: number }): Promise<CompletedSale> {
  const session = getSession();
  if (!session) throw new Error("Not signed in.");
  const employeeId = session.employeeId;
  const saleId = crypto.randomUUID();
  const sold = lines.filter((l) => !l.isReturn);
  const returns = lines.filter((l) => l.isReturn);

  const body = {
    id: saleId,
    total: totals.total,
    totalExTax: totals.totalExTax,
    dateOfSale: new Date().toISOString(),
    employeeId,
    storeId: STORE_ID,
    tillId: TILL_ID,
    transactions: sold.map((l) => ({
      amount: l.quantity,
      itemsCostExPrice: l.adjusted?.exPrice ?? l.unitExPrice,
      itemsCostPrice: l.adjusted?.price ?? l.unitPrice,
      itemId: l.itemId,
      businessId: BUSINESS_ID,
      tillId: TILL_ID,
      saleId,
      checkoutItemChange: l.adjusted
        ? { price: l.adjusted.price, exPrice: l.adjusted.exPrice, itemIdOne: l.itemId, itemIdTwo: BUSINESS_ID }
        : null,
      transaction_Discounts: l.discount ? [{ saleId, discountId: l.discount.discountId, discountRate: l.discount.discountRate }] : null,
    })),
    paymentSales: payments.map((p) => ({ ...p, saleId })),
    refunds: returns.length
      ? returns.map((l) => ({
          reason: "Till return",
          amount: l.quantity,
          itemId: l.itemId,
          businessId: BUSINESS_ID,
          authoriserId: employeeId,
          saleId,
          saleIdReturned: l.originSaleId,
        }))
      : null,
  };

  const stockPatches = lines.map((l) => ({ itemId: l.itemId, change: l.isReturn ? l.quantity : -l.quantity }));
  const change = payments.reduce((c, p) => c + p.change, 0);

  try {
    await send("POST", `/api/Sale`, body);
  } catch (e) {
    // Only NETWORK failures queue (fetch rejects with TypeError when offline) — an
    // HTTP error like a validation 400 must surface to the operator, not sit in the
    // outbox forever. A 401 never reaches here (handle401 reloads to login first).
    if (!(e instanceof TypeError)) throw e;
    await queueSale({ saleId, saleBody: body, stockPatches, queuedAt: new Date().toISOString() });
    notifyOutboxChanged();
    return { saleId, total: totals.total, change, queued: true };
  }

  await applyStockPatches(stockPatches);
  return { saleId, total: totals.total, change };
}

async function applyStockPatches(patches: { itemId: string; change: number }[]): Promise<void> {
  // Best-effort — 404 simply means the item isn't stock-tracked.
  for (const p of patches) {
    await fetch(`/api/Stock/UpdateQuantity/${encodeURIComponent(p.itemId)}`, {
      method: "PATCH",
      headers: { ...headers(), StoreId: String(STORE_ID), "Content-Type": "application/json" },
      body: String(p.change),
    }).catch(() => undefined);
  }
}

// ── outbox drain ────────────────────────────────────────────────────────────

const outboxListeners = new Set<() => void>();
export const onOutboxChanged = (fn: () => void) => {
  outboxListeners.add(fn);
  return () => outboxListeners.delete(fn);
};
const notifyOutboxChanged = () => outboxListeners.forEach((fn) => fn());

let draining = false;

/** Replay queued offline sales, oldest first. Safe to call repeatedly. */
export async function drainOutbox(): Promise<{ sent: number; remaining: number }> {
  if (draining) return { sent: 0, remaining: (await queuedSales()).length };
  draining = true;
  let sent = 0;
  try {
    const queue = (await queuedSales()).sort((a, b) => a.queuedAt.localeCompare(b.queuedAt));
    for (const q of queue) {
      try {
        await send("POST", `/api/Sale`, q.saleBody);
      } catch {
        break; // still offline (or server rejecting) — keep it queued, stop the drain
      }
      await applyStockPatches(q.stockPatches);
      await removeQueued(q.saleId);
      sent++;
    }
  } finally {
    draining = false;
    if (sent > 0) notifyOutboxChanged();
  }
  return { sent, remaining: (await queuedSales()).length };
}
