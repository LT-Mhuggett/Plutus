import { useEffect, useState } from "react";
import { ApiError, gbp } from "./api.ts";

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
  const s = JSON.parse(localStorage.getItem("plutus.portal.session") ?? "null");
  const res = await fetch(url, { method, headers: s ? { Authorization: `Bearer ${s.token}` } : {} });
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
          <table>
            <thead><tr><th>Provider</th><th>Ref</th><th className="num">Amount</th><th className="num">Age (min)</th></tr></thead>
            <tbody>
              {orphans.map((o) => (
                <tr key={o.eventId}>
                  <td>{o.provider}</td>
                  <td className="mono small">{o.providerRef}</td>
                  <td className="num">{gbp(o.amountPence)}</td>
                  <td className="num">{o.ageMinutes}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <button className="ghost small" disabled={busy} onClick={() => void reconcile()}>Re-run matching</button>
        </div>
      )}

      <h3>Banking — per till per day</h3>
      <table>
        <thead>
          <tr>
            <th>Day</th><th>Till</th>
            <th className="num">Float</th><th className="num">Cash</th><th className="num">Card</th>
            <th className="num">In/Out</th><th className="num">Expected</th><th className="num">Counted</th>
            <th className="num">Variance</th><th>Z</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={`${r.tillId}-${r.businessDay}`}>
              <td>{r.businessDay}</td>
              <td className="mono small">{r.tillId.slice(0, 8)}…</td>
              <td className="num">{gbp(r.floatPence)}</td>
              <td className="num">{gbp(r.cashTakingsPence)}</td>
              <td className="num">{gbp(r.cardTakingsPence)}</td>
              <td className="num">{r.paidInPence || r.paidOutPence ? `${gbp(r.paidInPence)} / ${gbp(r.paidOutPence)}` : "—"}</td>
              <td className="num">{gbp(r.expectedCashPence)}</td>
              <td className="num">{r.countedPence == null ? "—" : gbp(r.countedPence)}</td>
              <td className={`num ${r.variancePence ? "error" : ""}`}>
                {r.variancePence == null ? "—" : `${r.variancePence >= 0 ? "+" : ""}${gbp(r.variancePence)}`}
              </td>
              <td>{r.zClosed ? "✅" : <span className="muted">open</span>}</td>
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={10} className="muted">No cash activity in this range.</td></tr>}
        </tbody>
      </table>
      <p className="muted small">
        Expected cash = float + cash takings + paid-ins − paid-outs. Card takings shown for the settlement
        reconciliation (provider adapter arrives with WP7.1's commercial provider choice).
      </p>
    </section>
  );
}
