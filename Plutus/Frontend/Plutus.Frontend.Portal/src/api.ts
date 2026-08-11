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
export interface GatewayConfig {
  provider: string; config: Record<string, string>; updatedAtUtc: string | null;
  // Card surcharge: percent in basis points (169 = 1.69%) + flat pence. Both zero = none.
  surchargeBp: number; surchargeFlatPence: number;
}
export const fetchGatewayCatalogue = () => get<CommerceProviderInfo[]>("/api/v1/payments/gateway/catalogue");
export const fetchGatewayConfig = () => get<GatewayConfig>("/api/v1/payments/gateway");
export const setGatewayConfig = (body: {
  provider: string; config: Record<string, string>;
  surchargeBp: number; surchargeFlatPence: number;
}) =>
  put<void>("/api/v1/payments/gateway", body);
// Company → Security: this tenant's MFA/SSO requirement (portal.company.manage).
export const fetchMfaRequired = () => get<{ mfaRequired: boolean }>("/api/v1/company/security");
export const setMfaRequired = (mfaRequired: boolean) => put<void>("/api/v1/company/security", { mfaRequired });

// ── auth ──

// Email-first login: ask the server HOW this email should authenticate (password vs Keycloak/OIDC),
// without revealing whether the account exists. Unauthenticated — a plain fetch, not the token client.
export interface AuthMethod { method: "password" | "oidc"; loginHint: string }
export async function fetchAuthMethod(email: string): Promise<AuthMethod> {
  const res = await fetch(`/api/auth/method`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email }),
  });
  if (!res.ok) throw new Error(`Could not start sign-in (${res.status}).`);
  return res.json();
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

// ── reports ──

// WP2.1 dashboard KPIs (the home-screen pills).
export interface DashboardKpis {
  salesTodayPence: number; salesWeekPence: number; weekStart: string;
  activeUsers: number; activeTills: number; activeStores: number; activeWarehouses: number; activeWebstores: number;
  // Z closes in the last 7 days whose counted drawer did not match what Plutus expected.
  // Short and over are summed SEPARATELY on purpose — one till £20 short and another £20 over is
  // money in the wrong drawer, and a single netted figure would report that as a quiet week.
  drawersOutOfBalance: number; drawersShortPence: number; drawersOverPence: number; drawersFromDay: string;
}
export const fetchDashboard = () => get<DashboardKpis>("/api/v1/reports/dashboard");

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

// WP2c: the VAT return is band-shaped, not rate-shaped — a till derives each line's rate from its
// price pair, so one 20% band arrives as 1993–2004bp and totalling by that number would split a
// single rate across a dozen buckets. `vatPence` is the LEGAL figure (VAT fraction on takings,
// Notice 727 §3.4.1); `vatChargedPence` is what the tills actually charged, kept for reconciliation.
export interface VatBucket {
  period: string;
  bandKey: string;
  displayName: string;
  vatClass: string;
  vatRateBp: number;
  grossPence: number;
  netPence: number;
  vatPence: number;
  vatChargedPence: number;
  roundingDifferencePence: number;
  unclassified: boolean;
}
export interface VatReturnTotals {
  grossPence: number; netPence: number; vatPence: number;
  vatChargedPence: number; roundingDifferencePence: number; unclassifiedGrossPence: number;
}
// WP2c-exempt: partial exemption (HMRC Notice 706). Exempt supplies block recovery of attributable
// input tax; zero-rated ones don't. Both charge the customer nothing, so this split is only possible
// because the BAND is recorded on each sale line — the rate alone could never carry it.
export interface VatPartialExemption {
  taxableGrossPence: number; exemptGrossPence: number; outsideScopeGrossPence: number;
  recoverablePercent: number | null;
  applies: boolean;
  /** Takings recorded before the band travelled with the line — the split can't be exact for these. */
  unbandedGrossPence: number;
  basis: string; url: string;
}
export const fetchVat = (from: string, to: string, granularity: string) =>
  get<{ totals: VatReturnTotals; basis: string; partialExemption: VatPartialExemption; buckets: VatBucket[] }>(
    `/api/v1/reports/vat?from=${from}&to=${to}&granularity=${granularity}`);

// ── WP2c: the portal owns the VAT bands, and the tills read them ────────────────────────────────
// The band is the IDENTITY, not the rate: a rate change adds a dated point, it never edits one.
// `vatClass` is NOT derivable from `rateBp` — Zero and Exempt are both 0% and different in law.

