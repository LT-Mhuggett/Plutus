import { useEffect, useMemo, useState } from "react";
import {
  fetchTenants, provisionTenant, type ProvisionedTenant, fetchUsageSummary, fetchHealth, fetchTenantHealth, fetchAlerts, fetchJobs, setTenantStatus, impersonate,
  fetchOverrides, setOverrides, fetchFlags, setFlag, setSandbox, resetSandbox,
  fetchAnnouncements, createAnnouncement, deleteAnnouncement, fetchSla,
  fetchSignals, fetchContract, setContract, fetchMargin, fetchAnalytics, fetchConnectors, setCompliance,
  fetchNotificationCatalogue, fetchNotificationConfig, setNotificationConfig, sendNotificationTest, fetchNotificationEvents,
  fetchSendingIdentities, setSendingIdentity,
  fetchBillingCatalogue, fetchBillingConfig, setBillingConfig, type CommerceProviderInfo, type BillingConfig,
  fetchPlans, createPlan, updatePlan, deletePlan, assignPlan, type PlanRow,
  fetchContracts, fetchTenantUsers, type ContractLite, type TenantUser,
  fetchTickets, fetchOperatorThread, operatorReply, setTicket, SUPPORT_STATUS, SUPPORT_SEVERITY, type TicketRow, type TicketMessage,
  fetchQuarantine, fetchQuarantinePayload, resolveQuarantine, type QuarantineRow,
  type PlatformTenant, type UsageSummaryRow, type HealthResponse, type HealthTenantRow,
  type HealthDrillRow, type AlertRow, type JobRow, type OverrideRow, type FlagRow, type AnnouncementRow, type SlaResponse,
  type SignalRow, type ContractRow, type MarginResponse, type MarginRow, type AnalyticsResponse, type ConnectorRow,
  type ProviderInfo, type NotificationConfigRow, type MessageEventRow,
} from "./api.ts";
import { beginImpersonation } from "./auth.ts";
import DataTable from "./DataTable.tsx";
import { apiDateTime, apiDay, apiMs } from "./apiTime.ts";
import { fetchTicketSummary, type TicketSummary, type TicketClientRow } from "./api.ts";
import { operatorRequestClose, keepTicketOpen } from "./api.ts";

// FE4.5 row aliases for tables whose rows the API nests inside a response object.
type AdoptionRow = AnalyticsResponse["adoption"][number];
type RouteGroupRow = AnalyticsResponse["routeGroups"][number];
type ConsumerLagRow = HealthResponse["consumerLag"][number];

// WP13.4 operator dashboard (platform-admin only). Three screens answering "is anyone having a
// bad day?": Tenants (usage sparkline + health dot, drill to a tenant's p95), Health (error/lag/
// quarantine + alerts feed), Jobs (latest-per-job cadence grid). Hand-rolled SVG, no chart lib.

const STATUS = ["Trial", "Active", "PastDue", "Suspended", "Closed"];
const short = (id: string | null) => (id ? id.slice(0, 8) : "—");

/**
 * ⚠⚠ WP-LIVE (2026-08-21). Matt: *"The 'Live' grey icon needs to be fed from the heart beats. If the
 * tills are active, then the client is active."*
 *
 * ⚠⚠ THE DOT USED TO ANSWER A DIFFERENT QUESTION FROM THE ONE IT WAS LABELLED WITH. Its only input
 * was `TenantRequestStats`, so grey meant *"the API saw no requests from this tenant in the last
 * hour"* while it was read as *"is this customer alive"*. A shop trading all day through a quiet API
 * hour read grey beside its plan and its renewal date.
 *
 * ⚠ `tillsOnline` NOW OUTRANKS SILENCE, and only silence. A live till cannot turn a red dot green:
 * 5xx errors, an open quarantine and a bad error rate all still win, because "the shop is trading"
 * and "the shop is trading badly" are both worth knowing and only one of them needs somebody.
 *
 * ⚠ TRUE GREY IS STILL POSSIBLE and still means something — no traffic AND no till beating. That is
 * a customer who has not touched the platform in an hour, which is exactly what an operator scanning
 * this list is looking for.
 */
function healthColor(h?: HealthTenantRow): string {
  if (!h) return "#9ca3af";                                             // grey — nothing at all
  if (h.err5xx > 0 || h.quarantineOpen > 0 || h.errorRatePct >= 5) return "#dc2626"; // red
  if (h.peakP95Ms >= 1000 || h.errorRatePct > 0) return "#d97706";      // amber
  if ((h.tillsOnline ?? 0) > 0) return "#16a34a";                       // green — a till is beating
  if (h.requests === 0) return "#9ca3af";                               // grey — silent hour
  return "#16a34a";                                                     // green
}

/**
 * Plain-English hover text for the health dot — the words match `healthColor`'s thresholds exactly,
 * and it always lists the underlying last-hour numbers so a red dot is self-explanatory (e.g. a
 * tenant can be red on an open quarantine or error rate even when the visible "5xx" column is 0).
 *
 * ⚠⚠ IT NAMES WHICH SIGNAL ANSWERED. Two inputs that can disagree — API traffic and till presence —
 * must say which one produced the colour, or a green dot on a tenant with no requests reads as a
 * bug. That was the whole complaint about the old one: it gave an answer and not its reason.
 */
function healthTitle(h?: HealthTenantRow): string {
  if (!h) return "No activity in the last hour and no till beating — nothing to report.";

  const online = h.tillsOnline ?? 0;
  const stale = h.tillsStale ?? 0;

  const tills = online > 0
    ? `${online} till${online === 1 ? "" : "s"} beating${stale > 0 ? ` (${stale} stale)` : ""}`
    : stale > 0
      ? `no till beating (${stale} stale)`
      : "no till beating";

  const stats = `${tills} · ${h.requests} requests · 5xx errors ${h.err5xx} · error rate ${h.errorRatePct}% · peak p95 ${h.peakP95Ms}ms · quarantined ${h.quarantineOpen}`;

  if (h.err5xx > 0 || h.quarantineOpen > 0 || h.errorRatePct >= 5) return `Needs attention — ${stats}`;
  if (h.peakP95Ms >= 1000 || h.errorRatePct > 0) return `Slow or minor errors — ${stats}`;
  if (online > 0 && h.requests === 0) return `Trading — a till is beating, though the API has been quiet. ${stats}`;
  if (h.requests === 0) return `Quiet — ${stats}`;
  return `Healthy — ${stats}`;
}

function Dot({ color, title }: { color: string; title: string }) {
  return <span title={title} style={{ display: "inline-block", width: 10, height: 10, borderRadius: "50%", background: color }} />;
}

function Sparkline({ values, w = 120, h = 26 }: { values: number[]; w?: number; h?: number }) {
  if (values.length === 0) return <span className="muted small">—</span>;
  const max = Math.max(1, ...values);
  const step = values.length > 1 ? w / (values.length - 1) : w;
  const pts = values.map((v, i) => `${(i * step).toFixed(1)},${(h - (v / max) * h).toFixed(1)}`).join(" ");
  return (
    <svg width={w} height={h} viewBox={`0 0 ${w} ${h}`} role="img" aria-label="30-day sales">
      <polyline points={pts} fill="none" stroke="currentColor" strokeWidth={1.5} opacity={0.8} />
    </svg>
  );
}

export default function PlatformPage() {
  const [screen, setScreen] = useState<"Subscribers" | "Tickets" | "Health" | "Quarantine" | "Jobs" | "Flags" | "Comms" | "Commercial" | "Analytics" | "Notifications" | "Billing" | "Plans">("Subscribers");
  return (
    <section className="panel">
      {/* ⚠⚠ THE SCREEN SWITCHER IS ITS OWN ROW (2026-08-21). Matt, of the health dashboard: *"Which
          I believe exists, but I can't get to it."* It does exist — it is **Platform → Health** — and
          eleven peer tabs crammed into the toolbar beside the `Platform` heading is why nobody found
          it. On a narrow window they wrapped behind the title; on a wide one they read as decoration
          trailing off to the right.

          ⚠ A `nav.tabs`, the same control the main menu uses, so they read as NAVIGATION rather than
          as buttons that do something to the page. This is the 2026-08-19 look-and-feel rule applied
          within one screen: the same thing should look the same wherever it appears. */}
      <div className="toolbar"><h2 className="grow">Platform</h2></div>
      <nav className="tabs platform-tabs">
        {(["Subscribers", "Tickets", "Plans", "Health", "Quarantine", "Jobs", "Flags", "Comms", "Commercial", "Analytics", "Notifications", "Billing"] as const).map((s) => (
          <button key={s} className={s === screen ? "tab active" : "tab"} onClick={() => setScreen(s)}>{s}</button>
        ))}
      </nav>
      {screen === "Tickets" && <TicketsScreen />}
      {screen === "Plans" && <PlansScreen />}
      {screen === "Subscribers" && <TenantsScreen />}
      {screen === "Health" && <HealthScreen />}
      {screen === "Quarantine" && <QuarantineScreen />}
      {screen === "Jobs" && <JobsScreen />}
      {screen === "Flags" && <FlagsScreen />}
      {screen === "Comms" && <CommsScreen />}
      {screen === "Commercial" && <CommercialScreen />}
      {screen === "Analytics" && <AnalyticsScreen />}
      {screen === "Notifications" && <NotificationsScreen />}
      {screen === "Billing" && <BillingScreen />}
    </section>
  );
}

/**
 * ⚠⚠ THE TICKET SUMMARY (WP-TICKETS, 2026-08-21). Matt: *"There needs to be a summary view of all
 * tickets. Today, 7 days, last month, last 90 days. Which clients have raised etc."*
 *
 * The inbox was a flat list ordered by last update, so "is this week worse than last" and "which
 * customer is struggling" were questions you answered by counting rows on screen.
 *
 * ⚠ EVERY WINDOW CARRIES raised / open / closed / urgent. "12 tickets this week" with 11 closed is a
 * good week and reads as a bad one, and a single urgent among forty questions is not the same week
 * as forty questions.
 */
