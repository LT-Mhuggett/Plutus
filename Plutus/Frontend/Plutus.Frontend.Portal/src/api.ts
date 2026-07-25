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
export const createStore = (body: { companyId?: string; adLine1: string; city: string; postCode: string; contactNumber: string }) =>
  post<{ id: number }>(`/api/v1/stores`, body);
export const createStockLocation = (body: { storeId: number; type: string; name: string }) =>
  post<{ id: string }>(`/api/v1/stock/locations`, body);

// WP11.2 receipt template (per store). Shape owned here; stored/echoed opaquely by the API.
export interface ReceiptTemplate {
  headerLines?: string[];
  footerLines?: string[];
  showVatNumber?: boolean;
  showOperator?: boolean;
  showBarcode?: boolean;
}
export const putReceiptTemplate = (storeId: number, tpl: ReceiptTemplate) =>
  put<void>(`/api/v1/stores/${storeId}/receipt-template`, { receiptTemplateJson: JSON.stringify(tpl) });

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