export interface VatRatePoint {
  id: string; rateBp: number; effectiveFromUtc: string; note: string | null;
  inForce: boolean; pending: boolean;
}
export interface VatBandAdmin {
  key: string; displayName: string; vatClass: string;
  currentRateBp: number | null; points: VatRatePoint[];
}
export interface VatRule {
  id: string; title: string; whatPlutusDoes: string; where: string; source: string; url: string;
}
// WP2c-exempt: the legacy tax rows items are priced against, and which band each one means. This is
// the ONLY way to say "these items are exempt, not merely zero-rated" — both are 0%, so the rate
// cannot carry it, and the difference decides whether input tax is recoverable (Notice 706).
export interface VatTaxRow {
  legacyTaxId: number; name: string; rateBp: number;
  band: string | null;            // null = ambiguous and undecided (the zero-vs-exempt case)
  mappedExplicitly: boolean;      // false = derived from the rate, nobody has actually decided
  itemCount: number;
}
export const fetchVatBands = () =>
  get<{
    asOfUtc: string; classes: string[]; bands: VatBandAdmin[];
    taxRows: VatTaxRow[]; mappingRequired: boolean; guidance: VatRule[];
  }>(`/api/v1/vat/bands/admin`);
export const setVatTaxMapping = (legacyTaxId: number, band: string) =>
  put<void>(`/api/v1/vat/tax-mapping/${legacyTaxId}`, { band });
export const createVatBand = (key: string, displayName: string, vatClass: string, rateBp: number) =>
  post<{ key: string }>(`/api/v1/vat/bands?rateBp=${rateBp}`, { key, displayName, class: vatClass });
export const updateVatBand = (key: string, displayName: string, vatClass: string) =>
  put<void>(`/api/v1/vat/bands/${encodeURIComponent(key)}`, { key, displayName, class: vatClass });
export const addVatRateChange = (key: string, rateBp: number, effectiveFromUtc: string, note: string) =>
  post<{ id: string }>(`/api/v1/vat/bands/${encodeURIComponent(key)}/rate-changes`,
    { rateBp, effectiveFromUtc, note });
export const cancelVatRateChange = (key: string, id: string) =>
  del<void>(`/api/v1/vat/bands/${encodeURIComponent(key)}/rate-changes/${id}`);

// ── WP2c: restating past VAT periods (Matt, 2026-08-08 — "correct past return") ─────────────────
// The sales data was never wrong; the arithmetic on top of it was. This re-runs both methods over
// the same rollups so the difference per period is a number, not an estimate.
export interface VatCorrectionPeriod {
  key: string; startDay: string; endDay: string;
  complete: boolean; affected: boolean;
  grossPence: number; boxSixPence: number;
  asFiledVatPence: number; restatedVatPence: number; netErrorPence: number;
  unclassifiedGrossPence: number;
}
export interface VatCorrections {
  basis: string; staggerEndMonth: number; filedCorrectlyFrom: string; returnBasis: string;
  periods: VatCorrectionPeriod[];
  summary: {
    periodsAffected: number; asFiledVatPence: number; restatedVatPence: number;
    netErrorPence: number; underdeclared: boolean;
    // Takings no band explains contribute ZERO to the net error — there is no rate to take a
    // fraction of. Surfaced so "nothing to correct" can never quietly mean "nothing classified".
    unclassifiedGrossPence: number;
    thresholdPence: number; thresholdBoxSixPence: number; thresholdBasis: string;
    route: "adjust-next-return" | "vat652";
  } | null;
  guidance: { whatHappened: string; whatToDo: string; caveat: string; source: string; url: string };
}
export const fetchVatCorrections = (basis: string, staggerEndMonth: number) =>
  get<VatCorrections>(`/api/v1/reports/vat-corrections?basis=${basis}&staggerEndMonth=${staggerEndMonth}`);

// WP3.1 rich summary (the till's Summary shape, from summary-rich — SalesV2, tenant-wide; amounts
// in POUNDS, not pence). Powers the portal Reporting→Summary port (deltas, ex-VAT toggle, top items,
// payment split). Separate from fetchSummary (rollup pence buckets) which the Dashboard tab keeps.
export interface SalesSummary {
  totalSales: number; totalSalesExTax: number; totalOrders: number;
  byDay: { date: string; total: number; totalExTax: number; orders: number }[];
  topItems: { itemId: string; name: string; quantity: number; gross: number; grossExTax: number }[];
  byPayMethod: { method: string; total: number }[];
  byTaxRate: { tax: string; gross: number; net: number; vat: number }[];
}
const isoDay = (d: Date) => d.toISOString().slice(0, 10);
export const fetchSalesSummary = (from: Date, to: Date) =>
  get<SalesSummary>(`/api/v1/reports/summary-rich?from=${isoDay(from)}&to=${isoDay(to)}`);

