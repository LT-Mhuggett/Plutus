import { useEffect, useState } from "react";
import { csvUrl, fetchVat, gbp, type VatBucket } from "./api.ts";

const today = () => new Date().toISOString().slice(0, 10);
const startOfYear = () => `${new Date().getFullYear()}-01-01`;

/** VAT view: per-rate summaries per period, shaped to feed the UK VAT return boxes. */
export default function VatPage() {
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(today());
  const [granularity, setGranularity] = useState("month");
  const [buckets, setBuckets] = useState<VatBucket[]>([]);
  const [totals, setTotals] = useState<{ grossPence: number; netPence: number; vatPence: number } | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    setError("");
    fetchVat(from, to, granularity)
      .then((r) => { setBuckets(r.buckets); setTotals(r.totals); })
      .catch((e) => setError(String(e)));
  }, [from, to, granularity]);

  return (
    <section className="panel">
      <div className="toolbar">
        <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <label>
          Granularity{" "}
          <select value={granularity} onChange={(e) => setGranularity(e.target.value)}>
            <option value="month">Month</option><option value="week">Week</option>
            <option value="day">Day</option><option value="year">Year</option>
          </select>
        </label>
        <a className="ghost btn" href={csvUrl("vat", from, to)}>Export CSV</a>
      </div>

      {error && <p className="error">{error}</p>}
      {totals && (
        <div className="stat-row">
          <div className="stat"><span className="stat-label">Gross</span><span className="stat-value">{gbp(totals.grossPence)}</span></div>
          <div className="stat"><span className="stat-label">Net</span><span className="stat-value">{gbp(totals.netPence)}</span></div>
          <div className="stat"><span className="stat-label">VAT due</span><span className="stat-value">{gbp(totals.vatPence)}</span></div>
        </div>
      )}

      <table>
        <thead><tr><th>Period</th><th className="num">Rate</th><th className="num">Gross</th><th className="num">Net</th><th className="num">VAT</th></tr></thead>
        <tbody>
          {buckets.map((b) => (
            <tr key={`${b.period}-${b.vatRateBp}`}>
              <td>{b.period}</td>
              <td className="num">{(b.vatRateBp / 100).toFixed(2)}%</td>
              <td className="num">{gbp(b.grossPence)}</td>
              <td className="num">{gbp(b.netPence)}</td>
              <td className="num">{gbp(b.vatPence)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="muted small">
        Rates are as recorded at the till (basis points from the item's price band) — legacy data
        can show near-20% oddities like 19.81%; those are the source data, not a calculation error.
      </p>
    </section>
  );
}