function TicketSummaryStrip({ summary }: { summary: TicketSummary | null }) {
  if (!summary) return null;

  return (
    <>
      <div className="stat-row">
        {summary.windows.map((w) => (
          <div className="stat" key={w.label}>
            <span className="stat-label">{w.label}</span>
            <span className="stat-value">{w.raised}</span>
            <span className="muted small">
              {w.open} open · {w.closed} closed
              {w.urgent > 0 && <> · <strong>{w.urgent} urgent</strong></>}
            </span>
          </div>
        ))}
      </div>

      {/* ⚠ THE OLDEST STILL-OPEN TICKET, not an average. An average says nothing about the one that
          has been ignored for three weeks, and that is the one that loses a customer. */}
      <p className="muted small">
        {summary.openTotal} open in total
        {summary.oldestOpenAtUtc && <> · oldest raised {apiDateTime(summary.oldestOpenAtUtc)}</>}
      </p>
    </>
  );
}

/** ⚠ WHICH CLIENTS HAVE RAISED — over 90 days, matching the widest pill above so the two agree. */
function TicketsByClient({ rows }: { rows: TicketClientRow[] }) {
  if (rows.length === 0) return null;

  return (
    <>
      <h4>Who has raised tickets (90 days)</h4>
      <DataTable<TicketClientRow>
        columns={[
          { key: "tenant", label: "Subscriber" },
          { key: "raised", label: "Raised", numeric: true },
          { key: "open", label: "Open", numeric: true },
          {
            key: "urgent", label: "Urgent", numeric: true,
            render: (r) => (r.urgent > 0 ? <strong>{r.urgent}</strong> : <span className="muted">—</span>),
          },
          { key: "lastAtUtc", label: "Last activity", render: (r) => <span className="small">{apiDateTime(r.lastAtUtc)}</span> },
        ]}
        rows={rows} getKey={(r) => r.tenantId}
        initialSortKey="open" initialSortDir="desc"
        search={(r) => r.tenant}
        searchPlaceholder="Search subscriber…"
        emptyText="Nobody has raised a ticket in 90 days."
      />
    </>
  );
}

// OP4: operator ticket inbox — cross-tenant support tickets, reply + set status/assignee.
function TicketsScreen() {
  const [tickets, setTickets] = useState<TicketRow[]>([]);
  const [summary, setSummary] = useState<TicketSummary | null>(null);
  const [filter, setFilter] = useState<number | "">("");
  const [openId, setOpenId] = useState<string | null>(null);
  const [error, setError] = useState("");
  const refresh = () => {
    // ⚠ The summary is refreshed with the list, not once on mount: closing a ticket from the thread
    // changes both, and a stale pill row beside a fresh table is worse than no pills.
    void fetchTicketSummary().then(setSummary).catch(() => undefined);
    return fetchTickets(filter === "" ? undefined : filter).then((t) => { setTickets(t); setError(""); }).catch((e) => setError(String(e)));
  };
  useEffect(() => { void refresh(); }, [filter]); // eslint-disable-line react-hooks/exhaustive-deps

  if (openId) {
    const t = tickets.find((x) => x.id === openId);
    return <TicketThread ticket={t} onBack={() => { setOpenId(null); void refresh(); }} />;
  }
  return (
    <>
      {error && <p className="error">{error}</p>}
      <TicketSummaryStrip summary={summary} />
      <div className="toolbar">
        <label>Status
          <select value={filter} onChange={(e) => setFilter(e.target.value === "" ? "" : Number(e.target.value))}>
            <option value="">All</option>{SUPPORT_STATUS.map((s, i) => <option key={i} value={i}>{s}</option>)}
          </select>
        </label>
      </div>
      <DataTable<TicketRow>
        columns={[
          { key: "tenant", label: "Subscriber", render: (t) => t.tenant ?? "—" },
          { key: "subject", label: "Subject" },
          { key: "severity", label: "Severity", render: (t) => SUPPORT_SEVERITY[t.severity] ?? String(t.severity) },
          { key: "status", label: "Status", render: (t) => SUPPORT_STATUS[t.status] ?? String(t.status) },
          { key: "updatedAtUtc", label: "Updated", render: (t) => <span className="small">{apiDateTime(t.updatedAtUtc)}</span> },
        ]}
        rows={tickets} getKey={(t) => t.id} initialSortKey="updatedAtUtc" initialSortDir="desc"
        search={(t) => `${t.tenant ?? ""} ${t.subject} ${t.raisedByName}`}
        searchPlaceholder="Search subscriber / subject…"
        rowActions={(t) => <button className="ghost small" onClick={() => setOpenId(t.id)}>Open</button>}
        emptyText="No tickets."
      />
      <TicketsByClient rows={summary?.byClient ?? []} />
    </>
  );
}

/**
 * One ticket's thread, operator side.
 *
 * ⚠⚠ A CLOSED TICKET NOW **LOOKS** CLOSED (WP-TICKETS, 2026-08-21). Matt: *"the button 'Close' I
 * assume closes the ticket, but there is nothing visual within the ticket itself?"* It did close it,
 * and the thread said nothing at all — the only trace was a status word in a list the reader had
 * navigated away from.
 */
function TicketThread({ ticket, onBack }: { ticket?: TicketRow; onBack: () => void }) {
  const [msgs, setMsgs] = useState<TicketMessage[]>([]);
  const [reply, setReply] = useState("");
  const [error, setError] = useState("");
  const id = ticket?.id ?? "";
  const load = () => fetchOperatorThread(id).then(setMsgs).catch((e) => setError(String(e)));
  useEffect(() => { if (id) void load(); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  // ⚠ `onBack()` after an action that changes the ticket ROW, not just the thread: closing or
  // asking to close alters the list behind, and this component only ever re-reads its messages.
  const act = (p: Promise<unknown>) => void p.then(() => { setReply(""); return load(); }).catch((e) => setError(String(e)));
  const actAndBack = (p: Promise<unknown>) => void p.then(onBack).catch((e) => setError(String(e)));

  const closed = ticket?.status === 2;
  const asked = ticket?.closureRequestedByOperator;

  return (
    <>
      <div className="toolbar"><button className="ghost small" onClick={onBack}>← Tickets</button><h3 className="grow">{ticket?.tenant} — {ticket?.subject}</h3></div>
      {error && <p className="error small">{error}</p>}

      {/* ⚠⚠ THE STATE OF THE TICKET, IN THE THREAD. Both of these were invisible here before: a
          closed ticket read exactly like an open one, and a standing closure request appeared
          nowhere at all. */}
      {closed && (
        <p className="muted small">
          🔒 <strong>Closed</strong>
          {ticket?.closedAtUtc && <> on {apiDateTime(ticket.closedAtUtc)}</>}. The client can no
          longer reply to this thread.
        </p>
      )}
      {!closed && asked === true && (
        <p className="muted small">
          You have asked the client to close this{ticket?.closureRequestedAtUtc && <> ({apiDateTime(ticket.closureRequestedAtUtc)})</>} — waiting for them.
        </p>
      )}
      {!closed && asked === false && (
        <p className="muted small">
          ⚠ <strong>The client has asked to close this.</strong>
        </p>
      )}

      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {msgs.map((m, i) => (
          <div key={i} style={{ alignSelf: m.fromOperator ? "flex-end" : "flex-start", maxWidth: "75%", background: m.fromOperator ? "var(--accent)" : "var(--panel-alt, #eef1f5)", color: m.fromOperator ? "var(--accent-ink)" : "inherit", borderRadius: 8, padding: "6px 10px" }}>
            <div className="muted small">{m.authorName} · {apiDateTime(m.atUtc)}</div>
            <div>{m.body}</div>
          </div>
        ))}
        {msgs.length === 0 && <p className="muted">No messages.</p>}
      </div>

      {/* ⚠ A CLOSED THREAD OFFERS NO REPLY BOX. The server refuses a client reply on a closed ticket,
          and an input that looks usable and is not is worse than one that is absent. */}
      {!closed && (
        <div className="toolbar" style={{ marginTop: 12 }}>
          <input className="grow" placeholder="Reply to the client…" value={reply} maxLength={4000} onChange={(e) => setReply(e.target.value)} />
          <button className="primary small" disabled={!reply.trim()} onClick={() => act(operatorReply(id, reply.trim()))}>Reply</button>

          {/* ⚠ ASKING IS THE POLITE PATH AND CLOSING IS STILL THERE. Support sometimes has to end a
              thread; usually it should ask, and until now it could only do the former. */}
          {asked !== true && (
            <button className="ghost small" onClick={() => actAndBack(operatorRequestClose(id))}>Ask to close</button>
          )}
          {asked !== null && asked !== undefined && (
            <button className="ghost small" onClick={() => actAndBack(keepTicketOpen(id))}>Keep open</button>
          )}
          <button className="ghost small" onClick={() => actAndBack(setTicket(id, { status: 2 }))}>Close now</button>
        </div>
      )}
    </>
  );
}

// OP2: subscription plans — the operator's named price list. Assigning a plan to a tenant (on the
// tenant detail) copies its name + entitlement bundle onto that tenant.
function PlansScreen() {
  const [plans, setPlans] = useState<PlanRow[]>([]);
  const [form, setForm] = useState({ id: "", name: "", price: "", entitlements: "", active: true });
  const [error, setError] = useState("");
  const refresh = () => fetchPlans().then((p) => { setPlans(p); setError(""); }).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, []);

  const edit = (p: PlanRow) => setForm({ id: p.id, name: p.name, price: (p.pricePenceMonthly / 100).toString(), entitlements: p.entitlements.join(", "), active: p.active });
  const reset = () => setForm({ id: "", name: "", price: "", entitlements: "", active: true });

  function save() {
    const body = {
      name: form.name.trim(),
      pricePenceMonthly: Math.round((Number(form.price) || 0) * 100),
      entitlements: form.entitlements.split(",").map((s) => s.trim()).filter(Boolean),
      active: form.active,
    };
    const op = form.id ? updatePlan(form.id, body) : createPlan(body).then(() => undefined);
    void op.then(() => { reset(); return refresh(); }).catch((e) => setError(String(e)));
  }

  return (
    <>
      <p className="muted small">Named price list. Assign a plan to a subscriber on their detail page — it sets their entitlement bundle and list price (a negotiated contract price still overrides it for margin).</p>
      {error && <p className="error">{error}</p>}
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Name <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Standard" /></label>
        <label>£/mo <input className="short" inputMode="decimal" value={form.price} onChange={(e) => setForm({ ...form, price: e.target.value })} /></label>
        <label>Entitlements <input className="mono" value={form.entitlements} onChange={(e) => setForm({ ...form, entitlements: e.target.value })} placeholder="woo-connector, …" /></label>
        <label>Active <input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} /></label>
        <button className="primary small" disabled={!form.name.trim()} onClick={save}>{form.id ? "Update" : "Create"} plan</button>
        {form.id && <button className="ghost small" onClick={reset}>Cancel</button>}
      </div>
      <DataTable<PlanRow>
        columns={[
          { key: "name", label: "Plan" },
          { key: "pricePenceMonthly", label: "£/mo", numeric: true, render: (p) => pounds(p.pricePenceMonthly) },
          { key: "entitlements", label: "Entitlements", sortable: false, render: (p) => <span className="small mono">{p.entitlements.join(", ") || "—"}</span> },
          { key: "active", label: "Active", render: (p) => (p.active ? "yes" : "no") },
          { key: "tenantCount", label: "Tenants", numeric: true },
        ]}
        rows={plans} getKey={(p) => p.id} initialSortKey="name"
        search={(p) => `${p.name} ${p.entitlements.join(" ")}`}
        searchPlaceholder="Search plan / entitlement…"
        rowActions={(p) => (
          <>
            <button className="ghost small" onClick={() => edit(p)}>Edit</button>
            <button className="ghost small" disabled={p.tenantCount > 0} title={p.tenantCount > 0 ? "reassign tenants first" : ""}
              onClick={() => { if (confirm(`Delete plan "${p.name}"?`)) void deletePlan(p.id).then(refresh).catch((e) => setError(String(e))); }}>Delete</button>
          </>
        )}
        emptyText="No plans yet — create one above."
      />
    </>
  );
}