// WP3.5 VAT off-band catalogue integrity check (items whose inc-VAT price disagrees with their band).
export interface VatIntegrity {
  offBandCount: number;
  offBandItems: { id: string; name: string; band: string; price: number; exPrice: number; expectedPrice: number }[];
}
export const fetchVatIntegrity = () => get<VatIntegrity>(`/api/v1/reports/vat-integrity`);

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
  /** FE9.5 — null = never signed in */
  lastLoginAtUtc: string | null;
  /** FE9.1 — false = staff record with no web login yet (invite them) */
  hasLogin: boolean;
}
export const fetchUsers = (includeRemoved = false) =>
  get<PortalUser[]>(`/api/v1/users${includeRemoved ? "?includeRemoved=true" : ""}`);
export const createUser = (u: { fName: string; lName: string; email: string; password?: string }) =>
  post<{ id: string }>(`/api/v1/users`, u);
export const deactivateUser = (id: string) => post<void>(`/api/v1/users/${id}/deactivate`);

// FE9.1 passwords: an admin can set one directly, or email a single-use link (an "invite" when the
// user has no login yet). Delivery is simulated until an Email provider is enabled in Platform.
export const setUserPassword = (id: string, password: string) =>
  post<void>(`/api/v1/users/${id}/password`, { password });
export const sendPasswordReset = (id: string) =>
  post<{ sent: boolean; expiresAtUtc: string; isInvite: boolean }>(`/api/v1/users/${id}/password-reset`);

// FE9.2 remove = deactivate + revoke login + drop roles; the person row (and all history) stays.
export const removeUser = (id: string) =>
  post<{ removed: boolean; loginRevoked: boolean; rolesRemoved: number }>(`/api/v1/users/${id}/remove`);
export const restoreUser = (id: string) => post<void>(`/api/v1/users/${id}/restore`);

// FE9.3 the permission catalogue, described + grouped.
export interface PermissionInfo { code: string; group: string; description: string; ceilingCapable: boolean }
export const fetchPermissions = () => get<PermissionInfo[]>(`/api/v1/permissions`);

export interface RoleGrant { code: string; maxPence: number | null; group: string; description: string }
export interface Role {
  id: string;
  name: string;
  isBuiltIn: boolean;
  memberCount: number;
  grants: RoleGrant[];
}
export const fetchRoles = () => get<Role[]>(`/api/v1/roles`);

// FE9.1 self-service (anonymous, rate-limited) — the login page's "Forgot password?" + completion.
export async function requestPasswordReset(email: string): Promise<void> {
  await fetch(`/api/auth/password-reset/request`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email }),
  });
  // deliberately ignores the response: the API always 204s so it can't be used to test whether an
  // address has an account
}
export async function completePasswordReset(token: string, newPassword: string): Promise<void> {
  const res = await fetch(`/api/auth/password-reset/complete`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ token, newPassword }),
  });
  if (!res.ok) {
    let detail = res.status === 410 ? "This link has expired or has already been used." : `${res.status}`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep */ }
    throw new ApiError(res.status, detail);
  }
}

// ── FE7 gift cards ──────────────────────────────────────────────────────────
// Codes are minted here (worthless until a till sells one), then activated and redeemed at the till.
// The balance is Σ of an append-only ledger, so status and balance can never disagree.

// ⚠ The tenant's HMRC voucher treatment — the decision that must exist BEFORE any card can be
// minted or sold. null = not decided yet (gift cards disabled); locked = a card has been sold under
// the choice, so it can no longer change (its VAT is already declared).
export interface GiftCardSettings {
  treatment: "multi" | "single" | null;
  decidedAtUtc: string | null;
  locked: boolean;
}
export const fetchGiftCardSettings = () => get<GiftCardSettings>(`/api/v1/giftcards/settings`);
export const saveGiftCardSettings = (treatment: "multi" | "single") =>
  put<GiftCardSettings>(`/api/v1/giftcards/settings`, { treatment });

