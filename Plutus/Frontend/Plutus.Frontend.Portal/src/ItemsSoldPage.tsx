import { useEffect, useState } from "react";
import { downloadCsv, fetchItemsSold, gbp, type ItemsSold } from "./api.ts";

const iso = (d: Date) => d.toISOString().slice(0, 10);
const today = () => iso(new Date());
const daysAgo = (n: number) => iso(new Date(Date.now() - n * 86400_000));

/** WP11.4 items-sold report (NatApp "Stock Outtake" parity): date sold, item, location, qty,
 *  unit price, discount, line gross — quick ranges + month/quarter/year, with CSV export. */
export default function ItemsSoldPage() {
  const [from, setFrom] = useState(daysAgo(6));
  const [to, setTo] = useState(today());
  const [data, setData] = useState<ItemsSold | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    setLoading(true);
    setError("");
    fetchItemsSold(from, to)
      .then(setData)
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  }, [from, to]);

  const setRange = (f: string, t: string) => { setFrom(f); setTo(t); };
  const now = new Date();

  function pickMonth(v: string) {
    if (!v) return;
    const [y, m] = v.split("-").map(Number);
    setRange(`${v}-01`, iso(new Date(y, m, 0)));
  }
  function pickQuarter(qIdx: number) {
    const y = now.getFullYear();
    const startMonth = qIdx * 3;
    setRange(iso(new Date(y, startMonth, 1)), iso(new Date(y, startMonth + 3, 0)));
  }
  function pickYear(y: number) { setRange(`${y}-01-01`, `${y}-12-31`); }

  return (
    <section className="panel">
      <h2>Items sold</h2>
      <div className="toolbar">
        <button className="ghost small" onClick={() => setRange(today(), today())}>Today</button>
        <button className="ghost small" onClick={() => setRange(daysAgo(6), today())}>Last 7 days</button>
        <button className="ghost small" onClick={() => setRange(daysAgo(29), today())}>Last 30 days</button>
        <label>Month <input type="month" onChange={(e) => pickMonth(e.target.value)} /></label>
        <label>Quarter
          <select defaultValue="" onChange={(e) => e.target.value && pickQuarter(Number(e.target.value))}>
            <option value="">…</option>
            <option value="0">Q1 (Jan–Mar)</option><option value="1">Q2 (Apr–Jun)</option>
            <option value="2">Q3 (Jul–Sep)</option><option value="3">Q4 (Oct–Dec)</option>
          </select>
        </label>
        <label>Year
          <select defaultValue="" onChange={(e) => e.target.value && pickYear(Number(e.target.value))}>
            <option value="">…</option>
            {Array.from({ length: 8 }, (_, i) => now.getFullYear() - i).map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
        </label>
      </div>
      <div className="toolbar">
        <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <button className="ghost small" disabled={!data?.rows.length}
          onClick={() => void downloadCsv(`/api/v1/reports/items-sold.csv?from=${from}&to=${to}`, `items-sold-${from}-${to}.csv`)}>
          Export CSV
        </button>
      </div>

      {error && <p className="error">{error}</p>}
      {loading && <p className="muted">Loading…</p>}

      {data && (
        <>
          <div className="stat-row">
            <div className="stat"><span className="stat-label">Lines</span><span className="stat-value">{data.count}{data.count >= 2000 ? "+" : ""}</span></div>
            <div className="stat"><span className="stat-label">Units</span><span className="stat-value">{data.totals.qty}</span></div>
            <div className="stat"><span className="stat-label">Discounts</span><span className="stat-value">{gbp(data.totals.discountPence)}</span></div>
            <div className="stat"><span className="stat-label">Gross</span><span className="stat-value">{gbp(data.totals.grossPence)}</span></div>
          </div>
          {data.count >= 2000 && <p className="muted small">Showing the most recent 2,000 lines — narrow the range or export CSV for the full set.</p>}
          <table>
            <thead><tr>
              <th>Date sold</th><th>Item</th><th>Location</th>
              <th className="num">Qty</th><th className="num">Unit</th><th className="num">Discount</th><th className="num">Line gross</th>
            </tr></thead>
            <tbody>
              {data.rows.map((r, i) => (
                <tr key={i}>
                  <td className="small">{new Date(r.dateSold + "Z").toLocaleString("en-GB")}</td>
                  <td><span className="mono small">{r.itemIdOne}</span> {r.itemName}</td>
                  <td className="small">Store {r.storeId} · {r.tillName}</td>
                  <td className="num">{r.qty}</td>
                  <td className="num">{gbp(r.unitPricePence)}</td>
                  <td className="num">{r.discountPence ? gbp(r.discountPence) : "—"}</td>
                  <td className="num">{gbp(r.lineGrossPence)}</td>
                </tr>
              ))}
              {data.rows.length === 0 && <tr><td colSpan={7} className="muted">No items sold in this range.</td></tr>}
            </tbody>
          </table>
        </>
      )}
    </section>
  );
}