// 16.4: the operator's platform-wide billing provider (Stripe / Paddle / Chargebee / manual).
// Secrets are write-only (shown as __set__ once saved); the concrete adapter is wired when an
// account exists — until then selection + keys are stored and dunning stays manual.
function BillingScreen() {
  const [catalogue, setCatalogue] = useState<CommerceProviderInfo[]>([]);
  const [current, setCurrent] = useState<BillingConfig | null>(null);
  const [provider, setProvider] = useState("manual");
  const [enabled, setEnabled] = useState(false);
  const [config, setConfig] = useState<Record<string, string>>({});
  const [msg, setMsg] = useState("");
  const [error, setError] = useState("");

  const refresh = () =>
    Promise.all([fetchBillingCatalogue(), fetchBillingConfig()])
      .then(([c, cfg]) => { setCatalogue(c); setCurrent(cfg); setError(""); })
      .catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, []);
  useEffect(() => {
    if (!current) return;
    setProvider(current.provider); setEnabled(current.enabled); setConfig(current.config);
  }, [current]);

  const info = catalogue.find((p) => p.key === provider);

  return (
    <>
      <p className="muted small">One platform-wide choice: who bills your tenants. <strong>Manual</strong> = you invoice yourself (no automation). Selecting Stripe/Paddle/Chargebee stores the keys now; automated dunning activates when that provider's adapter is wired.</p>
      {error && <p className="error">{error}</p>}
      {msg && <p className="muted small">{msg}</p>}
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Provider
          <select value={provider} onChange={(e) => { setProvider(e.target.value); setConfig({}); }}>
            {catalogue.map((p) => <option key={p.key} value={p.key}>{p.label}</option>)}
          </select>
        </label>
        <label>Enabled <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /></label>
      </div>
      {info && <p className="muted small">{info.blurb}</p>}
      {info && info.fields.length > 0 && (
        <div className="toolbar" style={{ flexWrap: "wrap" }}>
          {info.fields.map((f) => (
            <label key={f.name}>{f.label}{f.required ? " *" : ""}
              <input type={f.secret ? "password" : "text"} value={config[f.name] ?? ""}
                placeholder={f.secret ? "(unchanged)" : ""}
                onChange={(e) => setConfig({ ...config, [f.name]: e.target.value })} />
            </label>
          ))}
        </div>
      )}
      <div className="toolbar">
        <button className="primary small" onClick={() =>
          void setBillingConfig({ provider, enabled, config })
            .then(() => { setMsg("Saved."); return refresh(); }).catch((e) => setError(String(e)))}>
          Save billing configuration
        </button>
      </div>
    </>
  );
}

// Notifications (17.3 config layer): pick a provider per channel, fill its fields (secrets are
// write-only), fire a test (SIMULATED until an adapter is wired), and read the delivery ledger.
function NotificationsScreen() {
  const [catalogue, setCatalogue] = useState<ProviderInfo[]>([]);
  const [rows, setRows] = useState<NotificationConfigRow[]>([]);
  const [events, setEvents] = useState<MessageEventRow[]>([]);
  const [channel, setChannel] = useState(0);
  const [provider, setProvider] = useState("none");
  const [enabled, setEnabled] = useState(false);
  const [config, setConfig] = useState<Record<string, string>>({});
  const [testTo, setTestTo] = useState("");
  const [testTenant, setTestTenant] = useState("");
  const [msg, setMsg] = useState("");
  const [error, setError] = useState("");

  const refresh = () =>
    Promise.all([fetchNotificationCatalogue(), fetchNotificationConfig(), fetchNotificationEvents()])
      .then(([c, cfg, e]) => { setCatalogue(c); setRows(cfg); setEvents(e); setError(""); })
      .catch((ex) => setError(String(ex)));
  useEffect(() => { void refresh(); }, []);

  // when channel or config rows change, load that channel's saved state into the editor
  useEffect(() => {
    const row = rows.find((r) => r.channel === channel);
    setProvider(row?.provider ?? "none");
    setEnabled(row?.enabled ?? false);
    setConfig(row?.config ?? {});
  }, [channel, rows]);

  const providersForChannel = catalogue.filter((p) => p.channel === channel || p.key === "none");
  const fields = catalogue.find((p) => p.key === provider)?.fields ?? [];
  const STATUS = ["Queued", "Sent", "Bounced", "Complained", "Failed"];

  function save() {
    void setNotificationConfig({ channel, provider, enabled, config })
      .then(() => { setMsg("Saved."); return refresh(); }).catch((e) => setError(String(e)));
  }
  function test() {
    void sendNotificationTest({ channel, tenantId: testTenant.trim() || "00000000-0000-0000-0000-000000000000", to: testTo.trim() })
      .then((r) => { setMsg(`Test: ${r.accepted ? "accepted" : "not sent"} — ${r.detail ?? ""}`); return refresh(); })
      .catch((e) => setError(String(e)));
  }

  return (
    <>
      <p className="muted small">Choose a provider per channel and fill in its keys (secrets are write-only — shown as <span className="mono">__set__</span> once saved). With no adapter wired yet a selected provider runs in <strong>SIMULATED</strong> mode so you can exercise the whole flow now.</p>
      {error && <p className="error">{error}</p>}
      {msg && <p className="muted small">{msg}</p>}
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Channel
          <select value={channel} onChange={(e) => setChannel(Number(e.target.value))}>
            <option value={0}>Email</option><option value={1}>SMS</option>
          </select>
        </label>
        <label>Provider
          <select value={provider} onChange={(e) => { setProvider(e.target.value); setConfig({}); }}>
            {providersForChannel.map((p) => <option key={p.key} value={p.key}>{p.label}</option>)}
          </select>
        </label>
        <label>Enabled <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /></label>
      </div>
      {fields.length > 0 && (
        <div className="toolbar" style={{ flexWrap: "wrap" }}>
          {fields.map((f) => (
            <label key={f.name}>{f.label}{f.required ? " *" : ""}
              <input type={f.secret ? "password" : "text"} value={config[f.name] ?? ""}
                placeholder={f.secret ? "(unchanged)" : ""}
                onChange={(e) => setConfig({ ...config, [f.name]: e.target.value })} />
            </label>
          ))}
        </div>
      )}
      <div className="toolbar"><button className="primary small" onClick={save}>Save configuration</button></div>

      <h4>Send a test</h4>
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Tenant id <input className="mono" placeholder="(optional)" value={testTenant} onChange={(e) => setTestTenant(e.target.value)} /></label>
        <label>To <input value={testTo} onChange={(e) => setTestTo(e.target.value)} placeholder="buyer@example.com" /></label>
        <button className="ghost small" disabled={!testTo.trim()} onClick={test}>Send test</button>
      </div>

      <h4>Delivery log</h4>
      <DataTable<MessageEventRow>
        columns={[
          { key: "atUtc", label: "When", render: (e) => <span className="small">{apiDateTime(e.atUtc)}</span> },
          { key: "channel", label: "Ch", render: (e) => (e.channel === 0 ? "email" : "sms") },
          { key: "toAddress", label: "To", render: (e) => <span className="small">{e.toAddress}</span> },
          { key: "fromAddress", label: "From", render: (e) => <span className="small">{e.fromAddress}</span> },
          { key: "status", label: "Status", render: (e) => STATUS[e.status] ?? String(e.status) },
          { key: "detail", label: "Detail", render: (e) => <span className="small">{e.detail ?? "—"}</span> },
        ]}
        rows={events} getKey={(e) => `${e.atUtc}-${e.toAddress}-${e.channel}`}
        initialSortKey="atUtc" initialSortDir="desc"
        search={(e) => `${e.toAddress} ${e.fromAddress} ${e.detail ?? ""} ${e.tenantId}`}
        searchPlaceholder="Search recipient / detail…"
        emptyText="No messages sent yet."
      />
    </>
  );
}