export interface GiftCardRow {
  code: string;
  /** grouped for reading aloud: "K7QP-2M9W-XT4R-8" */
  pretty: string;
  balancePence: number;
  /** unsold | active | spent | expired | void */
  status: string;
  issuedAtUtc: string | null;
  expiresAtUtc: string | null;
  voidedAtUtc: string | null;
  batch: string | null;
  createdAtUtc: string;
  customerId: string | null;
  customerName: string | null;
}
export interface GiftCardEntryRow {
  id: string;
  /** Issue | Redeem | Adjust | Expire */
  type: string;
  amountPence: number;
  reason: string | null;
  saleId: string | null;
  actorUserId: string | null;
  atUtc: string;
}
export interface GiftCardDetail extends GiftCardRow {
  /** the Code 39 payload printed on a voucher ("G" + code) */
  barcode: string;
  soldSaleId: string | null;
  customerMemberNo: string | null;
  entries: GiftCardEntryRow[];
}
export interface GeneratedCard { code: string; pretty: string; barcode: string; expiresAtUtc: string | null }

export const fetchGiftCards = (search = "", status = "all") =>
  get<GiftCardRow[]>(`/api/v1/giftcards?take=1000&status=${encodeURIComponent(status)}` +
    (search ? `&search=${encodeURIComponent(search)}` : ""));
export const fetchGiftCard = (code: string) =>
  get<GiftCardDetail>(`/api/v1/giftcards/${encodeURIComponent(code)}`);
export const generateGiftCards = (count: number, expiresMonths: number | null, batch: string) =>
  post<GeneratedCard[]>(`/api/v1/giftcards/generate`, { count, expiresMonths, batch });
export const voidGiftCard = (code: string, reason: string) =>
  post<{ code: string; status: string; balancePence: number }>(`/api/v1/giftcards/${encodeURIComponent(code)}/void`, { reason });
export const unvoidGiftCard = (code: string) =>
  post<{ code: string; status: string; balancePence: number }>(`/api/v1/giftcards/${encodeURIComponent(code)}/unvoid`);
export const adjustGiftCard = (code: string, amountPence: number, reason: string) =>
  post<{ code: string; balancePence: number }>(`/api/v1/giftcards/${encodeURIComponent(code)}/adjust`, { amountPence, reason });
export const linkGiftCardCustomer = (code: string, customerId: string | null) =>
  post<void>(`/api/v1/giftcards/${encodeURIComponent(code)}/customer`, { customerId });

export interface GiftCardLiability {
  /** money customers have paid that the shop still owes in goods */
  outstandingPence: number;
  outstandingCards: number;
  unsoldCards: number;
  activatedPence: number;
  redeemedPence: number;
  adjustedPence: number;
  expiredPence: number;
  /** balances on voided/expired cards — no longer a liability, but still worth showing */
  lockedPence: number;
}
export const fetchGiftCardLiability = (fromUtc?: string, toUtc?: string) =>
  get<GiftCardLiability>(`/api/v1/giftcards/liability` +
    (fromUtc ? `?fromUtc=${encodeURIComponent(fromUtc)}&toUtc=${encodeURIComponent(toUtc ?? "")}` : ""));

// FE9.5 per-user audit slice: "what has this person actually done?" — asked when deciding whether a
// dormant account is safe to remove. Same endpoint the entity-level trails use, filtered by actor.
export interface AuditRow {
  id: number;
  actorUserId: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  detailJson: string | null;
  atUtc: string;
}
export const fetchUserAudit = (userId: string, take = 100) =>
  get<AuditRow[]>(`/api/v1/audit?actorUserId=${encodeURIComponent(userId)}&take=${take}`);

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

// ── catalogue management: LEGACY bridge (WP4.2/4.4) ──────────────────────────
// The portal normally speaks only /api/v1. Item + category + tax MANAGEMENT is the one
// deliberate exception: it reuses the guardrailed legacy MVC controllers the web till already
// drives (api/Item, api/Category, api/Tax) — identical VAT guardrail, no parallel v1 CRUD to
// build and keep in sync. These controllers scope by a `BusinessId` request header (the till
// hardcodes its single tenant); the portal resolves it once from the tenant's first company
// and caches it. Composite legacy keys are (IdOne = row id, IdTwo = business id).
let _businessId: string | null = null;
export async function businessId(): Promise<string> {
  if (_businessId) return _businessId;
  const companies = await fetchCompanies();
  if (companies.length === 0) throw new ApiError(404, "No company found for this tenant.");
  _businessId = companies[0].id;
  return _businessId;
}

