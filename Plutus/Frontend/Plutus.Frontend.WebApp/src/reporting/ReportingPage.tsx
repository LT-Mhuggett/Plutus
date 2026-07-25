import { useEffect, useState } from "react";
import {
  fetchV1ItemsSold, fetchV1Staff, fetchV1StockLevels, fetchV1Summary, fetchV1Vat,
  type V1ItemsSold, type V1Staff, type V1StockLevel, type V1Summary, type V1Vat,
} from "../api.ts";
import { gbp } from "../money.ts";

const iso = (d: Date) => d.toISOString().slice(0, 10);
const today = () => iso(new Date());
const daysAgo = (n: number) => iso(new Date(Date.now() - n * 86400_000));
const vatPct = (bp: number) => `${(bp / 100).toFixed(bp % 100 ? 2 : 0)}%`;

const SUBTABS = ["Summary", "VAT", "Items sold", "Stock"] as const;
type SubTab = (typeof SUBTABS)[number];

/** WP: the till's reports now run on the SAME v1 endpoints as the portal (parity), scoped to
 *  this store. Gated server-side on the reporting permission — cashiers see a "no access" note. */
export default function ReportingPage() {
  const [sub, setSub] = useState<SubTab>("Summary");
  const [from, setFrom] = useState(daysAgo(6));
  const [to, setTo] = useState(today());

  return (
    <section className="panel">
      <div className="panel-head">
        <h2>Reporting <span className="muted small">· this store</span></h2>
        <nav className="subtabs">
          {SUBTABS.map((s) => (
            <button key={s} className={s === sub ? "subtab active" : "subtab"} onClick={() => setSub(s)}>{s}</button>
          ))}
        </nav>
      </div>

      {sub !== "Stock" && (
        <div className="toolbar">
          <button className="ghost small" onClick={() => { setFrom(today()); setTo(today()); }}>Today</button>
          <button className="ghost small" onClick={() => { setFrom(daysAgo(6)); setTo(today()); }}>7 days</button>
          <button className="ghost small" onClick={() => { setFrom(daysAgo(29)); setTo(today()); }}>30 days</button>
          <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
          <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        </div>
      )}

      {sub === "Summary" && <SummaryView from={from} to={to} />}
      {sub === "VAT" && <VatView from={from} to={to} />}
      {sub === "Items sold" && <ItemsSoldView from={from} to={to} />}
      {sub === "Stock" && <StockView />}
    </section>
  );
}

/** Turns a thrown "API 403 …" into a friendly permission note; re-throws nothing. */
function useReport<T>(load: () => Promise<T>, deps: unknown[]): { data: T | null; error: string; denied: boolean; loading: boolean } {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState("");
  const [denied, setDenied] = useState(false);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    setLoading(true); setError(""); setDenied(false);
    load()
      .then(setData)
      .catch((e) => {
        const msg = String(e instanceof Error ? e.message : e);
        if (msg.includes("403")) setDenied(true); else setError(msg);
      })
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
  return { data, error, denied, loading };
}

function Denied() {
  return <p className="muted">You don't have permission to view store reports. Ask a manager for the "reports" permission.</p>;
}

function SummaryView({ from, to }: { from: string; to: string }) {
  const { data, error, denied, loading } = useReport<V1Summary>(() => fetchV1Summary(from, to), [from, to]);
  if (denied) return <Denied />;
  if (loading) return <p className="muted">Loading…</p>;
  if (error) return <p className="error">{error}</p>;
  if (!data) return null;
  const t = data.totals;
  return (
    <>
      <div className="stat-row">
        <div className="stat"><span className="stat-label">Gross</span><span className="stat-value">{gbp(t.grossPence)}</span></div>
        <div className="stat"><span className="stat-label">VAT</span><span className="stat-value">{gbp(t.vatPence)}</span></div>
        <div className="stat"><span className="stat-label">Transactions</span><span className="stat-value">{t.txnCount}</span></div>
        <div className="stat"><span className="stat-label">Avg basket</span><span className="stat-value">{gbp(t.avgBasketPence)}</span></div>
      </div>
      <table>
        <thead><tr><th>Day</th><th className="num">Gross</th><th className="num">VAT</th><th className="num">Txns</th></tr></thead>
        <tbody>
          {data.buckets.map((b) => (
            <tr key={b.period}><td>{b.period}</td><td className="num">{gbp(b.grossPence)}</td><td className="num">{gbp(b.vatPence)}</td><td className="num">{b.txnCount}</td></tr>
          ))}
          {data.buckets.length === 0 && <tr><td colSpan={4} className="muted">No trade in this range.</td></tr>}
        </tbody>
      </table>
    </>
  );
}

