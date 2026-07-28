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

// ── platform / operator dashboard (WP13.1–13.4; all platform-admin) ──
export interface PlatformTenant { id: string; name: string; status: number; plan: string; entitlements: string[]; createdAtUtc: string; isSandbox: boolean; dataRegion?: string; dpaSignedAtUtc?: string | null; dpaRef?: string | null; planId?: string | null; planPricePenceMonthly?: number | null }
export interface OverrideRow { entitlement: string; deny: boolean; reason: string | null; createdAtUtc: string }
export interface FlagRow { flagName: string; enabled: boolean; reason: string | null; updatedAtUtc: string }
export interface UsageSummaryRow { tenantId: string; totals: Record<string, number>; salesDaily: { day: string; value: number }[] }
export interface HealthTenantRow { tenantId: string; requests: number; err4xx: number; err5xx: number; errorRatePct: number; peakP95Ms: number; maxMs: number; quarantineOpen: number }
export interface HealthResponse { generatedAtUtc: string; tenants: HealthTenantRow[]; consumerLag: { consumer: string; lag: number }[] }
export interface HealthDrillRow { minuteUtc: string; routeGroup: string; count: number; err4xx: number; err5xx: number; p50Ms: number; p95Ms: number; maxMs: number }
export interface AlertRow { alertKey: string; jobName: string; tenantId: string | null; kind: string; message: string; raisedAtUtc: string; lastSeenAtUtc: string; clearedAtUtc: string | null; occurrences: number }
export interface JobRow { jobName: string; tenantId: string | null; runStatus: string; startedAtUtc: string; finishedAtUtc: string | null; detail: string | null; cadenceStatus: string }

export const fetchTenants = () => get<PlatformTenant[]>("/api/v1/tenants");
export const fetchUsageSummary = () => get<UsageSummaryRow[]>("/api/v1/platform/usage/summary");
export const fetchHealth = () => get<HealthResponse>("/api/v1/platform/health");
export const fetchTenantHealth = (tenantId: string) =>
  get<{ tenantId: string; from: string; to: string; rows: HealthDrillRow[] }>(`/api/v1/platform/health/${tenantId}`);
// WP15.2 advisory SLA: monthly availability from TenantRequestStats. month = "YYYY-MM" (omit ⇒ current).
export interface SlaResponse { tenantId: string; month: string; thresholdPct: number; minutesWithTraffic: number; goodMinutes: number; availabilityPct: number; advisory: boolean }
export const fetchSla = (tenantId: string, month?: string) =>
  get<SlaResponse>(`/api/v1/platform/sla?tenantId=${tenantId}${month ? `&month=${month}` : ""}`);
export const fetchAlerts = () => get<AlertRow[]>("/api/v1/platform/alerts");
export const fetchJobs = () => get<JobRow[]>("/api/v1/platform/jobs");
export const setTenantStatus = (tenantId: string, status: number) =>
  put<void>(`/api/v1/tenants/${tenantId}/status`, { status });
export const impersonate = (tenantId: string, userId: string, minutes: number) =>
  post<{ token: string; name: string; expiresAt: string; impersonating: boolean }>(
    `/api/v1/platform/tenants/${tenantId}/impersonate`, { userId, minutes });
// WP14.2 overrides + flags
export const fetchOverrides = (tenantId: string) => get<OverrideRow[]>(`/api/v1/platform/tenants/${tenantId}/overrides`);
export const setOverrides = (tenantId: string, overrides: { entitlement: string; deny: boolean; reason?: string }[]) =>
  put<void>(`/api/v1/platform/tenants/${tenantId}/overrides`, { overrides });
export const fetchFlags = () => get<FlagRow[]>("/api/v1/platform/flags");
export const setFlag = (name: string, enabled: boolean, reason?: string) =>
  put<void>(`/api/v1/platform/flags/${encodeURIComponent(name)}`, { enabled, reason });
// WP14.3 sandbox
export const setSandbox = (tenantId: string, isSandbox: boolean) =>
  put<void>(`/api/v1/platform/tenants/${tenantId}/sandbox`, { isSandbox });
export const resetSandbox = (tenantId: string) =>
  post<{ tenantId: string; sales: number; grossPence: number }>(`/api/v1/platform/tenants/${tenantId}/reset`);
// WP15.1 announcements
export interface ActiveAnnouncement { id: string; severity: string; title: string; body: string; startsAtUtc: string; endsAtUtc: string }
export interface AnnouncementRow extends ActiveAnnouncement { tenantIds: string | null; createdBy: string; createdAtUtc: string }
export const fetchActiveAnnouncements = () => get<ActiveAnnouncement[]>("/api/v1/announcements/active");
export const fetchAnnouncements = () => get<AnnouncementRow[]>("/api/v1/platform/announcements");
export const createAnnouncement = (body: { severity: number; title: string; body: string; startsAtUtc: string; endsAtUtc: string; tenantIds?: string[] }) =>
  post<{ id: string }>("/api/v1/platform/announcements", body);