// WP16.3 margin view + WP16.5 anonymised analytics.
const pounds = (pence: number) => `£${(pence / 100).toFixed(2)}`;

function CommercialScreen() {
  const [margin, setMargin] = useState<MarginResponse | null>(null);
  const [error, setError] = useState("");
  useEffect(() => { fetchMargin().then(setMargin).catch((e) => setError(String(e))); }, []);
  if (error) return <p className="error">{error}</p>;
  if (!margin) return <p className="muted">Loading…</p>;
  if (!margin.configured)
    return <p className="muted">Margin is unconfigured. Set <span className="mono">PLATFORM_COSTS_PATH</span> to a <span className="mono">platform-costs.json</span> ({"{ monthlyInfraPence, directCostsPence }"}) to enable it.</p>;
  return (
    <>
      <p className="muted small">Advisory. Revenue from the contract price; cost = share of {pounds(margin.monthlyInfraPence)}/mo infra (by 30-day sales activity) + direct costs. Single shared box — not metered allocation.</p>
      <DataTable<MarginRow>
        columns={[
          { key: "name", label: "Tenant" },
          { key: "activityShare", label: "Activity", numeric: true, render: (t) => `${(t.activityShare * 100).toFixed(1)}%` },
          { key: "revenuePence", label: "Revenue", numeric: true, render: (t) => pounds(t.revenuePence) },
          { key: "attributedInfraPence", label: "Infra", numeric: true, render: (t) => pounds(t.attributedInfraPence) },
          { key: "directCostPence", label: "Direct", numeric: true, render: (t) => pounds(t.directCostPence) },
          { key: "costPence", label: "Cost", numeric: true, render: (t) => pounds(t.costPence) },
          {
            key: "marginPence", label: "Margin", numeric: true,
            render: (t) => <span style={{ color: t.marginPence >= 0 ? "#16a34a" : "#dc2626" }}>{pounds(t.marginPence)}</span>,
          },
        ]}
        rows={margin.tenants} getKey={(t) => t.tenantId} initialSortKey="marginPence" initialSortDir="desc"
        search={(t) => t.name}
        searchPlaceholder="Search tenant…"
        emptyText="No tenants."
      />
    </>
  );
}

function AnalyticsScreen() {
  const [a, setA] = useState<AnalyticsResponse | null>(null);
  const [error, setError] = useState("");
  useEffect(() => { fetchAnalytics().then(setA).catch((e) => setError(String(e))); }, []);
  if (error) return <p className="error">{error}</p>;
  if (!a) return <p className="muted">Loading…</p>;
  return (
    <>
      <p className="muted small">Aggregate-only, anonymised. {a.from} → {a.to}. Metrics with fewer than {a.kAnonymityFloor} contributing tenants are suppressed (k-anonymity).</p>
      <h4>Feature adoption</h4>
      <DataTable<AdoptionRow>
        columns={[
          { key: "metric", label: "Metric", render: (m) => <span className="mono">{m.metric}</span> },
          { key: "tenantsUsing", label: "Tenants using", numeric: true },
          { key: "totalUses", label: "Total uses", numeric: true },
        ]}
        rows={a.adoption} getKey={(m) => m.metric} initialSortKey="totalUses" initialSortDir="desc"
        search={(m) => m.metric}
        searchPlaceholder="Search metric…"
        emptyText="Nothing above the k-anonymity floor yet."
      />
      <h4>Surface activity (route groups)</h4>
      <DataTable<RouteGroupRow>
        columns={[
          { key: "routeGroup", label: "Route group", render: (r) => <span className="mono">{r.routeGroup}</span> },
          { key: "tenantsActive", label: "Tenants active", numeric: true },
          { key: "requests", label: "Requests", numeric: true },
        ]}
        rows={a.routeGroups} getKey={(r) => r.routeGroup} initialSortKey="requests" initialSortDir="desc"
        search={(r) => r.routeGroup}
        searchPlaceholder="Search route group…"
        emptyText="Nothing above the k-anonymity floor yet."
      />
      <h4>Login → sale funnel (coarse)</h4>
      {/* FE4.5: deliberately NOT a DataTable — the funnel is a fixed ordered sequence of stages
          (that order IS the meaning), so sorting or paging it would only destroy information.
          Same call as VAT-by-band and the payment split. */}
      <table>
        <thead><tr><th>Stage</th><th className="num">Tenants</th><th className="num">Volume</th></tr></thead>
        <tbody>
          {a.funnel.map((f) => <tr key={f.stage}><td className="mono">{f.stage}</td><td className="num">{f.tenants}</td><td className="num">{f.value ?? "—"}</td></tr>)}
        </tbody>
      </table>
    </>
  );
}

/**
 * **Platform → Quarantine: the sales that did not get in.**
 *
 * ⚠⚠ THE SCREEN THE RED DOT NEEDED. `quarantineOpen > 0` turns a tenant red on Health and holds it
 * there for ever, and until 2026-08-22 there was nowhere to see what was stuck. Matt: *"Where is
 * quarentine? What can I do to view and resolve these issues?"*
 *
 * ⚠ A QUARANTINED SALE IS NOT LOST AND NOT ACCEPTED — the ingest parks anything failing validation
 * rather than rewriting it (which would change what a customer was charged) or dropping it (which
 * would lose money silently). Every row is a decision somebody has to make.
 *
 * ⚠⚠ AND THE ACTION DEPENDS ON THE SOURCE, which is why `source` is the second column and not
 * buried. A `migration` row holds only a legacy reference — there is no sale to replay, so the only
 * honest action is to look it up in the old system and dismiss with a note. A Retry button on one of
 * those would tell the operator the sale had been recovered when nothing had happened at all.
 */
function QuarantineScreen() {
  const [state, setStateFilter] = useState<"open" | "resolved" | "all">("open");
  const [rows, setRows] = useState<QuarantineRow[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState<QuarantineRow | null>(null);

  const refresh = () =>
    fetchQuarantine(state).then((r) => { setRows(r); setError(""); }).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, [state]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <>
      <p className="muted small">
        Sales the platform refused rather than guess at. Nothing here has been lost — each is still
        exactly as it arrived — but none of them appear in reporting or on a VAT return until they
        are recorded, and dismissing a row does <strong>not</strong> record the sale.
      </p>

      <div className="toolbar">
        <nav className="tabs">
          {(["open", "resolved", "all"] as const).map((s) => (
            <button key={s} className={s === state ? "tab active" : "tab"} onClick={() => setStateFilter(s)}>
              {s === "open" ? "Open" : s === "resolved" ? "Resolved" : "All"}
            </button>
          ))}
        </nav>
        <span className="grow" />
        <button className="ghost small" onClick={() => void refresh()}>Refresh</button>
      </div>

      {error && <p className="error small">{error}</p>}

      <DataTable<QuarantineRow>
        columns={[
          { key: "tenantName", label: "Tenant", render: (r) => r.tenantName ?? r.tenantId.slice(0, 8) },
          { key: "source", label: "Source" },
          { key: "reference", label: "Reference", render: (r) => <code className="small">{r.reference}</code> },
          { key: "reason", label: "Why it was refused" },
          { key: "receivedAtUtc", label: "Parked", render: (r) => apiDateTime(r.receivedAtUtc) },
          {
            key: "resolvedAtUtc", label: "Resolved",
            render: (r) => (r.resolvedAtUtc
              ? <span title={r.resolutionNote ?? ""}>{apiDay(r.resolvedAtUtc)} · {r.resolvedBy ?? "not recorded"}</span>
              : <span className="muted">—</span>),
          },
        ]}
        rows={rows}
        getKey={(r) => r.id}
        search={(r) => `${r.tenantName ?? ""} ${r.reference} ${r.reason} ${r.source}`}
        initialSortKey="receivedAtUtc"
        initialSortDir="desc"
        emptyText={state === "open" ? "Nothing is stuck." : "No rows."}
        rowActions={(r) => <button className="ghost small" onClick={() => setOpen(r)}>Open</button>}
      />

      {open && (
        <QuarantineDetail
          row={open}
          busy={busy}
          onClose={() => setOpen(null)}
          onResolve={(note) => {
            setBusy(true);
            return resolveQuarantine(open.id, note)
              .then(() => { setOpen(null); return refresh(); })
              .catch((e) => setError(String(e)))
              .finally(() => setBusy(false));
          }}
        />
      )}
    </>
  );
}

/**
 * One row, its stored payload, and the decision.
 *
 * ⚠ THE NOTE IS REQUIRED and the button stays disabled without one. The server refuses an empty note
 * with a 400, and a dialog that lets you press a button the server will reject is a worse version of
 * the same rule.
 */
