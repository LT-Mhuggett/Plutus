import { useEffect, useMemo, useState } from "react";
import {
  fetchTenants, fetchUsageSummary, fetchHealth, fetchTenantHealth, fetchAlerts, fetchJobs, setTenantStatus, impersonate,
  fetchOverrides, setOverrides, fetchFlags, setFlag, setSandbox, resetSandbox,
  fetchAnnouncements, createAnnouncement, deleteAnnouncement, fetchSla,
  fetchSignals, fetchContract, setContract, fetchMargin, fetchAnalytics,
  type PlatformTenant, type UsageSummaryRow, type HealthResponse, type HealthTenantRow,
  type HealthDrillRow, type AlertRow, type JobRow, type OverrideRow, type FlagRow, type AnnouncementRow, type SlaResponse,
  type SignalRow, type ContractRow, type MarginResponse, type AnalyticsResponse,
} from "./api.ts";
import { beginImpersonation } from "./auth.ts";

// WP13.4 operator dashboard (platform-admin only). Three screens answering "is anyone having a
// bad day?": Tenants (usage sparkline + health dot, drill to a tenant's p95), Health (error/lag/
// quarantine + alerts feed), Jobs (latest-per-job cadence grid). Hand-rolled SVG, no chart lib.

const STATUS = ["Trial", "Active", "PastDue", "Suspended", "Closed"];
const short = (id: string | null) => (id ? id.slice(0, 8) : "—");

function healthColor(h?: HealthTenantRow): string {
  if (!h) return "#9ca3af";                                             // grey — no traffic
  if (h.err5xx > 0 || h.quarantineOpen > 0 || h.errorRatePct >= 5) return "#dc2626"; // red
  if (h.peakP95Ms >= 1000 || h.errorRatePct > 0) return "#d97706";      // amber
  return "#16a34a";                                                     // green
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
  const [screen, setScreen] = useState<"Tenants" | "Health" | "Jobs" | "Flags" | "Comms" | "Commercial" | "Analytics">("Tenants");
  return (
    <section className="panel">
      <div className="toolbar">
        <h2 className="grow">Platform</h2>
        {(["Tenants", "Health", "Jobs", "Flags", "Comms", "Commercial", "Analytics"] as const).map((s) => (
          <button key={s} className={s === screen ? "tab active" : "tab"} onClick={() => setScreen(s)}>{s}</button>
        ))}
      </div>
      {screen === "Tenants" && <TenantsScreen />}
      {screen === "Health" && <HealthScreen />}
      {screen === "Jobs" && <JobsScreen />}
      {screen === "Flags" && <FlagsScreen />}
      {screen === "Comms" && <CommsScreen />}
      {screen === "Commercial" && <CommercialScreen />}
      {screen === "Analytics" && <AnalyticsScreen />}
    </section>
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
      <table>
        <thead><tr><th>Tenant</th><th className="num">Activity</th><th className="num">Revenue</th><th className="num">Infra</th><th className="num">Direct</th><th className="num">Cost</th><th className="num">Margin</th></tr></thead>
        <tbody>
          {margin.tenants.map((t) => (
            <tr key={t.tenantId}>
              <td>{t.name}</td>
              <td className="num">{(t.activityShare * 100).toFixed(1)}%</td>
              <td className="num">{pounds(t.revenuePence)}</td>
              <td className="num">{pounds(t.attributedInfraPence)}</td>
              <td className="num">{pounds(t.directCostPence)}</td>
              <td className="num">{pounds(t.costPence)}</td>
              <td className="num" style={{ color: t.marginPence >= 0 ? "#16a34a" : "#dc2626" }}>{pounds(t.marginPence)}</td>
            </tr>
          ))}
          {margin.tenants.length === 0 && <tr><td colSpan={7} className="muted">No tenants.</td></tr>}
        </tbody>
      </table>
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
      <table>
        <thead><tr><th>Metric</th><th className="num">Tenants using</th><th className="num">Total uses</th></tr></thead>
        <tbody>
          {a.adoption.map((m) => <tr key={m.metric}><td className="mono">{m.metric}</td><td className="num">{m.tenantsUsing}</td><td className="num">{m.totalUses}</td></tr>)}
          {a.adoption.length === 0 && <tr><td colSpan={3} className="muted">Nothing above the k-anonymity floor yet.</td></tr>}
        </tbody>
      </table>
      <h4>Surface activity (route groups)</h4>
      <table>
        <thead><tr><th>Route group</th><th className="num">Tenants active</th><th className="num">Requests</th></tr></thead>
        <tbody>
          {a.routeGroups.map((r) => <tr key={r.routeGroup}><td className="mono">{r.routeGroup}</td><td className="num">{r.tenantsActive}</td><td className="num">{r.requests}</td></tr>)}
          {a.routeGroups.length === 0 && <tr><td colSpan={3} className="muted">Nothing above the k-anonymity floor yet.</td></tr>}
        </tbody>
      </table>
      <h4>Login → sale funnel (coarse)</h4>
      <table>
        <thead><tr><th>Stage</th><th className="num">Tenants</th><th className="num">Volume</th></tr></thead>
        <tbody>
          {a.funnel.map((f) => <tr key={f.stage}><td className="mono">{f.stage}</td><td className="num">{f.tenants}</td><td className="num">{f.value ?? "—"}</td></tr>)}
        </tbody>
      </table>
    </>
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
      <table>
        <thead><tr><th>Severity</th><th>Title</th><th>Window</th><th>Targets</th><th /></tr></thead>
        <tbody>
          {items.map((a) => (
            <tr key={a.id}>
              <td>{a.severity}</td><td>{a.title}</td>
              <td className="small">{new Date(a.startsAtUtc + "Z").toLocaleString("en-GB")} → {new Date(a.endsAtUtc + "Z").toLocaleString("en-GB")}</td>
              <td className="small">{a.tenantIds ? "targeted" : "all"}</td>
              <td><button className="ghost small" onClick={() => void deleteAnnouncement(a.id).then(refresh)}>Delete</button></td>
            </tr>
          ))}
          {items.length === 0 && !error && <tr><td colSpan={5} className="muted">No announcements.</td></tr>}
        </tbody>
      </table>
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
      <table>
        <thead><tr><th>Feature</th><th>State</th><th>Reason</th><th /></tr></thead>
        <tbody>
          {flags.map((f) => (
            <tr key={f.flagName}>
              <td className="mono">{f.flagName}</td>
              <td><span style={{ color: f.enabled ? "#16a34a" : "#dc2626" }}>{f.enabled ? "enabled" : "KILLED"}</span></td>
              <td className="small">{f.reason ?? "—"}</td>
              <td><button className="ghost small" onClick={() => toggle(f.flagName, !f.enabled)}>{f.enabled ? "Kill" : "Enable"}</button></td>
            </tr>
          ))}
          {flags.length === 0 && !error && <tr><td colSpan={4} className="muted">No global flags set.</td></tr>}
        </tbody>
      </table>
    </>
  );
}

