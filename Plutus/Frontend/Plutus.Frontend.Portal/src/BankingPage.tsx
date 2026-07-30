import { useEffect, useState } from "react";
import { ApiError, gbp } from "./api.ts";
import { accessToken } from "./auth.ts";
import DataTable from "./DataTable.tsx";

// WP7.2 banking view + WP7.1 unresolved-payments queue.

interface BankingRow {
  tillId: string;
  businessDay: string;
  floatPence: number;
  paidInPence: number;
  paidOutPence: number;
  cashTakingsPence: number;
  cardTakingsPence: number;
  expectedCashPence: number;
  countedPence: number | null;
  variancePence: number | null;
  zClosed: boolean;
}
interface OrphanRow { eventId: string; provider: string; providerRef: string; amountPence: number; capturedAtUtc: string; ageMinutes: number }

async function j<T>(method: string, url: string): Promise<T> {
  const token = accessToken();
  const res = await fetch(url, { method, headers: token ? { Authorization: `Bearer ${token}` } : {} });
  if (!res.ok) {
    let detail = `${res.status}`;
    try { detail = (await res.json())?.detail ?? detail; } catch { /* keep */ }
    throw new ApiError(res.status, detail);
  }
  return res.json();
}

const today = () => new Date().toISOString().slice(0, 10);
const daysAgo = (n: number) => new Date(Date.now() - n * 86400_000).toISOString().slice(0, 10);

export default function BankingPage() {
  const [from, setFrom] = useState(daysAgo(7));
  const [to, setTo] = useState(today());
  const [rows, setRows] = useState<BankingRow[]>([]);
  const [orphans, setOrphans] = useState<OrphanRow[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const refresh = () =>
    Promise.all([
      j<BankingRow[]>("GET", `/api/v1/cash/banking?from=${from}&to=${to}`),
      j<OrphanRow[]>("GET", `/api/v1/payments/unresolved`),
    ])
      .then(([b, o]) => { setRows(b); setOrphans(o); setError(""); })
      .catch((e) => setError(String(e instanceof Error ? e.message : e)));

  useEffect(() => { void refresh(); }, [from, to]); // eslint-disable-line react-hooks/exhaustive-deps

  async function reconcile() {
    setBusy(true);
    try { await j("POST", `/api/v1/payments/reconcile`); await refresh(); }
    catch (e) { setError(String(e instanceof Error ? e.message : e)); }
    finally { setBusy(false); }
  }

  return (
    <section className="panel">
      <div className="toolbar">
        <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
      </div>
      {error && <p className="error">{error}</p>}

      {orphans.length > 0 && (
        <div className="callout">
          <strong>{orphans.length} unresolved payment{orphans.length > 1 ? "s" : ""}</strong> — captured at a terminal but
          no matching sale is recorded yet.
          <DataTable<OrphanRow>
            columns={[
              { key: "provider", label: "Provider" },
              { key: "providerRef", label: "Ref", render: (o) => <span className="mono small">{o.providerRef}</span> },
              { key: "amountPence", label: "Amount", numeric: true, render: (o) => gbp(o.amountPence) },
              { key: "ageMinutes", label: "Age (min)", numeric: true },
            ]}
            rows={orphans} getKey={(o) => o.eventId} initialSortKey="ageMinutes" initialSortDir="desc"
            search={(o) => `${o.provider} ${o.providerRef}`}
            searchPlaceholder="Search provider / ref…"
            emptyText="No unresolved payments."
          />
          <button className="ghost small" disabled={busy} onClick={() => void reconcile()}>Re-run matching</button>
        </div>
      )}

      <h3>Banking — per till per day</h3>
      <DataTable<BankingRow>
        columns={[
          { key: "businessDay", label: "Day" },
          { key: "tillId", label: "Till", render: (r) => <span className="mono small">{r.tillId.slice(0, 8)}…</span> },
          { key: "floatPence", label: "Float", numeric: true, render: (r) => gbp(r.floatPence) },
          { key: "cashTakingsPence", label: "Cash", numeric: true, render: (r) => gbp(r.cashTakingsPence) },
          { key: "cardTakingsPence", label: "Card", numeric: true, render: (r) => gbp(r.cardTakingsPence) },
          { key: "paidInPence", label: "In/Out", numeric: true, render: (r) => (r.paidInPence || r.paidOutPence ? `${gbp(r.paidInPence)} / ${gbp(r.paidOutPence)}` : "—") },
          { key: "expectedCashPence", label: "Expected", numeric: true, render: (r) => gbp(r.expectedCashPence) },
          { key: "countedPence", label: "Counted", numeric: true, render: (r) => (r.countedPence == null ? "—" : gbp(r.countedPence)) },
          {
            key: "variancePence", label: "Variance", numeric: true,
            render: (r) => (
              <span className={r.variancePence ? "error" : undefined}>
                {r.variancePence == null ? "—" : `${r.variancePence >= 0 ? "+" : ""}${gbp(r.variancePence)}`}
              </span>
            ),
          },
          { key: "zClosed", label: "Z", render: (r) => (r.zClosed ? "✅" : <span className="muted">open</span>) },
        ]}
        rows={rows} getKey={(r) => `${r.tillId}-${r.businessDay}`}
        initialSortKey="businessDay" initialSortDir="desc"
        search={(r) => `${r.businessDay} ${r.tillId}`}
        searchPlaceholder="Search day / till…"
        emptyText="No cash activity in this range."
      />
      <p className="muted small">
        Expected cash = float + cash takings + paid-ins − paid-outs. Card takings shown for the settlement
        reconciliation (provider adapter arrives with WP7.1's commercial provider choice).
      </p>
    </section>
  );
}