function VatView({ from, to }: { from: string; to: string }) {
  const { data, error, denied, loading } = useReport<V1Vat>(() => fetchV1Vat(from, to), [from, to]);
  if (denied) return <Denied />;
  if (loading) return <p className="muted">Loading…</p>;
  if (error) return <p className="error">{error}</p>;
  if (!data) return null;
  return (
    <>
      <div className="stat-row">
        <div className="stat"><span className="stat-label">Gross</span><span className="stat-value">{gbp(data.totals.grossPence)}</span></div>
        <div className="stat"><span className="stat-label">Net</span><span className="stat-value">{gbp(data.totals.netPence)}</span></div>
        <div className="stat"><span className="stat-label">VAT</span><span className="stat-value">{gbp(data.totals.vatPence)}</span></div>
      </div>
      <table>
        <thead><tr><th>Period</th><th>Rate</th><th className="num">Gross</th><th className="num">Net</th><th className="num">VAT</th></tr></thead>
        <tbody>
          {data.buckets.map((b, i) => (
            <tr key={i}><td>{b.period}</td><td>{vatPct(b.vatRateBp)}</td><td className="num">{gbp(b.grossPence)}</td><td className="num">{gbp(b.netPence)}</td><td className="num">{gbp(b.vatPence)}</td></tr>
          ))}
          {data.buckets.length === 0 && <tr><td colSpan={5} className="muted">No VAT in this range.</td></tr>}
        </tbody>
      </table>
    </>
  );
}

function ItemsSoldView({ from, to }: { from: string; to: string }) {
  const [staffId, setStaffId] = useState("");
  const [staff, setStaff] = useState<V1Staff[]>([]);
  useEffect(() => { fetchV1Staff().then(setStaff).catch(() => setStaff([])); }, []);
  const { data, error, denied, loading } = useReport<V1ItemsSold>(() => fetchV1ItemsSold(from, to, staffId || undefined), [from, to, staffId]);

  return (
    <>
      <div className="toolbar">
        <label>Staff
          <select value={staffId} onChange={(e) => setStaffId(e.target.value)}>
            <option value="">All staff</option>
            {staff.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </label>
      </div>
      {denied ? <Denied /> : loading ? <p className="muted">Loading…</p> : error ? <p className="error">{error}</p> : data && (
        <>
          <div className="stat-row">
            <div className="stat"><span className="stat-label">Lines</span><span className="stat-value">{data.count}{data.count >= 2000 ? "+" : ""}</span></div>
            <div className="stat"><span className="stat-label">Units</span><span className="stat-value">{data.totals.qty}</span></div>
            <div className="stat"><span className="stat-label">Gross</span><span className="stat-value">{gbp(data.totals.grossPence)}</span></div>
          </div>
          <table>
            <thead><tr><th>Date</th><th>Item</th><th>Staff</th><th className="num">Qty</th><th className="num">Unit</th><th className="num">Disc</th><th className="num">Gross</th></tr></thead>
            <tbody>
              {data.rows.map((r, i) => (
                <tr key={i}>
                  <td className="small">{new Date(r.dateSold + "Z").toLocaleString("en-GB")}</td>
                  <td><span className="mono small">{r.itemIdOne}</span> {r.itemName}</td>
                  <td className="small">{r.staffName}</td>
                  <td className="num">{r.qty}</td><td className="num">{gbp(r.unitPricePence)}</td>
                  <td className="num">{r.discountPence ? gbp(r.discountPence / 100) : "—"}</td>
                  <td className="num">{gbp(r.lineGrossPence)}</td>
                </tr>
              ))}
              {data.rows.length === 0 && <tr><td colSpan={7} className="muted">No items sold for these filters.</td></tr>}
            </tbody>
          </table>
        </>
      )}
    </>
  );
}

function StockView() {
  const [search, setSearch] = useState("");
  const { data, error, denied, loading } = useReport<V1StockLevel[]>(() => fetchV1StockLevels(search), [search]);
  return (
    <>
      <div className="toolbar">
        <label>Search <input placeholder="barcode / name" value={search} onChange={(e) => setSearch(e.target.value)} /></label>
        <span className="muted small">Live on-hand (same data as the portal).</span>
      </div>
      {denied ? <Denied /> : loading ? <p className="muted">Loading…</p> : error ? <p className="error">{error}</p> : data && (
        <table>
          <thead><tr><th>Item</th><th>Name</th><th>Location</th><th className="num">On hand</th></tr></thead>
          <tbody>
            {data.map((l, i) => (
              <tr key={i}><td className="mono small">{l.itemIdOne}</td><td>{l.name ?? <span className="muted">?</span>}</td><td>{l.location}</td><td className="num">{l.quantity}</td></tr>
            ))}
            {data.length === 0 && <tr><td colSpan={4} className="muted">No stock rows.</td></tr>}
          </tbody>
        </table>
      )}
    </>
  );
}