export const deleteAnnouncement = (id: string) => del<void>(`/api/v1/platform/announcements/${id}`);
// WP16.1 churn signals
export interface SignalRow { tenantId: string; signal: string; detail: string; raisedAtUtc: string }
export const fetchSignals = () => get<SignalRow[]>("/api/v1/platform/signals");
// WP16.2 contracts / renewals
export interface ContractRow { tenantId: string; renewalAtUtc: string; termMonths: number; pricePenceMonthly: number; notes: string | null; updatedAtUtc: string; updatedBy: string }
export const fetchContract = (tenantId: string) => get<ContractRow | null>(`/api/v1/platform/tenants/${tenantId}/contract`);
export const setContract = (tenantId: string, body: { renewalAtUtc: string; termMonths: number; pricePenceMonthly: number; notes: string }) =>
  put<void>(`/api/v1/platform/tenants/${tenantId}/contract`, body);
// WP16.3 margin
export interface MarginRow { tenantId: string; name: string; activityShare: number; revenuePence: number; attributedInfraPence: number; directCostPence: number; costPence: number; marginPence: number }
export interface MarginResponse { configured: boolean; monthlyInfraPence: number; tenants: MarginRow[] }
export const fetchMargin = () => get<MarginResponse>("/api/v1/platform/margin");
// WP16.5 anonymised product analytics
export interface AnalyticsResponse {
  from: string; to: string; kAnonymityFloor: number;
  adoption: { metric: string; tenantsUsing: number; totalUses: number }[];
  routeGroups: { routeGroup: string; tenantsActive: number; requests: number }[];
  funnel: { stage: string; tenants: number; value: number | null }[];
}
export const fetchAnalytics = () => get<AnalyticsResponse>("/api/v1/platform/analytics");
// WP17.1 connector health
export interface ConnectorRow { connector: string; tenantId?: string; lastPollAtUtc: string | null; lastWebhookAtUtc: string | null; lastOutboundAtUtc: string | null; errorStreak: number; lastError: string | null; silent: boolean }
export const fetchConnectors = () => get<ConnectorRow[]>("/api/v1/platform/connectors");
export const fetchConnectorHealth = () => get<ConnectorRow[]>("/api/v1/webstores/connector-health");
// WP18.2 residency & DPA
export const setCompliance = (tenantId: string, body: { dataRegion: string; dpaSignedAtUtc: string | null; dpaRef: string | null }) =>
  put<void>(`/api/v1/tenants/${tenantId}/compliance`, body);
// Notifications (17.3 config layer)
export interface ProviderField { name: string; label: string; secret: boolean; required: boolean }
export interface ProviderInfo { key: string; label: string; channel: number; fields: ProviderField[] }
export interface NotificationConfigRow { channel: number; provider: string; enabled: boolean; config: Record<string, string>; updatedAtUtc: string }
export interface MessageEventRow { tenantId: string; channel: number; toAddress: string; fromAddress: string; status: number; providerMessageId: string | null; detail: string | null; atUtc: string }
export interface SendingIdentityRow { channel: number; fromAddress: string; domain: string | null; verified: boolean }
export const fetchNotificationCatalogue = () => get<ProviderInfo[]>("/api/v1/platform/notifications/catalogue");
export const fetchNotificationConfig = () => get<NotificationConfigRow[]>("/api/v1/platform/notifications/config");
export const setNotificationConfig = (body: { channel: number; provider: string; enabled: boolean; config: Record<string, string> }) =>
  put<void>("/api/v1/platform/notifications/config", body);
export const sendNotificationTest = (body: { channel: number; tenantId: string; to: string }) =>
  post<{ accepted: boolean; providerMessageId: string | null; detail: string | null }>("/api/v1/platform/notifications/test", body);
export const fetchNotificationEvents = () => get<MessageEventRow[]>("/api/v1/platform/notifications/events");
export const fetchSendingIdentities = (tenantId: string) => get<SendingIdentityRow[]>(`/api/v1/platform/tenants/${tenantId}/sending-identity`);
export const setSendingIdentity = (tenantId: string, body: { channel: number; fromAddress: string; domain: string; verified: boolean }) =>
  put<void>(`/api/v1/platform/tenants/${tenantId}/sending-identity`, body);
// OP2 subscription plans (operator)
export interface PlanRow { id: string; name: string; pricePenceMonthly: number; entitlements: string[]; active: boolean; tenantCount: number; updatedAtUtc: string }
export const fetchPlans = () => get<PlanRow[]>("/api/v1/platform/plans");
export const createPlan = (body: { name: string; pricePenceMonthly: number; entitlements: string[]; active: boolean }) =>
  post<{ id: string }>("/api/v1/platform/plans", body);
