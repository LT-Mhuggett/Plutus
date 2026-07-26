// Thin typed client over the /api/v1 platform surface (WP3.5). Same-origin — Caddy
// reverse-proxies /api/* on the admin host to the DBService. The portal NEVER talks to
// legacy /api/* endpoints except /api/Auth/Login (the shared operator login).

import { setSession, type Session } from "./session.ts";
import { accessToken, signOut } from "./auth.ts";

function authHeaders(): Record<string, string> {
  const t = accessToken();
  return t ? { Authorization: `Bearer ${t}` } : {};
}

export class ApiError extends Error {
  constructor(public status: number, detail: string) {
    super(detail);
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method,
    headers: { ...authHeaders(), ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (res.status === 401) {
    signOut();
    throw new ApiError(401, "Signed out.");
  }
  if (!res.ok) {
    let detail = `${res.status}`;
    try {
      const parsed = await res.json();
      detail = parsed?.detail ?? JSON.stringify(parsed);
    } catch {
      /* keep status */
    }
    throw new ApiError(res.status, detail);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

const get = <T>(url: string) => request<T>("GET", url);
const post = <T>(url: string, body?: unknown) => request<T>("POST", url, body);
const put = <T>(url: string, body?: unknown) => request<T>("PUT", url, body);
const del = <T>(url: string) => request<T>("DELETE", url);

export const gbp = (pence: number) =>
  new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP" }).format(pence / 100);

// ── auth ──

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

// ── reports ──

export interface SummaryBucket {
  period: string;
  grossPence: number;
  vatPence: number;
  txnCount: number;
  avgBasketPence: number;
}
export interface Summary {
  totals: { grossPence: number; vatPence: number; txnCount: number; avgBasketPence: number };
  buckets: SummaryBucket[];
}

export const fetchSummary = (from: string, to: string, granularity: string, level = "company", id = "") =>
  get<Summary>(`/api/v1/reports/summary?from=${from}&to=${to}&granularity=${granularity}&level=${level}${id ? `&id=${id}` : ""}`);

export interface VatBucket {
  period: string;
  vatRateBp: number;
  grossPence: number;
  netPence: number;
  vatPence: number;
}
export const fetchVat = (from: string, to: string, granularity: string) =>
  get<{ totals: { grossPence: number; netPence: number; vatPence: number }; buckets: VatBucket[] }>(
    `/api/v1/reports/vat?from=${from}&to=${to}&granularity=${granularity}`);

export interface SaleRow {
  id: string;
  businessDay: string;
  occurredAtUtc: string;
  tillId: string;
  channel: string;
  grossPence: number;
  vatPence: number;
  legacyRef: string | null;
}
export const fetchSales = (from: string, to: string) => get<SaleRow[]>(`/api/v1/sales?from=${from}&to=${to}&take=500`);

export interface SaleDetail {
  id: string;
  tillId: string;
  deviceId: string;
  deviceSeq: number;
  channel: string;
  businessDay: string;
  occurredAtUtc: string;
  grossPence: number;
  vatPence: number;
  operatorUserId: string | null;
  legacyRef: string | null;
  note: string | null;
  vatReconstructed: boolean;
  lines: { lineNo: number; itemId: string; qty: number; unitPricePence: number; discountPence: number; lineGrossPence: number; vatRateBp: number; vatAmountPence: number; discountsJson: string | null }[];
  tenders: { tenderType: string; amountPence: number; changePence: number }[];
}
export const fetchSaleDetail = (id: string) => get<SaleDetail>(`/api/v1/sales/${id}`);

export const csvUrl = (type: "summary" | "vat", from: string, to: string) =>
  `/api/v1/reports/export.csv?type=${type}&from=${from}&to=${to}`;

// ── users, roles, assignments ──

export interface PortalUser {
  id: string;
  fName: string;
  lName: string;
  email: string;
  active: boolean;
  storeId: number;
  roles: string[];
}
export const fetchUsers = () => get<PortalUser[]>(`/api/v1/users`);
export const createUser = (u: { fName: string; lName: string; email: string; password?: string }) =>
  post<{ id: string }>(`/api/v1/users`, u);
export const deactivateUser = (id: string) => post<void>(`/api/v1/users/${id}/deactivate`);

export interface Role {
  id: string;
  name: string;
  isBuiltIn: boolean;
  grants: { code: string; maxPence: number | null }[];
}
export const fetchRoles = () => get<Role[]>(`/api/v1/roles`);

export interface Assignment {
  id: string;
  roleId: string;
  roleName: string;
  scopeType: string;
  scopeId: string;
  daysOfWeekMask: number | null;
  windowStartLocal: string | null;
  windowEndLocal: string | null;
}
export const fetchAssignments = (userId: string) => get<Assignment[]>(`/api/v1/users/${userId}/role-assignments`);
export const assignRole = (userId: string, roleId: string, scope: string) =>
  post<{ id: string }>(`/api/v1/users/${userId}/role-assignments`, { roleId, scope });
export const unassignRole = (userId: string, assignmentId: string) =>
  del<void>(`/api/v1/users/${userId}/role-assignments/${assignmentId}`);

export interface EffectivePermissionRow {
  code: string;
  maxPence: number | null;
  display: string;
}
export const fetchEffectivePermissions = (userId: string, scope: string) =>
  get<{ permissions: EffectivePermissionRow[] }>(`/api/v1/users/${userId}/effective-permissions?scope=${encodeURIComponent(scope)}`);

// ── companies, stores, tills ──

export interface Company {
  id: string;
  name: string;
  nameAbbr: string;
  vatIN: string;
}
export const fetchCompanies = () => get<Company[]>(`/api/v1/companies`);
export const updateCompany = (id: string, body: Partial<Company>) => put<void>(`/api/v1/companies/${id}`, body);

export interface StoreRow {
  id: number;
  companyId: string;
  name: string | null;
  adLine1: string;
  adLine2: string;
  city: string;
  postCode: string;
  country: string;
  contactNumber: string;
  openingHoursJson: string | null;
  receiptTemplateJson: string | null;
}
export const fetchStores = () => get<StoreRow[]>(`/api/v1/stores`);
export const updateStore = (id: number, body: Partial<StoreRow>) => put<void>(`/api/v1/stores/${id}`, body);
// WP11.3
export const createStore = (body: { companyId?: string; name?: string; adLine1: string; city: string; postCode: string; contactNumber: string }) =>
  post<{ id: number }>(`/api/v1/stores`, body);
export const createStockLocation = (body: { storeId: number; type: string; name: string }) =>
  post<{ id: string }>(`/api/v1/stock/locations`, body);
export interface StockLocationRow { id: string; storeId: number; type: string; name: string }
export const fetchStockLocations = () => get<StockLocationRow[]>(`/api/v1/stock/locations`);

// ── loyalty ──
export interface LoyaltyRow {
  id: string; name: string; email: string | null; phone: string | null;
  tier: string | null; autoDiscountRate: number | null; renewalDay: string | null; expired: boolean; creditBalancePence: number;
}
export const fetchLoyalty = (search?: string) =>
  get<{ count: number; rows: LoyaltyRow[] }>(`/api/v1/loyalty${search ? `?search=${encodeURIComponent(search)}` : ""}`);

// WP11.2 receipt template (per store) + NatApp receipt fields. Stored/echoed opaquely by the API.
export interface ReceiptTemplate {
  storeName?: string;
  addressLines?: string[];
  phone?: string;
  vatNumber?: string;
  headerLines?: string[];
  footerLines?: string[];
  showVatNumber?: boolean;
  showOperator?: boolean;
  showBarcode?: boolean;
}
export const putReceiptTemplate = (storeId: number, tpl: ReceiptTemplate) =>
  put<void>(`/api/v1/stores/${storeId}/receipt-template`, { receiptTemplateJson: JSON.stringify(tpl) });

// ── WP11.4 items-sold report ──
export interface ItemSoldRow {
  dateSold: string; itemIdOne: string; itemName: string; storeId: number; tillId: string;
  tillName: string; staffId: string; staffName: string;
  qty: number; unitPricePence: number; discountPence: number; lineGrossPence: number;
}
export interface ItemsSold {
  from: string; to: string; count: number;
  totals: { qty: number; grossPence: number; discountPence: number };
  rows: ItemSoldRow[];
}
export interface StaffRow { id: string; name: string }
export const itemsSoldQuery = (from: string, to: string, storeId?: number, operatorUserId?: string) =>
  `/api/v1/reports/items-sold?from=${from}&to=${to}&take=2000` +
  (storeId != null ? `&storeId=${storeId}` : "") + (operatorUserId ? `&operatorUserId=${operatorUserId}` : "");
export const fetchItemsSold = (from: string, to: string, storeId?: number, operatorUserId?: string) =>
  get<ItemsSold>(itemsSoldQuery(from, to, storeId, operatorUserId));
export const fetchReportStaff = (storeId?: number) =>
  get<StaffRow[]>(`/api/v1/reports/staff${storeId != null ? `?storeId=${storeId}` : ""}`);

/** Auth-correct CSV download (a plain <a href> can't send the bearer token). */
export async function downloadCsv(url: string, filename: string): Promise<void> {
  const t = accessToken();
  const res = await fetch(url, { headers: t ? { Authorization: `Bearer ${t}` } : {} });
  if (!res.ok) throw new ApiError(res.status, `Export failed (${res.status}).`);
  const blob = await res.blob();
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = filename;
  a.click();
  URL.revokeObjectURL(a.href);
}

export interface TillRow {
  id: string;
  name: string;
  storeId: number;
  lastOnline: string;
  devices: { id: string; status: string; lastSeenSeq: number; createdAtUtc: string }[];
}
export const fetchTills = () => get<TillRow[]>(`/api/v1/tills`);
export const createTill = (storeId: number, name: string) =>
  post<{ tillId: string; enrolmentCode: string; expiresAtUtc: string }>(`/api/v1/tills`, { storeId, name });
export const renameTill = (id: string, name: string) => put<void>(`/api/v1/tills/${id}/name`, { name });
export const revokeTill = (id: string) => post<void>(`/api/v1/tills/${id}/revoke`);
export const deleteTill = (id: string) => del<void>(`/api/v1/tills/${id}`);

// ── periods ──

export interface Period {
  id: string;
  companyId: string;
  name: string;
  startDay: string;
  endDay: string;
  status: string;
  closedAtUtc: string | null;
  snapshotJson: string | null;
}
export const fetchPeriods = () => get<Period[]>(`/api/v1/periods`);
export const createPeriod = (name: string, startDay: string, endDay: string) =>
  post<{ id: string }>(`/api/v1/periods`, { name, startDay, endDay });
export const closePeriod = (id: string) => post<{ id: string; snapshot: unknown }>(`/api/v1/periods/${id}/close`);

// ---- Phase 6: webstore connector (WP6.2 review queue, WP6.4 catalogue/alignment) ----
export interface WebstoreConn {
  id: string; name: string; url: string | null; provider: string; storeId: number | null;
  enabled: boolean; oversellBuffer: number;
  ordersCursorUtc: string | null; productsCursorUtc: string | null; lastFullProductSweepUtc: string | null;
  pendingSkus: number;
}
export interface SkuMapRow {
  id: string; sku: string; status: string; boundItemIdOne: string | null; seenCount: number;
  firstSeenWooOrderId: number | null; firstSeenUtc: string; updatedAtUtc: string;
  web: { name: string; pricePence: number; status: string } | null;
}
export interface WebstoreProductRow {
  wooProductId: number; sku: string | null; name: string; pricePence: number;
  regularPricePence: number | null; stockQuantity: number | null; stockStatus: string | null;
  status: string; permalink: string | null; wooModifiedUtc: string | null; linkedItem: boolean;
}
export interface WebstoreProductsResp {
  total: number; skip: number; take: number; lastRefreshed: string | null; rows: WebstoreProductRow[];
}
export interface AlignmentRow {
  sku: string; webName: string; tillName: string; nameDrift: boolean;
  webPricePence: number; tillPricePence: number; priceDiffPence: number; status: string; stockStatus: string | null;
}
export interface AlignmentResp {
  matched: number; nameDrift: AlignmentRow[]; priceDiffers: AlignmentRow[];
  webOnly: { sku: string | null; name: string; pricePence: number; status: string }[];
  tillOnlyCount: number;
}
export const fetchWebstores = () => get<WebstoreConn[]>(`/api/v1/webstores`);
export const fetchSkuMap = (id: string, status?: string) =>
  get<SkuMapRow[]>(`/api/v1/webstores/${id}/skumap${status ? `?status=${status}` : ""}`);
export const bindSku = (id: string, mapId: string, itemIdOne: string) =>
  post<unknown>(`/api/v1/webstores/${id}/skumap/${mapId}/bind`, { itemIdOne });
export const ignoreSku = (id: string, mapId: string) =>
  post<unknown>(`/api/v1/webstores/${id}/skumap/${mapId}/ignore`);
export const createItemFromSku = (id: string, mapId: string, name?: string, pricePence?: number) =>
  post<unknown>(`/api/v1/webstores/${id}/skumap/${mapId}/create-item`, { name, pricePence });
export const retryParkedOrders = (id: string) =>
  post<{ recorded: number; still: number; notOurs: number }>(`/api/v1/webstores/${id}/retry`);
export const fetchWebstoreProducts = (id: string, opts: { status?: string; linked?: string; skip?: number; take?: number }) => {
  const p = new URLSearchParams();
  if (opts.status) p.set("status", opts.status);
  if (opts.linked) p.set("linked", opts.linked);
  p.set("skip", String(opts.skip ?? 0));
  p.set("take", String(opts.take ?? 50));
  return get<WebstoreProductsResp>(`/api/v1/webstores/${id}/products?${p}`);
};
export const refreshWebstoreProducts = (id: string) =>
  post<{ refreshed: number; requests: number }>(`/api/v1/webstores/${id}/products/refresh`);
export const fetchAlignment = (id: string) => get<AlignmentResp>(`/api/v1/webstores/${id}/alignment`);

// ---- WP6.3 outbound (dry-run journal + mode switch) ----
export interface OutboundLogRow {
  id: number; kind: string; itemIdOne: string; wooProductId: number | null;
  fromValue: string | null; toValue: string | null; mode: string; result: string; lane: string;
  createdAtUtc: string; sentAtUtc: string | null;
}
export interface OutboundLogResp { mode: string; pendingDry: number; rows: OutboundLogRow[] }
export const fetchOutboundLog = (id: string, take = 100) =>
  get<OutboundLogResp>(`/api/v1/webstores/${id}/outbound-log?take=${take}`);
export const setOutboundMode = (id: string, mode: string) =>
  request<{ mode: string }>("PUT", `/api/v1/webstores/${id}/outbound-mode`, { mode });
