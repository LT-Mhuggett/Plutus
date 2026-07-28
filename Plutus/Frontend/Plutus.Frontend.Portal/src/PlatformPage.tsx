import { useEffect, useMemo, useState } from "react";
import {
  fetchTenants, fetchUsageSummary, fetchHealth, fetchTenantHealth, fetchAlerts, fetchJobs, setTenantStatus, impersonate,
  fetchOverrides, setOverrides, fetchFlags, setFlag, setSandbox, resetSandbox,
  type PlatformTenant, type UsageSummaryRow, type HealthResponse, type HealthTenantRow,
  type HealthDrillRow, type AlertRow, type JobRow, type OverrideRow, type FlagRow,
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
  const [screen, setScreen] = useState<"Tenants" | "Health" | "Jobs" | "Flags">("Tenants");
  return (
    <section className="panel">
      <div className="toolbar">
        <h2 className="grow">Platform</h2>
        {(["Tenants", "Health", "Jobs", "Flags"] as const).map((s) => (
          <button key={s} className={s === screen ? "tab active" : "tab"} onClick={() => setScreen(s)}>{s}</button>
        ))}
      </div>
      {screen === "Tenants" && <TenantsScreen />}
      {screen === "Health" && <HealthScreen />}
      {screen === "Jobs" && <JobsScreen />}
      {screen === "Flags" && <FlagsScreen />}
    </section>
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
                <td>{t.name} {t.isSandbox && <span style={{ background: "#7c3aed", color: "white", fontSize: 10, padding: "1px 5px", borderRadius: 3 }}>SANDBOX</span>}<br /><span className="muted small">{short(t.id)}</span></td>
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
  const [impUser, setImpUser] = useState("");
  const [impMins, setImpMins] = useState(30);
  const [overrides, setOvrs] = useState<OverrideRow[]>([]);
  const [newFeature, setNewFeature] = useState("");

  const refresh = () =>
    Promise.all([fetchTenantHealth(tenantId), fetchOverrides(tenantId)])
      .then(([h, o]) => { setRows(h.rows); setOvrs(o); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));
  useEffect(() => { void refresh(); }, [tenantId]); // eslint-disable-line react-hooks/exhaustive-deps

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