export const updatePlan = (id: string, body: { name: string; pricePenceMonthly: number; entitlements: string[]; active: boolean }) =>
  put<void>(`/api/v1/platform/plans/${id}`, body);
export const deletePlan = (id: string) => del<void>(`/api/v1/platform/plans/${id}`);
export const assignPlan = (tenantId: string, planId: string | null) =>
  put<void>(`/api/v1/tenants/${tenantId}/plan`, { planId });
// OP4 support tickets
export interface TicketRow { id: string; tenantId?: string; tenant?: string; subject: string; status: number; severity: number; raisedByName: string; assignedTo?: string | null; createdAtUtc: string; updatedAtUtc: string }
export interface TicketMessage { fromOperator: boolean; authorName: string; body: string; atUtc: string }
export const SUPPORT_STATUS = ["Open", "Waiting on client", "Closed"];
export const SUPPORT_SEVERITY = ["Question", "Problem", "Urgent"];
// client side
export const fetchMyTickets = () => get<TicketRow[]>("/api/v1/support/tickets");
export const createTicket = (body: { subject: string; body: string; severity: number }) => post<{ id: string }>("/api/v1/support/tickets", body);
export const fetchMyThread = (id: string) => get<TicketMessage[]>(`/api/v1/support/tickets/${id}/messages`);
export const clientReply = (id: string, body: string) => post<void>(`/api/v1/support/tickets/${id}/messages`, { body });
// operator side
export const fetchTickets = (status?: number) => get<TicketRow[]>(`/api/v1/platform/tickets${status != null ? `?status=${status}` : ""}`);
export const fetchOperatorThread = (id: string) => get<TicketMessage[]>(`/api/v1/platform/tickets/${id}/messages`);
export const operatorReply = (id: string, body: string) => post<void>(`/api/v1/platform/tickets/${id}/reply`, { body });
export const setTicket = (id: string, body: { status?: number; assignedTo?: string }) => put<void>(`/api/v1/platform/tickets/${id}`, body);

// OP3 subscribers landing
export interface ContractLite { tenantId: string; renewalAtUtc: string; pricePenceMonthly: number; termMonths: number }
export const fetchContracts = () => get<ContractLite[]>("/api/v1/platform/contracts");
export interface TenantUser { id: string; name: string; email: string | null; roles: string[] }
export interface TenantUsersResp { lastPortalActivityDay: string | null; users: TenantUser[] }
export const fetchTenantUsers = (tenantId: string) => get<TenantUsersResp>(`/api/v1/platform/tenants/${tenantId}/users`);
// 16.4 billing provider (operator, platform-wide)
export interface CommerceProviderInfo { key: string; label: string; blurb: string; fields: ProviderField[] }
export interface BillingConfig { provider: string; enabled: boolean; config: Record<string, string>; updatedAtUtc: string | null }
export const fetchBillingCatalogue = () => get<CommerceProviderInfo[]>("/api/v1/platform/billing/catalogue");
export const fetchBillingConfig = () => get<BillingConfig>("/api/v1/platform/billing/config");
export const setBillingConfig = (body: { provider: string; enabled: boolean; config: Record<string, string> }) =>
  put<void>("/api/v1/platform/billing/config", body);
// 17.2 per-tenant payment gateway (client-facing, portal.company.manage)
export interface GatewayConfig { provider: string; config: Record<string, string>; updatedAtUtc: string | null }
export const fetchGatewayCatalogue = () => get<CommerceProviderInfo[]>("/api/v1/payments/gateway/catalogue");
export const fetchGatewayConfig = () => get<GatewayConfig>("/api/v1/payments/gateway");
export const setGatewayConfig = (body: { provider: string; config: Record<string, string> }) =>
  put<void>("/api/v1/payments/gateway", body);

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
  operatorName?: string | null;
  lines: { lineNo: number; itemId: string; itemIdOne?: string | null; itemName?: string | null; qty: number; unitPricePence: number; discountPence: number; lineGrossPence: number; vatRateBp: number; vatAmountPence: number; discountsJson: string | null }[];
  tenders: { tenderType: string; amountPence: number; changePence: number }[];
  adjustments?: { type: string; itemId: string | null; qty: number | null; amountPence: number; reason: string; createdAtUtc: string }[];
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

// ---- WP6.1 one-click onboarding ----
export const createWebstoreConnection = (name: string, url: string, storeId?: number) =>
  post<{ id: string; authorizeUrl: string }>(`/api/v1/webstores`, {
    name, url, storeId, returnUrl: `${window.location.origin}${window.location.pathname}?connected=1`,
  });
export const disconnectWebstore = (id: string) =>
  request<{ detail: string }>("DELETE", `/api/v1/webstores/${id}`);
