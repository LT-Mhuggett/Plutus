import { useEffect, useState } from "react";
import SummaryReport from "./SummaryReport.tsx";
import CustomReport from "../StatisticsPage.tsx";
import VatReport from "./VatReport.tsx";
import {
  fetchV1ItemsSold, fetchV1Staff, fetchV1StockLevels,
  type V1ItemsSold, type V1Staff, type V1StockResp,
} from "../api.ts";
import { gbp } from "../money.ts";

const iso = (d: Date) => d.toISOString().slice(0, 10);
const today = () => iso(new Date());
const daysAgo = (n: number) => iso(new Date(Date.now() - n * 86400_000));

// All reports live under this one "Reporting" tab. Summary/Custom/VAT are the original views
// (restored); Items sold + Stock are the v1 reports (also in the portal). Each keeps its own look
// — "available on both surfaces", not "identical".
const SUBTABS = ["Summary", "Custom", "VAT", "Items sold", "Stock"] as const;
type SubTab = (typeof SUBTABS)[number];

export default function ReportingPage() {
  const [sub, setSub] = useState<SubTab>("Summary");

  return (
    <section className="panel">
      <div className="panel-head">
        <h2>Reporting</h2>
        <nav className="subtabs">
          {SUBTABS.map((s) => (
            <button key={s} className={s === sub ? "subtab active" : "subtab"} onClick={() => setSub(s)}>{s}</button>
          ))}
        </nav>
      </div>
      {sub === "Summary" && <SummaryReport />}
      {sub === "Custom" && <CustomReport />}
      {sub === "VAT" && <VatReport />}
      {sub === "Items sold" && <ItemsSoldView />}
      {sub === "Stock" && <StockView />}
    </section>
  );
}

/** Turns a thrown "API 403 …" into a friendly permission note. */
function useReport<T>(load: () => Promise<T>, deps: unknown[]) {
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

/** Items sold at this store (v1), with a date range + staff filter. */
function ItemsSoldView() {
  const [from, setFrom] = useState(daysAgo(6));
  const [to, setTo] = useState(today());
  const [staffId, setStaffId] = useState("");
  const [staff, setStaff] = useState<V1Staff[]>([]);
  useEffect(() => { fetchV1Staff().then(setStaff).catch(() => setStaff([])); }, []);
  const { data, error, denied, loading } = useReport<V1ItemsSold>(() => fetchV1ItemsSold(from, to, staffId || undefined), [from, to, staffId]);

  return (
    <>
      <div className="toolbar">
        <button className="ghost small" onClick={() => { setFrom(today()); setTo(today()); }}>Today</button>
        <button className="ghost small" onClick={() => { setFrom(daysAgo(6)); setTo(today()); }}>7 days</button>
        <button className="ghost small" onClick={() => { setFrom(daysAgo(29)); setTo(today()); }}>30 days</button>
        <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <label>Staff
          <select value={staffId} onChange={(e) => setStaffId(e.target.value)}>
            <option value="">All staff</option>
            {staff.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </label>
      </div>
      {denied ? <Denied /> : loading ? <p className="muted">Loading…</p> : error ? <p className="error">{error}</p> : data && (
        <>
          <div className="stat-tiles">
            <div className="stat-tile"><span className="stat-label">Lines</span><span className="stat-value">{data.count}{data.count >= 2000 ? "+" : ""}</span></div>
            <div className="stat-tile"><span className="stat-label">Units</span><span className="stat-value">{data.totals.qty}</span></div>
            <div className="stat-tile"><span className="stat-label">Gross</span><span className="stat-value">{gbp(data.totals.grossPence)}</span></div>
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
                  <td className="num">{r.discountPence ? gbp(r.discountPence) : "—"}</td>
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

/** Live on-hand stock at this store (v1 — same data as the portal). */
function StockView() {
  const [search, setSearch] = useState("");
  const [take, setTake] = useState(25);
  const [skip, setSkip] = useState(0);
  useEffect(() => { setSkip(0); }, [search, take]);
  const { data, error, denied, loading } = useReport<V1StockResp>(() => fetchV1StockLevels(search, skip, take), [search, take, skip]);
  return (
    <>
      <div className="toolbar">
        <label>Search <input placeholder="barcode / name" value={search} onChange={(e) => setSearch(e.target.value)} /></label>
        <label>Show
          <select value={take} onChange={(e) => setTake(Number(e.target.value))}>
            <option value={25}>25</option><option value={50}>50</option><option value={100}>100</option>
          </select>
        </label>
      </div>
      {data && <p className="muted small">{data.inStock.toLocaleString()} in stock · {data.matched.toLocaleString()} with a stock record · {data.totalCatalogueItems.toLocaleString()} products in the catalogue</p>}
      {denied ? <Denied /> : loading ? <p className="muted">Loading…</p> : error ? <p className="error">{error}</p> : data && (
        <>
          <table>
            <thead><tr><th>Item</th><th>Name</th><th>Location</th><th className="num">On hand</th></tr></thead>
            <tbody>
              {data.rows.map((l, i) => (
                <tr key={i}><td className="mono small">{l.itemIdOne}</td><td>{l.name ?? <span className="muted">?</span>}</td><td>{l.location}</td><td className="num">{l.quantity}</td></tr>
              ))}
              {data.rows.length === 0 && <tr><td colSpan={4} className="muted">No stock rows.</td></tr>}
            </tbody>
          </table>
          {data.matched > take && (
            <div className="toolbar">
              <button className="ghost small" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - take))}>&larr; Prev</button>
              <span className="muted small">{skip + 1}&ndash;{Math.min(skip + take, data.matched)} of {data.matched.toLocaleString()}</span>
              <button className="ghost small" disabled={skip + take >= data.matched} onClick={() => setSkip(skip + take)}>Next &rarr;</button>
            </div>
          )}
        </>
      )}
    </>
  );
}