function QuarantineDetail({ row, busy, onClose, onResolve }: {
  row: QuarantineRow; busy: boolean; onClose: () => void; onResolve: (note: string) => void | Promise<unknown>;
}) {
  const [note, setNote] = useState("");
  const [payload, setPayload] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    void fetchQuarantinePayload(row.id)
      .then((p) => { if (live) setPayload(p.payload); })
      .catch(() => { if (live) setPayload(null); });
    return () => { live = false; };
  }, [row.id]);

  // ⚠ Escape cancels, and there is a visible ✕ — the dialog contract (till-design D4), which the
  // portal follows too.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="overlay" onClick={onClose}>
      <div className="dialog" onClick={(e) => e.stopPropagation()}>
        <div className="toolbar">
          <h3 className="grow">Quarantined sale</h3>
          <button className="ghost small" onClick={onClose} aria-label="Close">✕</button>
        </div>

        <dl className="env-info">
          <dt>Tenant</dt><dd>{row.tenantName ?? row.tenantId}</dd>
          <dt>Reference</dt><dd><code>{row.reference}</code></dd>
          <dt>Source</dt><dd>{row.source}</dd>
          <dt>Parked</dt><dd>{apiDateTime(row.receivedAtUtc)}</dd>
          <dt>Why</dt><dd>{row.reason}</dd>
        </dl>

        <p className="muted small">{row.retryHint}</p>

        <details className="panel">
          <summary>Stored payload</summary>
          <pre className="small" style={{ maxHeight: 260, overflow: "auto", whiteSpace: "pre-wrap" }}>{payload ?? "…"}</pre>
        </details>

        {row.resolvedAtUtc ? (
          <p className="small">
            Resolved {apiDateTime(row.resolvedAtUtc)} by {row.resolvedBy ?? "not recorded"}
            {row.resolutionNote ? ` — ${row.resolutionNote}` : ""}
          </p>
        ) : (
          <>
            {/* ⚠ SAYS WHAT DISMISSING ACTUALLY DOES. The tenant goes green and the sale stays absent
                from reporting — the honest outcome for a legacy row nobody can reconstruct, and the
                one thing an operator must not misread as "recovered". */}
            <p className="small">
              <strong>Dismissing does not record the sale.</strong> It says the platform is no longer
              waiting on it: this tenant stops being red, and the sale stays out of reporting.
            </p>
            <label className="grow">
              What was decided, and why
              <input value={note} onChange={(e) => setNote(e.target.value)}
                     placeholder="e.g. checked the 2019 till roll — a voided sale, no money taken" />
            </label>
            <div className="toolbar">
              <span className="grow" />
              <button className="ghost" onClick={onClose}>Cancel</button>
              <button className="primary" disabled={busy || !note.trim()} onClick={() => void onResolve(note.trim())}>
                {busy ? "Saving…" : "Dismiss with this note"}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function CommsScreen() {
  const [items, setItems] = useState<AnnouncementRow[]>([]);
  const [form, setForm] = useState({ severity: 0, title: "", body: "", hours: 24, tenantIds: "" });
  const [error, setError] = useState("");
  const refresh = () => fetchAnnouncements().then((a) => { setItems(a); setError(""); }).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, []);

  function submit() {
    const now = new Date();
    const ids = form.tenantIds.split(",").map((s) => s.trim()).filter(Boolean);
    void createAnnouncement({
      severity: form.severity, title: form.title.trim(), body: form.body,
      startsAtUtc: now.toISOString(), endsAtUtc: new Date(now.getTime() + form.hours * 3600_000).toISOString(),
      tenantIds: ids.length ? ids : undefined,
    }).then(() => { setForm({ severity: 0, title: "", body: "", hours: 24, tenantIds: "" }); return refresh(); })
      .catch((e) => setError(String(e)));
  }

  return (
    <>
      {error && <p className="error">{error}</p>}
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Severity
          <select value={form.severity} onChange={(e) => setForm({ ...form, severity: Number(e.target.value) })}>
            <option value={0}>Info</option><option value={1}>Maintenance</option><option value={2}>Incident</option>
          </select>
        </label>
        <label>Title <input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} /></label>
        <label>Body <input value={form.body} onChange={(e) => setForm({ ...form, body: e.target.value })} /></label>
        <label>Hours <input className="short" inputMode="numeric" value={form.hours} onChange={(e) => setForm({ ...form, hours: Number(e.target.value) || 24 })} /></label>
        <label>Tenants <input className="mono" placeholder="all (or comma guids)" value={form.tenantIds} onChange={(e) => setForm({ ...form, tenantIds: e.target.value })} /></label>
        <button className="primary small" disabled={!form.title.trim()} onClick={submit}>Publish</button>
      </div>
      <DataTable<AnnouncementRow>
        columns={[
          { key: "severity", label: "Severity" },
          { key: "title", label: "Title" },
          {
            key: "startsAtUtc", label: "Window",
            render: (a) => <span className="small">{apiDateTime(a.startsAtUtc)} → {apiDateTime(a.endsAtUtc)}</span>,
          },
          { key: "tenantIds", label: "Targets", render: (a) => <span className="small">{a.tenantIds ? "targeted" : "all"}</span> },
        ]}
        rows={items} getKey={(a) => a.id} initialSortKey="startsAtUtc" initialSortDir="desc"
        search={(a) => `${a.title} ${a.severity} ${a.body}`}
        searchPlaceholder="Search title / severity…"
        rowActions={(a) => <button className="ghost small" onClick={() => void deleteAnnouncement(a.id).then(refresh)}>Delete</button>}
        emptyText="No announcements."
      />
    </>
  );
}

function FlagsScreen() {
  const [flags, setFlags] = useState<FlagRow[]>([]);
  const [name, setName] = useState("");
  const [error, setError] = useState("");
  const refresh = () => fetchFlags().then((f) => { setFlags(f); setError(""); }).catch((e) => setError(String(e)));
  useEffect(() => { void refresh(); }, []);

  const toggle = (n: string, enabled: boolean) =>
    void setFlag(n, enabled, enabled ? "re-enabled" : "kill switch").then(refresh).catch((e) => setError(String(e)));

  return (
    <>
      <p className="muted small">A kill switch (Enabled off) disables that feature for EVERY tenant instantly — before plan or overrides.</p>
      {error && <p className="error">{error}</p>}
      <div className="toolbar">
        <input placeholder="feature key (e.g. woo-outbound)" value={name} onChange={(e) => setName(e.target.value)} />
        <button className="ghost small" disabled={!name.trim()} onClick={() => toggle(name.trim(), false)}>Add kill switch</button>
      </div>
      <DataTable<FlagRow>
        columns={[
          { key: "flagName", label: "Feature", render: (f) => <span className="mono">{f.flagName}</span> },
          { key: "enabled", label: "State", render: (f) => <span style={{ color: f.enabled ? "#16a34a" : "#dc2626" }}>{f.enabled ? "enabled" : "KILLED"}</span> },
          { key: "reason", label: "Reason", render: (f) => <span className="small">{f.reason ?? "—"}</span> },
        ]}
        rows={flags} getKey={(f) => f.flagName} initialSortKey="flagName"
        search={(f) => `${f.flagName} ${f.reason ?? ""}`}
        searchPlaceholder="Search feature…"
        rowActions={(f) => <button className="ghost small" onClick={() => toggle(f.flagName, !f.enabled)}>{f.enabled ? "Kill" : "Enable"}</button>}
        emptyText="No global flags set."
      />
    </>
  );
}


/**
 * Create a subscriber, from the operator portal.
 *
 * ⚠⚠ WHY THIS EXISTS. Matt, 2026-08-23: *"I dont need YOU to create it, I need either a way to
 * create it in the operator portal, or a way to sign up for it."* The endpoint has existed since
 * T1.2 and this screen has listed its output all along — nothing ever called it, so the only way in
 * was curl with a hand-copied bearer token.
 *
 * ⚠ THIS IS NOT SELF-SERVE SIGNUP. No application, no email verification, no abuse controls, and no
 * DPA acceptance — an operator is already trusted with impersonation, so none of those five stages
 * protect anything here. A stranger off the internet needs every one of them, and that is WP-signup.
 */
function NewTenantDialog({ onClose, onDone }: { onClose: () => void; onDone: (r: ProvisionedTenant) => void }) {
  const [name, setName] = useState("");
  const [plan, setPlan] = useState("standard");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [isSandbox, setIsSandbox] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  // ⚠ Escape cancels, and there is a visible ✕ — the dialog contract (till-design D4).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape" && !busy) onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, busy]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setError("");
    try {
      onDone(await provisionTenant({
        name: name.trim(), plan, adminEmail: email.trim(), adminPassword: password, isSandbox,
      }));
    } catch (err) {
      setError(String(err instanceof Error ? err.message : err));
      setBusy(false);
    }
  };

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <form className="dialog" onSubmit={submit}>
        <div className="toolbar">
          <h3 className="grow">New subscriber</h3>
          <button type="button" className="ghost small" onClick={onClose} disabled={busy} aria-label="Close">✕</button>
        </div>

        <p className="muted small">
          Creates the subscriber, its company, a first store and an admin login — and gives that admin
          the <strong>Owner</strong> role, so they can set up tills and staff themselves.
        </p>

        <div className="form-grid">
          <label>Business name
            <input value={name} onChange={(e) => setName(e.target.value)} required disabled={busy}
                   placeholder="Test Shop" />
          </label>
          <label>Plan
            <select value={plan} onChange={(e) => setPlan(e.target.value)} disabled={busy}>
              <option value="standard">standard</option>
              <option value="demo">demo</option>
            </select>
          </label>
          <label>Admin email
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required disabled={busy}
                   placeholder="owner@example.com" />
          </label>
          <label>Admin password
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required
                   disabled={busy} autoComplete="new-password" minLength={8} />
          </label>
        </div>

        {/* SANDBOX DEFAULTS ON HERE, AND ONLY HERE. Six places exclude sandbox tenants from the
            commercial rollups - MRR, analytics, usage, contracts - so an unflagged test subscriber
            inflates the revenue figure on this very screen.
            The flag was always SETTABLE (the toggle on the subscriber detail below, and
            PUT /api/v1/platform/tenants/id/sandbox behind it). What was missing was setting it at
            CREATION: between creating a test subscriber and remembering to flip that toggle, it
            counts as real, and nothing prompts anyone.
            Defaulted ON because operator-created subscribers are overwhelmingly tests - a real one
            is a deliberate un-tick, and the warning below makes that choice loud. The API keeps the
            opposite default; see ProvisionRequest. */}
        <label className="check">
          <input type="checkbox" checked={isSandbox} onChange={(e) => setIsSandbox(e.target.checked)} disabled={busy} />
          {" "}Sandbox — a test subscriber, excluded from MRR and every commercial report
        </label>
        {!isSandbox && (
          <p className="warn small">
            ⚠ This will be counted as a <strong>real, paying subscriber</strong> in MRR and the
            commercial reports. Tick Sandbox if you are testing.
          </p>
        )}

        <p className="muted small">
          ⚠ The password is a real credential — it signs into this portal and nothing here expires on
          its own. It cannot be read back afterwards, so record it now.
        </p>

        {error && <p className="error small">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="ghost" onClick={onClose} disabled={busy}>Cancel</button>
          <button type="submit" className="primary"
                  disabled={busy || !name.trim() || !email.trim() || password.length < 8}>
            {busy ? "Creating…" : "Create subscriber"}
          </button>
        </div>
      </form>
    </div>
  );
}