async function legacy<T>(method: string, url: string, body?: unknown): Promise<T> {
  return (await legacyPaged<T>(method, url, body)).rows;
}

/** FE4.2: the row count the legacy `Index` endpoints have always returned in the `X-Pagination`
 *  header — and which every frontend threw away, leaving pagers to guess ("Next" enabled while a
 *  full page came back). Surfacing it is what lets DataTable show a true "X–Y of N" in server mode. */
export interface Paged<T> { rows: T; total: number | null }

export async function legacyPaged<T>(method: string, url: string, body?: unknown): Promise<Paged<T>> {
  const bid = await businessId();
  const res = await fetch(url, {
    method,
    headers: { ...authHeaders(), BusinessId: bid, ...(body !== undefined ? { "Content-Type": "application/json" } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (res.status === 401) { signOut(); throw new ApiError(401, "Signed out."); }
  if (!res.ok) {
    let detail = `${res.status}`;
    try { const parsed = await res.json(); detail = parsed?.detail ?? JSON.stringify(parsed); } catch { /* keep */ }
    throw new ApiError(res.status, detail);
  }
  const rows = res.status === 204 ? (undefined as T) : await res.json();
  return { rows, total: paginationTotal(res) };
}

/** Reads `X-Pagination` (`{TotalCount, PageSize, CurrentPage, TotalPages, …}`); null when the
 *  endpoint doesn't send it, so callers can fall back to the old guess. */
export function paginationTotal(res: Response): number | null {
  const raw = res.headers.get("X-Pagination");
  if (!raw) return null;
  try {
    const meta = JSON.parse(raw) as { TotalCount?: number; totalCount?: number };
    const total = meta.TotalCount ?? meta.totalCount;
    return typeof total === "number" ? total : null;
  } catch {
    return null;
  }
}

export interface Tax { idOne: number; name: string; rate: number } // rate = multiplier, 1.2 = 20%
export interface CatalogueItem {
  idOne: string; name: string; brand: string; desc: string;
  cost: number; exPrice: number; price: number; taxId: number; catId: string;
  /** FE5.5 — true = stock isn't tracked for this item (bags, back-issues) */
  stockUntracked?: boolean;
  /** FE5.4 — non-null = in the Bin (only ever populated in the Bin view) */
  binnedAtUtc?: string | null;
}

// ── FE5.2 current stock (batched — one call per visible page, never one per row) ──
export interface StockLevelLite { itemIdOne: string; untracked: boolean; quantity: number | null }
export const fetchStockLevelsFor = (itemIdOnes: string[]) =>
  post<StockLevelLite[]>(`/api/v1/stock/levels/bulk`, itemIdOnes);

// ── FE5.3 bulk catalogue edits (gated inventory.bulk server-side) ──
export interface BulkCriteria { search?: string; matchAllWords?: boolean; catId?: string | null; binned?: boolean }
export type BulkAction =
  | "set-category" | "clear-category" | "set-brand" | "clear-brand"
  | "bin" | "restore" | "set-untracked" | "clear-untracked";
export interface BulkRequest {
  action: BulkAction;
  value?: string;
  categoryId?: string | null;
  ids?: string[];        // the tick-list…
  criteria?: BulkCriteria; // …or everything matching the current filter
}
/** How many items a criteria selection covers — shown in the confirmation BEFORE anything runs. */
export const bulkCount = (criteria: BulkCriteria) =>
  post<{ count: number; capped: boolean; max: number }>(`/api/v1/items/bulk/count`, criteria);
export const bulkItems = (req: BulkRequest) =>
  post<{ affected: number; detail: string; ids: string[] }>(`/api/v1/items/bulk`, req);
export interface ItemInput {
  id: string; name: string; brand: string; desc: string;
  cost: number; price: number; exPrice: number; taxId: number; catId: string;
  /** FE5.5 — must be carried through an edit; the legacy PUT binds the WHOLE entity, so omitting
   *  it would silently reset the flag (and likewise un-bin a binned item). */
  stockUntracked?: boolean;
  binnedAtUtc?: string | null;
}

export const fetchTaxes = () => legacy<Tax[]>("GET", `/api/Tax/Index?PageNumber=1&PageSize=50`);
// The legacy Item/Index rows already carry every field the edit dialog needs (brand, cost, tax,
// catId), so the list row IS the edit payload — no separate detail fetch.
/** FE4.2: returns rows AND the true total from X-Pagination, so the items list can show
 *  "X–Y of N" instead of a blind Next button. */
export const fetchCatalogueItemsPaged = (pageNumber: number, pageSize: number, search = "", catId = "", binned = false) =>
  legacyPaged<CatalogueItem[]>("GET",
    `/api/Item/Index?PageNumber=${pageNumber}&PageSize=${pageSize}` +
      (search ? `&Search=${encodeURIComponent(search)}` : "") +
      // FE5.0: category filtering is server-side (client-side over one page showed nothing)
      (catId ? `&CatId=${encodeURIComponent(catId)}` : "") +
      // FE5.4: default excludes binned items everywhere; the Bin view opts in
      (binned ? `&Binned=true` : ""));

export const fetchCatalogueItems = (pageNumber: number, pageSize: number, search = "", catId = "") =>
  fetchCatalogueItemsPaged(pageNumber, pageSize, search, catId).then((p) => p.rows);

// PUT/POST bind the full legacy entity — the composite key + businessId must be present, and the
// server overrides exPrice from the tax band (the VAT guardrail), so we send our derived value
// but the band is authoritative. Mirrors the till's itemBody exactly (WebApp/src/api.ts).
async function itemBody(i: ItemInput) {
  const bid = await businessId();
  return {
    id: i.id, idOne: i.id, idTwo: bid,
    name: i.name, brand: i.brand || "-", desc: i.desc ?? "",
    cost: i.cost, exPrice: i.exPrice, price: i.price,
    image: null, amount: 0, taxId: i.taxId, catId: i.catId, businessId: bid,
    // FE5.4/5.5: the legacy PUT binds the whole Item, so these MUST be echoed back or an ordinary
    // edit would clear the untracked flag / silently restore a binned item.
    stockUntracked: i.stockUntracked ?? false,
    binnedAtUtc: i.binnedAtUtc ?? null,
  };
}
/**
 * Duplicate-barcode guard for the Add-item dialog — the same check the web till does, and the
 * NatApp before it (AddEditInventoryViewModel refuses with "Item already exists!").
 *
 * POST /api/Item has NO duplicate check: a taken barcode hits the composite PK (IdOne, IdTwo)
 * and surfaces as a raw 500. `includeBinned=true` because a BINNED item still owns its barcode
 * row and would break the insert just the same, while the default lookup hides it (FE5.4).
 * Returns null when the barcode is free; a lookup failure also returns null so a flaky
 * connection can't veto a legitimate create — the POST stays the final authority.
 */
export async function findItemByBarcode(id: string): Promise<CatalogueItem | null> {
  try {
    return await legacy<CatalogueItem>("GET", `/api/Item/${encodeURIComponent(id)}?includeBinned=true`);
  } catch {
    return null;
  }
}

export const createItem = async (i: ItemInput) => legacy<void>("POST", `/api/Item`, await itemBody(i));
export const updateItem = async (i: ItemInput) => legacy<void>("PUT", `/api/Item/${encodeURIComponent(i.id)}`, await itemBody(i));
// NB: initial stock is set through the v1 Stock ledger (per-location, multi-store correct), not the
// till's legacy /api/Stock write (which assumes store 1) — see the Inventory page's Stock ledger tab.

// ── categories: guarded v1 manager (WP4.4) ──────────────────────────────────
// NOT the legacy api/Category CRUD (whose DELETE cascade-deletes every item in the category — a
// data-loss trap for the webstore). This v1 surface carries item counts, blocks a delete while
// items reference the category (reassign first), and refuses the last category. `id` = the legacy
// Category.IdOne, so it drops straight into an item's catId.
export interface Category { id: string; name: string; description: string; itemCount: number }
export const fetchCategories = () => get<Category[]>(`/api/v1/categories`);
export const createCategory = (name: string, description?: string) =>
  post<{ id: string; name: string }>(`/api/v1/categories`, { name, description });
export const renameCategory = (id: string, name: string, description?: string) =>
  put<void>(`/api/v1/categories/${id}`, { name, description });
export const reassignCategory = (id: string, toId: string) =>
  post<{ moved: number }>(`/api/v1/categories/${encodeURIComponent(id)}/reassign`, { toId });
export const deleteCategory = (id: string) => del<void>(`/api/v1/categories/${id}`);

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

// ── FE10 till themes — colour schemes pushed to stores / tills / groups ──
// ColorsJson slot shape is owned by the frontends (mirrors the till's CSS variables).
export interface ThemeColors { accent?: string; accentInk?: string; surface?: string; surface2?: string; ink?: string; inkMuted?: string; line?: string }
export interface ThemeRow { id: string; name: string; baseMode: "light" | "dark"; colorsJson: string | null; updatedAtUtc: string }
export interface TillGroupRow { id: string; name: string; tillIds: string[] }
/** scope: 0 tenant · 1 store · 2 group · 3 till (server precedence: till > group > store > tenant). */
export interface ThemeAssignmentRow { scope: number; scopeKey: string; themeKey: string; updatedAtUtc: string }
export interface ThemesBundle { themes: ThemeRow[]; groups: TillGroupRow[]; assignments: ThemeAssignmentRow[] }
export const fetchThemes = () => get<ThemesBundle>(`/api/v1/themes`);
export const createTheme = (body: { name: string; baseMode: string; colorsJson: string | null }) =>
  post<{ id: string }>(`/api/v1/themes`, body);
export const updateTheme = (id: string, body: { name: string; baseMode: string; colorsJson: string | null }) =>
  put<void>(`/api/v1/themes/${id}`, body);
export const deleteTheme = (id: string) => del<void>(`/api/v1/themes/${id}`);
/** themeKey: builtin:system | builtin:light | builtin:dark | a theme id; null clears (inherit). */
export const putThemeAssignment = (scope: number, scopeKey: string, themeKey: string | null) =>
  put<void>(`/api/v1/themes/assignments`, { scope, scopeKey, themeKey });
export const createTillGroup = (body: { name: string; tillIds: string[] }) =>
  post<{ id: string }>(`/api/v1/till-groups`, body);
export const updateTillGroup = (id: string, body: { name: string; tillIds: string[] }) =>
  put<void>(`/api/v1/till-groups/${id}`, body);
export const deleteTillGroup = (id: string) => del<void>(`/api/v1/till-groups/${id}`);

// ── loyalty ──
export interface LoyaltyRow {
  id: string; name: string; email: string | null; phone: string | null;
  memberNo: string | null;
  tierId: string | null;
  tier: string | null; autoDiscountRate: number | null; renewalDay: string | null; expired: boolean; creditBalancePence: number;
}
export const fetchLoyalty = (search?: string) =>
  get<{ count: number; rows: LoyaltyRow[] }>(`/api/v1/loyalty${search ? `?search=${encodeURIComponent(search)}` : ""}`);
// WP5.1: create a customer (customers.manage) — used by the Loyalty tab's "Add member" flow, which
// then opens the shared CustomerDialog to set the membership tier.
export const createCustomer = (body: { name: string; email?: string; phone?: string }) =>
  post<{ id: string }>(`/api/v1/customers`, body);

// FE1: the tier catalogue — pre-defined loyalty levels a membership is ASSIGNED (no free text).
// Editing a tier's rate moves every member of it at once (live-follow, server-side).
export interface LoyaltyTier {
  id: string; name: string; autoDiscountRate: number; durationMonths: number;
  active: boolean; sortOrder: number; memberCount: number;
}
export interface LoyaltyTierInput {
  name: string; autoDiscountRate: number; durationMonths?: number; sortOrder?: number; active?: boolean;
}
export const fetchLoyaltyTiers = (includeInactive = false) =>
  get<LoyaltyTier[]>(`/api/v1/loyalty/tiers${includeInactive ? "?includeInactive=true" : ""}`);
export const createLoyaltyTier = (body: LoyaltyTierInput) =>
  post<{ id: string }>(`/api/v1/loyalty/tiers`, body);
export const updateLoyaltyTier = (id: string, body: LoyaltyTierInput) =>
  put<LoyaltyTier>(`/api/v1/loyalty/tiers/${id}`, body);

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
  dateSold: string; itemIdOne: string; itemName: string; category: string | null; storeId: number; tillId: string;
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

// WP3.7 category sales
export interface CategorySalesRow { category: string; qty: number; grossPence: number; discountPence: number; sharePct: number }
export interface CategorySales { from: string; to: string; totals: { grossPence: number; qty: number; categories: number }; rows: CategorySalesRow[] }
export const fetchCategorySales = (from: string, to: string) =>
  get<CategorySales>(`/api/v1/reports/category-sales?from=${from}&to=${to}`);
// WP3.8 best sellers
export interface BestSellerRow { rank: number; itemIdOne: string; itemName: string; category: string | null; qty: number; grossPence: number; sharePct: number }
export const fetchBestSellers = (from: string, to: string, by: "qty" | "gross", take = 25) =>
  get<{ from: string; to: string; by: string; rows: BestSellerRow[] }>(`/api/v1/reports/best-sellers?from=${from}&to=${to}&by=${by}&take=${take}`);
// WP3.9 stock levels (also drives the negative-stock report via filter=negative)
export interface StockLevelRow { stockLocationId: string; location: string; itemIdOne: string; name: string | null; category: string | null; quantity: number }
export interface StockLevels { totalCatalogueItems: number; inStock: number; matched: number; skip: number; take: number; rows: StockLevelRow[] }
export const fetchStockLevels = (opts: { filter?: string; search?: string; locationId?: string; skip?: number; take?: number }) => {
  const p = new URLSearchParams();
  if (opts.filter) p.set("filter", opts.filter);
  if (opts.search) p.set("search", opts.search);
  if (opts.locationId) p.set("locationId", opts.locationId);
  p.set("skip", String(opts.skip ?? 0)); p.set("take", String(opts.take ?? 25));
  return get<StockLevels>(`/api/v1/stock/levels?${p}`);
};

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
  /** When a device on this till last sent a heartbeat — live presence first, the persisted column
   *  second. ⚠ NULL means never heard from, and must render as "never": until 2026-08-11 this field
   *  carried the till's ENROLMENT date, so every row showed a confident, wrong "last online". */
  lastOnline: string | null;
  /** When the till was enrolled. What `lastOnline` used to be, now under its real name. */
  enrolledAtUtc: string;
  /** true = the virtual till a webstore connection sells through (not an enrollable device) */
  isWebstore: boolean;
  devices: {
    id: string;
    status: string;
    lastSeenSeq: number;
    createdAtUtc: string;
    /** FE3.0 hardware-agent telemetry, reported by the web till. reportedAt null = this device has
     *  never reported (native till / pre-FE3 web till); reported with a null version = the web till
     *  looked at localhost and found NO agent installed. */
    agentVersion: string | null;
    agentPrinterName: string | null;
    agentPrinterOnline: boolean | null;
    agentReportedAtUtc: string | null;
    /** Which BUILD this till is running, off its 60s heartbeat — the question asked after every
     *  deploy. From in-memory presence, so null means "not heard from recently", NEVER
     *  "old version": presence rebuilds itself within a minute of a backend restart. */
    appVersion: string | null;
    /** Online · Stale · Offline. */
    presence: string;
    lastSeenUtc: string | null;
    /** Sales queued on the till and not yet pushed. Non-zero on an Offline till is the one worth
     *  chasing — that is money sitting on a machine. */
    outboxDepth: number | null;
  }[];
}
export const fetchTills = () => get<TillRow[]>(`/api/v1/tills`);
export const createTill = (storeId: number, name: string) =>
  post<{ tillId: string; enrolmentCode: string; expiresAtUtc: string }>(`/api/v1/tills`, { storeId, name });
export const renameTill = (id: string, name: string) => put<void>(`/api/v1/tills/${id}/name`, { name });
export const revokeTill = (id: string) => post<void>(`/api/v1/tills/${id}/revoke`);
export const deleteTill = (id: string) => del<void>(`/api/v1/tills/${id}`);
/** FE6.1: a fresh single-use code for an EXISTING till — the till (and its sales history) is kept
 *  and the previous device is retired when the code is redeemed. This is what to use when a till's
 *  browser has lost its credential; creating a NEW till instead is what produced stray tills. */
export const reissueTillCode = (id: string) =>
  post<{ tillId: string; enrolmentCode: string; expiresAtUtc: string }>(`/api/v1/tills/${id}/enrol-code`);
/** FE6.2: move a till to another store (identity preserved). */
export const moveTillToStore = (id: string, storeId: number) =>
  put<void>(`/api/v1/tills/${id}/store`, { storeId });
// WP6.2: approve (→ revoke) or reject (→ active) a device's pending un-enrol request.
export const decideDeviceRemoval = (deviceId: string, approve: boolean) =>
  post<{ status: string }>(`/api/v1/tills/devices/${deviceId}/removal`, { approve });

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
export const fetchWebstoreProducts = (id: string, opts: { status?: string; linked?: string; search?: string; skip?: number; take?: number }) => {
  const p = new URLSearchParams();
  if (opts.status) p.set("status", opts.status);
  if (opts.linked) p.set("linked", opts.linked);
  if (opts.search) p.set("search", opts.search); // FE4.2: server-side name/SKU search
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
