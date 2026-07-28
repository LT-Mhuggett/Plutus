import { useEffect, useMemo, useState } from "react";
import {
  fetchTenants, fetchUsageSummary, fetchHealth, fetchTenantHealth, fetchAlerts, fetchJobs, setTenantStatus,
  type PlatformTenant, type UsageSummaryRow, type HealthResponse, type HealthTenantRow,
  type HealthDrillRow, type AlertRow, type JobRow,
} from "./api.ts";

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
  const [screen, setScreen] = useState<"Tenants" | "Health" | "Jobs">("Tenants");
  return (
    <section className="panel">
      <div className="toolbar">
        <h2 className="grow">Platform</h2>
        {(["Tenants", "Health", "Jobs"] as const).map((s) => (
          <button key={s} className={s === screen ? "tab active" : "tab"} onClick={() => setScreen(s)}>{s}</button>
        ))}
      </div>
      {screen === "Tenants" && <TenantsScreen />}
      {screen === "Health" && <HealthScreen />}
      {screen === "Jobs" && <JobsScreen />}
    </section>
  );
}

function TenantsScreen() {
  const [tenants, setTenants] = useState<PlatformTenant[]>([]);
  const [usage, setUsage] = useState<UsageSummaryRow[]>([]);
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [error, setError] = useState("");

  const refresh = () =>
    Promise.all([fetchTenants(), fetchUsageSummary(), fetchHealth()])
      .then(([t, u, h]) => { setTenants(t); setUsage(u); setHealth(h); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, []);

  const usageBy = useMemo(() => new Map(usage.map((u) => [u.tenantId, u])), [usage]);
  const healthBy = useMemo(() => new Map((health?.tenants ?? []).map((h) => [h.tenantId, h])), [health]);

  if (openId) return <TenantDetail tenantId={openId} tenant={tenants.find((t) => t.id === openId)} onClose={() => setOpenId(null)} />;

  return (
    <>
      {error && <p className="error">{error}</p>}
      <table>
        <thead><tr><th /><th>Tenant</th><th>Status</th><th>Plan</th><th>30-day sales</th><th className="num">Sales</th><th className="num">Err 5xx</th><th /></tr></thead>
        <tbody>
          {tenants.map((t) => {
            const u = usageBy.get(t.id);
            const h = healthBy.get(t.id);
            const series = (u?.salesDaily ?? []).map((d) => d.value);
            return (
              <tr key={t.id}>
                <td><Dot color={healthColor(h)} title={h ? `err5xx ${h.err5xx}, p95 ${h.peakP95Ms}ms, quarantine ${h.quarantineOpen}` : "no traffic (last hour)"} /></td>
                <td>{t.name}<br /><span className="muted small">{short(t.id)}</span></td>
                <td>{STATUS[t.status] ?? t.status}</td>
                <td>{t.plan || "—"}</td>
                <td>{series.length ? <Sparkline values={series} /> : <span className="muted small">—</span>}</td>
                <td className="num">{u?.totals?.["sales.count"] ?? 0}</td>
                <td className="num">{h?.err5xx ?? 0}</td>
                <td><button className="ghost small" onClick={() => setOpenId(t.id)}>Open</button></td>
              </tr>
            );
          })}
          {tenants.length === 0 && !error && <tr><td colSpan={8} className="muted">No tenants.</td></tr>}
        </tbody>
      </table>
    </>
  );
}

function TenantDetail({ tenantId, tenant, onClose }: { tenantId: string; tenant?: PlatformTenant; onClose: () => void }) {
  const [rows, setRows] = useState<HealthDrillRow[]>([]);
  const [status, setStatus] = useState<number>(tenant?.status ?? 1);
  const [error, setError] = useState("");

  const refresh = () =>
    fetchTenantHealth(tenantId).then((r) => { setRows(r.rows); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, [tenantId]); // eslint-disable-line react-hooks/exhaustive-deps

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

      <h4>Response p95 (last 24h, ms)</h4>
      {p95.length ? <P95Chart values={p95} /> : <p className="muted small">No request stats yet for this tenant.</p>}
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