/**
 * What provisioning returned — shown once, because these ids are the only thing the operator needs
 * carry forward and re-reading them means going and looking each one up.
 *
 * ⚠ The store id is the one that matters next: creating the subscriber's first till needs it.
 */
function NewTenantResult({ result, onClose }: { result: ProvisionedTenant; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog">
        <div className="toolbar">
          <h3 className="grow">Subscriber created</h3>
          <button type="button" className="ghost small" onClick={onClose} aria-label="Close">✕</button>
        </div>

        <dl className="env-info">
          <dt>Subscriber</dt><dd><code>{result.tenantId}</code></dd>
          <dt>Company</dt><dd><code>{result.companyId}</code></dd>
          <dt>Store</dt><dd><code>{result.storeId}</code> — created as “Main”, with placeholder address</dd>
          <dt>Admin</dt><dd><code>{result.adminUserId}</code></dd>
        </dl>

        <p className="muted small">
          <strong>Next:</strong> sign in as that admin — they hold Owner. Add a till (Tills), somebody
          who can sell (Users → a role with till permissions, Cashier is enough), and a few items with
          barcodes and VAT bands. ⚠ A till shows nobody on its sign-in list until at least one person
          holds a <code>pos.*</code> permission.
        </p>

        <div className="dialog-actions">
          <button type="button" className="primary" onClick={onClose}>Done</button>
        </div>
      </div>
    </div>
  );
}
function TenantsScreen() {
  const [tenants, setTenants] = useState<PlatformTenant[]>([]);
  const [usage, setUsage] = useState<UsageSummaryRow[]>([]);
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [signals, setSignals] = useState<SignalRow[]>([]);
  const [contracts, setContracts] = useState<ContractLite[]>([]);
  const [creating, setCreating] = useState(false);
  const [created, setCreated] = useState<ProvisionedTenant | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [error, setError] = useState("");

  const refresh = () =>
    Promise.all([fetchTenants(), fetchUsageSummary(), fetchHealth(), fetchSignals(), fetchContracts()])
      .then(([t, u, h, s, c]) => { setTenants(t); setUsage(u); setHealth(h); setSignals(s); setContracts(c); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, []);

  const usageBy = useMemo(() => new Map(usage.map((u) => [u.tenantId, u])), [usage]);
  const healthBy = useMemo(() => new Map((health?.tenants ?? []).map((h) => [h.tenantId, h])), [health]);
  const contractsBy = useMemo(() => new Map(contracts.map((c) => [c.tenantId, c])), [contracts]);
  const signalsBy = useMemo(() => {
    const m = new Map<string, string[]>();
    for (const s of signals) m.set(s.tenantId, [...(m.get(s.tenantId) ?? []), s.signal]);
    return m;
  }, [signals]);

  // A tenant's monthly value = negotiated contract price if set, else the assigned plan's list price.
  const priceOf = (t: PlatformTenant) => contractsBy.get(t.id)?.pricePenceMonthly ?? t.planPricePenceMonthly ?? 0;
  // MRR = live (Trial/Active) non-sandbox subscribers; plus a status headcount.
  const live = tenants.filter((t) => !t.isSandbox && (t.status === 0 || t.status === 1));
  const mrr = live.reduce((sum, t) => sum + priceOf(t), 0);
  const statusCounts = STATUS.map((label, i) => ({ label, n: tenants.filter((t) => !t.isSandbox && t.status === i).length }));
  const daysUntil = (iso: string) => Math.ceil((apiMs(iso) - Date.now()) / 86_400_000);

  if (openId) return <TenantDetail tenantId={openId} tenant={tenants.find((t) => t.id === openId)} onClose={() => setOpenId(null)} />;

  return (
    <>
      {error && <p className="error">{error}</p>}
      {/* NEW SUBSCRIBER, 2026-08-23. Matt: *"I dont need YOU to create it, I need either a way to
          create it in the operator portal, or a way to sign up for it."* The endpoint has existed
          since T1.2 and this screen has listed its output all along - nothing ever called it, so
          the only way in was curl with a hand-copied bearer token.
          NOT self-serve signup (WP-signup): no application, no email verification, no DPA
          acceptance, no abuse controls. This is an operator creating a subscriber directly. */}
      <div className="toolbar">
        <span className="grow" />
        <button className="primary" onClick={() => setCreating(true)}>New subscriber…</button>
      </div>
      {creating && <NewTenantDialog onClose={() => setCreating(false)} onDone={(r) => { setCreating(false); setCreated(r); void refresh(); }} />}
      {created && <NewTenantResult result={created} onClose={() => setCreated(null)} />}
      <div className="stat-row">
        <div className="stat"><span className="stat-label">MRR (live subscribers)</span><span className="stat-value">{pounds(mrr)}</span><span className="muted small">{live.length} paying</span></div>
        {statusCounts.filter((s) => s.n > 0).map((s) => (
          <div className="stat" key={s.label}><span className="stat-label">{s.label}</span><span className="stat-value">{s.n}</span></div>
        ))}
      </div>
      <DataTable<PlatformTenant>
        columns={[
          {
            key: "health", label: "", sortable: false,
            render: (t) => { const h = healthBy.get(t.id); return <Dot color={healthColor(h)} title={healthTitle(h)} />; },
          },
          {
            key: "name", label: "Subscriber",
            render: (t) => (
              <>
                {t.name} {t.isSandbox && <span style={{ background: "#7c3aed", color: "white", fontSize: 10, padding: "1px 5px", borderRadius: 3 }}>SANDBOX</span>}
                <br /><span className="muted small">{short(t.id)}</span>
              </>
            ),
          },
          { key: "status", label: "Status", render: (t) => STATUS[t.status] ?? String(t.status) },
          { key: "plan", label: "Plan", render: (t) => t.plan || "—" },
          { key: "price", label: "£/mo", numeric: true, sort: (t) => priceOf(t), render: (t) => (priceOf(t) ? pounds(priceOf(t)) : "—") },
          {
            key: "renewal", label: "Renewal",
            sort: (t) => contractsBy.get(t.id)?.renewalAtUtc ?? "",
            render: (t) => {
              const c = contractsBy.get(t.id);
              if (!c) return <span className="muted">—</span>;
              const d = daysUntil(c.renewalAtUtc);
              return (
                <span className="small" style={{ color: d <= 30 ? "#d97706" : undefined }}>
                  {apiDay(c.renewalAtUtc)}{d >= 0 ? ` (${d}d)` : " (past)"}
                </span>
              );
            },
          },
          {
            key: "signals", label: "Signals", sortable: false,
            render: (t) => {
              const sigs = signalsBy.get(t.id) ?? [];
              return sigs.length
                ? <>{sigs.map((s) => (
                    <span key={s} title={s} style={{ background: "#d97706", color: "white", fontSize: 10, padding: "1px 5px", borderRadius: 3, marginRight: 3 }}>{s}</span>
                  ))}</>
                : <span className="muted small">—</span>;
            },
          },
          {
            key: "sparkline", label: "30-day sales", sortable: false,
            render: (t) => {
              const series = (usageBy.get(t.id)?.salesDaily ?? []).map((d) => d.value);
              return series.length ? <Sparkline values={series} /> : <span className="muted small">—</span>;
            },
          },
          { key: "sales", label: "Sales", numeric: true, sort: (t) => usageBy.get(t.id)?.totals?.["sales.count"] ?? 0, render: (t) => usageBy.get(t.id)?.totals?.["sales.count"] ?? 0 },
          { key: "err5xx", label: "Err 5xx", numeric: true, sort: (t) => healthBy.get(t.id)?.err5xx ?? 0, render: (t) => healthBy.get(t.id)?.err5xx ?? 0 },
        ]}
        rows={tenants} getKey={(t) => t.id} initialSortKey="name"
        search={(t) => `${t.name} ${t.id} ${t.plan ?? ""} ${STATUS[t.status] ?? ""} ${(signalsBy.get(t.id) ?? []).join(" ")}`}
        searchPlaceholder="Search subscriber / plan / signal…"
        rowActions={(t) => <button className="ghost small" onClick={() => setOpenId(t.id)}>Open</button>}
        emptyText="No subscribers."
      />
    </>
  );
}

function TenantDetail({ tenantId, tenant, onClose }: { tenantId: string; tenant?: PlatformTenant; onClose: () => void }) {
  const [rows, setRows] = useState<HealthDrillRow[]>([]);
  const [status, setStatus] = useState<number>(tenant?.status ?? 1);
  const [error, setError] = useState("");
  const [impUser, setImpUser] = useState("");
  const [impMins, setImpMins] = useState(30);
  const [overrides, setOvrs] = useState<OverrideRow[]>([]);
  const [newFeature, setNewFeature] = useState("");
  const [sla, setSla] = useState<SlaResponse | null>(null);
  const [contract, setContractState] = useState<{ renewalAtUtc: string; termMonths: number; pricePenceMonthly: number; notes: string }>({ renewalAtUtc: "", termMonths: 12, pricePenceMonthly: 0, notes: "" });
  const [compliance, setComplianceState] = useState({
    dataRegion: tenant?.dataRegion ?? "UK",
    dpaSigned: (tenant?.dpaSignedAtUtc ?? "").slice(0, 10),
    dpaRef: tenant?.dpaRef ?? "",
  });
  const [plans, setPlans] = useState<PlanRow[]>([]);
  const [selectedPlan, setSelectedPlan] = useState<string>(tenant?.planId ?? "");
  useEffect(() => { fetchPlans().then(setPlans).catch(() => undefined); }, []);
  const [users, setUsers] = useState<TenantUser[]>([]);
  const [lastPortal, setLastPortal] = useState<string | null>(null);
  useEffect(() => { fetchTenantUsers(tenantId).then((r) => { setUsers(r.users); setLastPortal(r.lastPortalActivityDay); }).catch(() => undefined); }, [tenantId]);
  const [sendFrom, setSendFrom] = useState({ fromAddress: "", domain: "" });
  useEffect(() => {
    fetchSendingIdentities(tenantId).then((rows) => {
      const email = rows.find((r) => r.channel === 0);
      if (email) setSendFrom({ fromAddress: email.fromAddress, domain: email.domain ?? "" });
    }).catch(() => undefined);
  }, [tenantId]);

  const refresh = () =>
    Promise.all([fetchTenantHealth(tenantId), fetchOverrides(tenantId)])
      .then(([h, o]) => { setRows(h.rows); setOvrs(o); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, [tenantId]); // eslint-disable-line react-hooks/exhaustive-deps
  // WP15.2 advisory SLA for the current month (a failed fetch just leaves it blank).
  useEffect(() => { fetchSla(tenantId).then(setSla).catch(() => setSla(null)); }, [tenantId]);
  // WP16.2 contract (204/null when none — leaves the form at its defaults).
  useEffect(() => {
    fetchContract(tenantId).then((c: ContractRow | null) => {
      if (c) setContractState({ renewalAtUtc: c.renewalAtUtc.slice(0, 10), termMonths: c.termMonths, pricePenceMonthly: c.pricePenceMonthly, notes: c.notes ?? "" });
    }).catch(() => undefined);
  }, [tenantId]);

  const saveContract = () =>
    void setContract(tenantId, {
      renewalAtUtc: new Date(contract.renewalAtUtc + "T00:00:00Z").toISOString(),
      termMonths: contract.termMonths, pricePenceMonthly: contract.pricePenceMonthly, notes: contract.notes,
    }).then(() => setError("")).catch((e) => setError(String(e)));

  const saveOverrides = (next: OverrideRow[]) =>
    void setOverrides(tenantId, next.map((o) => ({ entitlement: o.entitlement, deny: o.deny, reason: o.reason ?? undefined })))
      .then(refresh).catch((e) => setError(String(e)));

  // p95 series over the drill window (all route groups collapsed to the max p95 per minute).
  const p95 = useMemo(() => {
    const byMinute = new Map<string, number>();
    for (const r of rows) byMinute.set(r.minuteUtc, Math.max(byMinute.get(r.minuteUtc) ?? 0, r.p95Ms));
    return [...byMinute.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([, v]) => v);
  }, [rows]);

  return (
    <>
      <div className="toolbar">
        <button className="ghost small" onClick={onClose}>← Tenants</button>
        <h3 className="grow">{tenant?.name ?? short(tenantId)}</h3>
      </div>
      {error && <p className="error small">{error}</p>}

      <dl className="kv">
        <dt>Tenant</dt><dd className="mono small">{tenantId}</dd>
        <dt>Plan</dt><dd>{tenant?.plan || "—"}</dd>
        <dt>Entitlements</dt><dd className="small">{tenant?.entitlements?.join(", ") || "—"}</dd>
      </dl>

      <div className="toolbar">
        <label>Status
          <select value={status} onChange={(e) => setStatus(Number(e.target.value))}>
            {STATUS.map((s, i) => <option key={i} value={i}>{s}</option>)}
          </select>
        </label>
        <button className="primary small"
          onClick={() => void setTenantStatus(tenantId, status).then(() => setError("")).catch((e) => setError(String(e)))}>
          Set status
        </button>
      </div>

      {sla && (
        <dl className="kv">
          <dt>SLA ({sla.month})</dt>
          <dd>
            <strong>{sla.availabilityPct}%</strong> availability
            <span className="muted small"> — {sla.goodMinutes}/{sla.minutesWithTraffic} good minutes (&lt;{sla.thresholdPct}% 5xx); advisory, single-box</span>
          </dd>
        </dl>
      )}

      <h4>Users {lastPortal && <span className="muted small">· last portal activity {lastPortal}</span>}</h4>
      <DataTable<TenantUser>
        columns={[
          { key: "name", label: "Name", render: (u) => u.name || short(u.id) },
          { key: "email", label: "Email", render: (u) => <span className="small">{u.email ?? "—"}</span> },
          { key: "roles", label: "Roles", sortable: false, render: (u) => <span className="small">{u.roles.join(", ") || "—"}</span> },
        ]}
        rows={users} getKey={(u) => u.id} initialSortKey="name"
        search={(u) => `${u.name} ${u.email ?? ""} ${u.roles.join(" ")}`}
        searchPlaceholder="Search name / email / role…"
        emptyText="No users found for this subscriber."
      />

      <h4>Subscription plan</h4>
      <p className="muted small">Assigning a plan sets this tenant's list price + entitlement bundle. A negotiated contract price (below) overrides the list price for margin.</p>
      <div className="toolbar">
        <label>Plan
          <select value={selectedPlan} onChange={(e) => setSelectedPlan(e.target.value)}>
            <option value="">(none)</option>
            {plans.map((p) => <option key={p.id} value={p.id}>{p.name} — {pounds(p.pricePenceMonthly)}/mo</option>)}
          </select>
        </label>
        <button className="primary small" onClick={() =>
          void assignPlan(tenantId, selectedPlan || null).then(() => setError("")).catch((e) => setError(String(e)))}>
          Assign plan
        </button>
      </div>

      <h4>Residency & DPA</h4>
      <p className="muted small">Data region (UK today, modelled for the future) + the signed Data Processing Agreement. No signed DPA raises a <span className="mono">dpa-missing</span> signal. Portability = the tenant export; retention = the offboarding sweeper.</p>
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Region <input className="short" value={compliance.dataRegion} onChange={(e) => setComplianceState({ ...compliance, dataRegion: e.target.value })} /></label>
        <label>DPA signed <input type="date" value={compliance.dpaSigned} onChange={(e) => setComplianceState({ ...compliance, dpaSigned: e.target.value })} /></label>
        <label>DPA ref <input value={compliance.dpaRef} onChange={(e) => setComplianceState({ ...compliance, dpaRef: e.target.value })} /></label>
        <button className="primary small" onClick={() =>
          void setCompliance(tenantId, {
            dataRegion: compliance.dataRegion.trim() || "UK",
            dpaSignedAtUtc: compliance.dpaSigned ? new Date(compliance.dpaSigned + "T00:00:00Z").toISOString() : null,
            dpaRef: compliance.dpaRef.trim() || null,
          }).then(() => setError("")).catch((e) => setError(String(e)))}>Save compliance</button>
      </div>

      <h4>Email sending identity</h4>
      <p className="muted small">The from-address this tenant's notifications send as (per-tenant, so a shared domain is never poisoned). Verify SPF/DKIM on the domain before going live.</p>
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>From <input value={sendFrom.fromAddress} onChange={(e) => setSendFrom({ ...sendFrom, fromAddress: e.target.value })} placeholder="no-reply@shop.example" /></label>
        <label>Domain <input value={sendFrom.domain} onChange={(e) => setSendFrom({ ...sendFrom, domain: e.target.value })} placeholder="shop.example" /></label>
        <button className="primary small" disabled={!sendFrom.fromAddress.trim()} onClick={() =>
          void setSendingIdentity(tenantId, { channel: 0, fromAddress: sendFrom.fromAddress.trim(), domain: sendFrom.domain.trim(), verified: false })
            .then(() => setError("")).catch((e) => setError(String(e)))}>Save sending identity</button>
      </div>

      <h4>Contract & renewal</h4>
      <p className="muted small">The relationship record (renewal date, term, negotiated monthly price). Billing owns money-truth once it exists; a renewal-due signal fires 60/30/7 days out.</p>
      <div className="toolbar" style={{ flexWrap: "wrap" }}>
        <label>Renewal <input type="date" value={contract.renewalAtUtc} onChange={(e) => setContractState({ ...contract, renewalAtUtc: e.target.value })} /></label>
        <label>Term (months) <input className="short" inputMode="numeric" value={contract.termMonths} onChange={(e) => setContractState({ ...contract, termMonths: Number(e.target.value) || 0 })} /></label>
        <label>Price £/mo <input className="short" inputMode="numeric" value={(contract.pricePenceMonthly / 100).toString()} onChange={(e) => setContractState({ ...contract, pricePenceMonthly: Math.round((Number(e.target.value) || 0) * 100) })} /></label>
        <label>Notes <input value={contract.notes} onChange={(e) => setContractState({ ...contract, notes: e.target.value })} /></label>
        <button className="primary small" disabled={!contract.renewalAtUtc} onClick={saveContract}>Save contract</button>
      </div>

      <h4>Response p95 (last 24h, ms)</h4>
      {p95.length ? <P95Chart values={p95} /> : <p className="muted small">No request stats yet for this tenant.</p>}

      <h4>Entitlement overrides</h4>
      <p className="muted small">Grant a feature (beta) or deny it (temporary disable). Deny wins over the plan; takes effect immediately.</p>
      <DataTable<OverrideRow>
        columns={[
          { key: "entitlement", label: "Entitlement", render: (o) => <span className="mono">{o.entitlement}</span> },
          { key: "deny", label: "Effect", render: (o) => <span style={{ color: o.deny ? "#dc2626" : "#16a34a" }}>{o.deny ? "deny" : "grant"}</span> },
        ]}
        rows={overrides} getKey={(o) => `${o.entitlement}-${o.deny}`} initialSortKey="entitlement"
        search={(o) => o.entitlement}
        searchPlaceholder="Search entitlement…"
        rowActions={(o) => <button className="ghost small" onClick={() => saveOverrides(overrides.filter((x) => x !== o))}>Remove</button>}
        emptyText="No overrides."
      />
      <div className="toolbar">
        <input className="mono" placeholder="feature or ratelimit.rps:100" value={newFeature} onChange={(e) => setNewFeature(e.target.value)} />
        <button className="ghost small" disabled={!newFeature.trim()} onClick={() => saveOverrides([...overrides, { entitlement: newFeature.trim(), deny: false, reason: "beta", createdAtUtc: "" }])}>Grant</button>
        <button className="ghost small" disabled={!newFeature.trim()} onClick={() => saveOverrides([...overrides, { entitlement: newFeature.trim(), deny: true, reason: "disabled", createdAtUtc: "" }])}>Deny</button>
      </div>

      <h4>Sandbox</h4>
      <div className="toolbar">
        <span className="small">{tenant?.isSandbox ? "This is a SANDBOX tenant." : "Live tenant."}</span>
        <button className="ghost small"
          onClick={() => void setSandbox(tenantId, !(tenant?.isSandbox ?? false)).then(() => window.location.reload()).catch((e) => setError(String(e)))}>
          {tenant?.isSandbox ? "Unmark sandbox" : "Mark as sandbox"}
        </button>
        {tenant?.isSandbox && (
          <button className="primary small"
            onClick={() => { if (confirm("Reset this sandbox to the demo seed? This wipes its transactional data.")) void resetSandbox(tenantId).then(() => refresh()).catch((e) => setError(String(e))); }}>
            Reset to demo
          </button>
        )}
      </div>

      <h4>Impersonate a user</h4>
      <p className="muted small">Opens the portal AS that user (their scopes minus refunds/void/admin), audited, expires automatically.</p>
      <div className="toolbar">
        <label>User id <input className="mono" value={impUser} onChange={(e) => setImpUser(e.target.value)} placeholder="employee guid" /></label>
        <label>Minutes <input className="short" inputMode="numeric" value={impMins} onChange={(e) => setImpMins(Number(e.target.value) || 30)} /></label>
        <button className="primary small" disabled={!impUser.trim()}
          onClick={() => void impersonate(tenantId, impUser.trim(), impMins)
            .then((r) => beginImpersonation({ token: r.token, employeeId: impUser.trim(), name: r.name, expiresAt: r.expiresAt }))
            .catch((e) => setError(String(e instanceof Error ? e.message : e)))}>
          Impersonate
        </button>
      </div>
    </>
  );
}

function P95Chart({ values }: { values: number[] }) {
  const w = 640, h = 160, pad = 24;
  const max = Math.max(1, ...values);
  const step = values.length > 1 ? (w - pad * 2) / (values.length - 1) : 0;
  const pts = values.map((v, i) => `${(pad + i * step).toFixed(1)},${(h - pad - (v / max) * (h - pad * 2)).toFixed(1)}`).join(" ");
  return (
    <svg className="chart" width="100%" viewBox={`0 0 ${w} ${h}`} role="img" aria-label="p95 latency">
      <line x1={pad} y1={h - pad} x2={w - pad} y2={h - pad} stroke="currentColor" opacity={0.2} />
      <line x1={pad} y1={pad} x2={pad} y2={h - pad} stroke="currentColor" opacity={0.2} />
      <text x={pad} y={pad - 6} className="small" fill="currentColor" opacity={0.6}>{max}ms</text>
      <polyline points={pts} fill="none" stroke="currentColor" strokeWidth={1.5} />
    </svg>
  );
}

function HealthScreen() {
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [alerts, setAlerts] = useState<AlertRow[]>([]);
  const [connectors, setConnectors] = useState<ConnectorRow[]>([]);
  const [error, setError] = useState("");

  useEffect(() => {
    Promise.all([fetchHealth(), fetchAlerts(), fetchConnectors()])
      .then(([h, a, c]) => { setHealth(h); setAlerts(a); setConnectors(c); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, []);

  return (
    <>
      {error && <p className="error">{error}</p>}
      <h4>Open alerts</h4>
      {alerts.length === 0 ? <p className="muted small">No open alerts. 🎉</p> : (
        <DataTable<AlertRow>
          columns={[
            { key: "kind", label: "Kind", render: (a) => <span style={{ color: a.kind === "failed" ? "#dc2626" : "#d97706" }}>{a.kind}</span> },
            { key: "jobName", label: "Job" },
            { key: "tenantId", label: "Tenant", render: (a) => short(a.tenantId) },
            { key: "message", label: "Message", render: (a) => <span className="small">{a.message}</span> },
            { key: "occurrences", label: "×", numeric: true },
            { key: "raisedAtUtc", label: "Since", render: (a) => <span className="small">{apiDateTime(a.raisedAtUtc)}</span> },
          ]}
          rows={alerts} getKey={(a) => a.alertKey} initialSortKey="raisedAtUtc" initialSortDir="desc"
          search={(a) => `${a.kind} ${a.jobName} ${a.message}`}
          searchPlaceholder="Search job / message…"
          emptyText="No open alerts."
        />
      )}

      <h4>Per-tenant request health (last hour)</h4>
      <DataTable<HealthTenantRow>
        columns={[
          { key: "dot", label: "", sortable: false, render: (h) => <Dot color={healthColor(h)} title={healthTitle(h)} /> },
          { key: "tenantId", label: "Tenant", render: (h) => short(h.tenantId) },
          // ⚠⚠ THE HEARTBEAT SIGNAL, ON THE SCREEN AND NOT ONLY IN A TOOLTIP (WP-LIVE, 2026-08-21).
          // The dot is now fed by this as well as by request stats, and a colour whose reason is
          // hidden behind a hover is a colour somebody has to take on trust. ⚠ Stale is shown apart
          // from online: "2 (1 stale)" is a different morning from "2".
          {
            key: "tills", label: "Tills", numeric: true,
            sort: (h) => h.tillsOnline ?? 0,
            render: (h) => {
              const online = h.tillsOnline ?? 0, stale = h.tillsStale ?? 0;
              if (online === 0 && stale === 0) return <span className="muted">—</span>;
              return <>{online}{stale > 0 && <span className="muted small"> ({stale} stale)</span>}</>;
            },
          },
          { key: "requests", label: "Requests", numeric: true },
          { key: "err4xx", label: "4xx", numeric: true },
          { key: "err5xx", label: "5xx", numeric: true },
          { key: "errorRatePct", label: "Err %", numeric: true },
          { key: "peakP95Ms", label: "Peak p95", numeric: true, render: (h) => `${h.peakP95Ms}ms` },
          { key: "quarantineOpen", label: "Quarantine", numeric: true },
        ]}
        rows={health?.tenants ?? []} getKey={(h) => h.tenantId} initialSortKey="requests" initialSortDir="desc"
        search={(h) => h.tenantId}
        searchPlaceholder="Search tenant…"
        emptyText="No request traffic in the last hour."
      />

      <h4>Connector health</h4>
      <DataTable<ConnectorRow>
        columns={[
          {
            key: "dot", label: "", sortable: false,
            render: (c) => <Dot color={c.silent || c.errorStreak > 0 ? "#dc2626" : "#16a34a"} title={c.silent ? "silent" : "healthy"} />,
          },
          { key: "connector", label: "Connector", render: (c) => <span className="mono">{c.connector}</span> },
          { key: "tenantId", label: "Tenant", render: (c) => short(c.tenantId ?? null) },
          { key: "lastPollAtUtc", label: "Last poll", render: (c) => <span className="small">{c.lastPollAtUtc ? apiDateTime(c.lastPollAtUtc) : "—"}</span> },
          { key: "lastWebhookAtUtc", label: "Last webhook", render: (c) => <span className="small">{c.lastWebhookAtUtc ? apiDateTime(c.lastWebhookAtUtc) : "—"}</span> },
          { key: "lastOutboundAtUtc", label: "Last outbound", render: (c) => <span className="small">{c.lastOutboundAtUtc ? apiDateTime(c.lastOutboundAtUtc) : "—"}</span> },
          { key: "errorStreak", label: "Err streak", numeric: true },
        ]}
        rows={connectors} getKey={(c) => `${c.connector}:${c.tenantId ?? "platform"}`}
        initialSortKey="errorStreak" initialSortDir="desc"
        search={(c) => `${c.connector} ${c.tenantId ?? ""} ${c.lastError ?? ""}`}
        searchPlaceholder="Search connector / tenant…"
        emptyText="No connectors have reported yet."
      />

      <h4>Outbox consumer lag</h4>
      <DataTable<ConsumerLagRow>
        columns={[
          { key: "consumer", label: "Consumer" },
          { key: "lag", label: "Lag", numeric: true },
        ]}
        rows={health?.consumerLag ?? []} getKey={(c) => c.consumer} initialSortKey="lag" initialSortDir="desc"
        search={(c) => c.consumer}
        searchPlaceholder="Search consumer…"
        emptyText="No consumers registered."
      />
    </>
  );
}

function JobsScreen() {
  const [jobs, setJobs] = useState<JobRow[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    fetchJobs().then((j) => { setJobs(j); setError(""); }).catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, []);

  const color = (s: string) => (s === "failed" ? "#dc2626" : s === "silent" ? "#d97706" : "#16a34a");

  return (
    <>
      {error && <p className="error">{error}</p>}
      <DataTable<JobRow>
        columns={[
          { key: "dot", label: "", sortable: false, render: (j) => <Dot color={color(j.cadenceStatus)} title={j.cadenceStatus} /> },
          { key: "jobName", label: "Job" },
          { key: "tenantId", label: "Tenant", render: (j) => short(j.tenantId) },
          {
            key: "lastRun", label: "Last run",
            sort: (j) => j.finishedAtUtc ?? j.startedAtUtc,
            render: (j) => <span className="small">{apiDateTime(j.finishedAtUtc ?? j.startedAtUtc)}</span>,
          },
          { key: "runStatus", label: "Outcome" },
          { key: "detail", label: "Detail", render: (j) => <span className="small">{j.detail ?? "—"}</span> },
        ]}
        rows={jobs} getKey={(j) => `${j.jobName}:${j.tenantId ?? "platform"}`}
        initialSortKey="lastRun" initialSortDir="desc"
        search={(j) => `${j.jobName} ${j.tenantId ?? ""} ${j.runStatus} ${j.detail ?? ""}`}
        searchPlaceholder="Search job / outcome…"
        emptyText="No jobs have reported yet."
      />
    </>
  );
}