function TenantsScreen() {
  const [tenants, setTenants] = useState<PlatformTenant[]>([]);
  const [usage, setUsage] = useState<UsageSummaryRow[]>([]);
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [signals, setSignals] = useState<SignalRow[]>([]);
  const [openId, setOpenId] = useState<string | null>(null);
  const [error, setError] = useState("");

  const refresh = () =>
    Promise.all([fetchTenants(), fetchUsageSummary(), fetchHealth(), fetchSignals()])
      .then(([t, u, h, s]) => { setTenants(t); setUsage(u); setHealth(h); setSignals(s); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, []);

  const usageBy = useMemo(() => new Map(usage.map((u) => [u.tenantId, u])), [usage]);
  const healthBy = useMemo(() => new Map((health?.tenants ?? []).map((h) => [h.tenantId, h])), [health]);
  const signalsBy = useMemo(() => {
    const m = new Map<string, string[]>();
    for (const s of signals) m.set(s.tenantId, [...(m.get(s.tenantId) ?? []), s.signal]);
    return m;
  }, [signals]);

  if (openId) return <TenantDetail tenantId={openId} tenant={tenants.find((t) => t.id === openId)} onClose={() => setOpenId(null)} />;

  return (
    <>
      {error && <p className="error">{error}</p>}
      <table>
        <thead><tr><th /><th>Tenant</th><th>Status</th><th>Plan</th><th>Signals</th><th>30-day sales</th><th className="num">Sales</th><th className="num">Err 5xx</th><th /></tr></thead>
        <tbody>
          {tenants.map((t) => {
            const u = usageBy.get(t.id);
            const h = healthBy.get(t.id);
            const series = (u?.salesDaily ?? []).map((d) => d.value);
            return (
              <tr key={t.id}>
                <td><Dot color={healthColor(h)} title={h ? `err5xx ${h.err5xx}, p95 ${h.peakP95Ms}ms, quarantine ${h.quarantineOpen}` : "no traffic (last hour)"} /></td>
                <td>{t.name} {t.isSandbox && <span style={{ background: "#7c3aed", color: "white", fontSize: 10, padding: "1px 5px", borderRadius: 3 }}>SANDBOX</span>}<br /><span className="muted small">{short(t.id)}</span></td>
                <td>{STATUS[t.status] ?? t.status}</td>
                <td>{t.plan || "—"}</td>
                <td>
                  {(signalsBy.get(t.id) ?? []).map((s) => (
                    <span key={s} title={s} style={{ background: "#d97706", color: "white", fontSize: 10, padding: "1px 5px", borderRadius: 3, marginRight: 3 }}>{s}</span>
                  ))}
                  {!(signalsBy.get(t.id)?.length) && <span className="muted small">—</span>}
                </td>
                <td>{series.length ? <Sparkline values={series} /> : <span className="muted small">—</span>}</td>
                <td className="num">{u?.totals?.["sales.count"] ?? 0}</td>
                <td className="num">{h?.err5xx ?? 0}</td>
                <td><button className="ghost small" onClick={() => setOpenId(t.id)}>Open</button></td>
              </tr>
            );
          })}
          {tenants.length === 0 && !error && <tr><td colSpan={9} className="muted">No tenants.</td></tr>}
        </tbody>
      </table>
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
      <table>
        <thead><tr><th>Entitlement</th><th>Effect</th><th /></tr></thead>
        <tbody>
          {overrides.map((o) => (
            <tr key={o.entitlement + o.deny}>
              <td className="mono">{o.entitlement}</td>
              <td><span style={{ color: o.deny ? "#dc2626" : "#16a34a" }}>{o.deny ? "deny" : "grant"}</span></td>
              <td><button className="ghost small" onClick={() => saveOverrides(overrides.filter((x) => x !== o))}>Remove</button></td>
            </tr>
          ))}
          {overrides.length === 0 && <tr><td colSpan={3} className="muted">No overrides.</td></tr>}
        </tbody>
      </table>
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
  const [error, setError] = useState("");

  useEffect(() => {
    Promise.all([fetchHealth(), fetchAlerts()])
      .then(([h, a]) => { setHealth(h); setAlerts(a); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  }, []);

  return (
    <>
      {error && <p className="error">{error}</p>}
      <h4>Open alerts</h4>
      {alerts.length === 0 ? <p className="muted small">No open alerts. 🎉</p> : (
        <table>
          <thead><tr><th>Kind</th><th>Job</th><th>Tenant</th><th>Message</th><th className="num">×</th><th>Since</th></tr></thead>
          <tbody>
            {alerts.map((a) => (
              <tr key={a.alertKey}>
                <td><span style={{ color: a.kind === "failed" ? "#dc2626" : "#d97706" }}>{a.kind}</span></td>
                <td>{a.jobName}</td><td>{short(a.tenantId)}</td>
                <td className="small">{a.message}</td>
                <td className="num">{a.occurrences}</td>
                <td className="small">{new Date(a.raisedAtUtc + "Z").toLocaleString("en-GB")}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h4>Per-tenant request health (last hour)</h4>
      <table>
        <thead><tr><th /><th>Tenant</th><th className="num">Requests</th><th className="num">4xx</th><th className="num">5xx</th><th className="num">Err %</th><th className="num">Peak p95</th><th className="num">Quarantine</th></tr></thead>
        <tbody>
          {(health?.tenants ?? []).map((h) => (
            <tr key={h.tenantId}>
              <td><Dot color={healthColor(h)} title="health" /></td>
              <td>{short(h.tenantId)}</td>
              <td className="num">{h.requests}</td><td className="num">{h.err4xx}</td><td className="num">{h.err5xx}</td>
              <td className="num">{h.errorRatePct}</td><td className="num">{h.peakP95Ms}ms</td><td className="num">{h.quarantineOpen}</td>
            </tr>
          ))}
          {(health?.tenants?.length ?? 0) === 0 && <tr><td colSpan={8} className="muted">No request traffic in the last hour.</td></tr>}
        </tbody>
      </table>

      <h4>Outbox consumer lag</h4>
      <table>
        <thead><tr><th>Consumer</th><th className="num">Lag</th></tr></thead>
        <tbody>
          {(health?.consumerLag ?? []).map((c) => (
            <tr key={c.consumer}><td>{c.consumer}</td><td className="num">{c.lag}</td></tr>
          ))}
          {(health?.consumerLag?.length ?? 0) === 0 && <tr><td colSpan={2} className="muted">No consumers registered.</td></tr>}
        </tbody>
      </table>
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
      <table>
        <thead><tr><th /><th>Job</th><th>Tenant</th><th>Last run</th><th>Outcome</th><th>Detail</th></tr></thead>
        <tbody>
          {jobs.map((j, i) => (
            <tr key={`${j.jobName}:${j.tenantId}:${i}`}>
              <td><Dot color={color(j.cadenceStatus)} title={j.cadenceStatus} /></td>
              <td>{j.jobName}</td><td>{short(j.tenantId)}</td>
              <td className="small">{new Date((j.finishedAtUtc ?? j.startedAtUtc) + "Z").toLocaleString("en-GB")}</td>
              <td>{j.runStatus}</td>
              <td className="small">{j.detail ?? "—"}</td>
            </tr>
          ))}
          {jobs.length === 0 && !error && <tr><td colSpan={6} className="muted">No jobs have reported yet.</td></tr>}
        </tbody>
      </table>
    </>
  );
}
