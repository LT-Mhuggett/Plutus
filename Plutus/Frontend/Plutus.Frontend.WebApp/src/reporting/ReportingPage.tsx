import { useEffect, useState } from "react";
import SummaryReport from "./SummaryReport.tsx";
import CustomReport from "../StatisticsPage.tsx";
import VatReport from "./VatReport.tsx";
import DataTable from "../DataTable.tsx";
import {
  fetchV1ItemsSold, fetchV1Staff, fetchV1StockLevels, fetchV1CategorySales, fetchV1BestSellers,
  type V1ItemsSold, type V1Staff, type V1StockResp,
  type V1CategorySales, type V1CategorySalesRow, type V1BestSellerRow,
} from "../api.ts";
import { gbp } from "../money.ts";

const iso = (d: Date) => d.toISOString().slice(0, 10);
const today = () => iso(new Date());
const daysAgo = (n: number) => iso(new Date(Date.now() - n * 86400_000));

// All reports live under this one "Reporting" tab. Summary/Custom/VAT are the original views
// (restored); Items sold + Stock are the v1 reports (also in the portal). Each keeps its own look
// — "available on both surfaces", not "identical".
const SUBTABS = ["Summary", "Custom", "VAT", "Items sold", "Category sales", "Best sellers", "Stock", "Negative stock"] as const;
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
      {sub === "Category sales" && <CategorySalesView />}
      {sub === "Best sellers" && <BestSellersView />}
      {sub === "Stock" && <StockView />}
      {sub === "Negative stock" && <StockView negativeOnly />}
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
            <thead><tr><th>Date</th><th>Item</th><th>Category</th><th>Staff</th><th className="num">Qty</th><th className="num">Unit</th><th className="num">Disc</th><th className="num">Gross</th></tr></thead>
            <tbody>
              {data.rows.map((r, i) => (
                <tr key={i}>
                  <td className="small">{new Date(r.dateSold + "Z").toLocaleString("en-GB")}</td>
                  <td><span className="mono small">{r.itemIdOne}</span> {r.itemName}</td>
                  <td className="small">{r.category ?? "—"}</td>
                  <td className="small">{r.staffName}</td>
                  <td className="num">{r.qty}</td><td className="num">{gbp(r.unitPricePence)}</td>
                  <td className="num">{r.discountPence ? gbp(r.discountPence) : "—"}</td>
                  <td className="num">{gbp(r.lineGrossPence)}</td>
                </tr>
              ))}
              {data.rows.length === 0 && <tr><td colSpan={8} className="muted">No items sold for these filters.</td></tr>}
            </tbody>
          </table>
        </>
      )}
    </>
  );
}

/** Live on-hand stock at this store (v1 — same data as the portal). `negativeOnly` = the
 *  negative-stock report (items below zero on hand). */
function StockView({ negativeOnly = false }: { negativeOnly?: boolean }) {
  const [search, setSearch] = useState("");
  const [take, setTake] = useState(25);
  const [skip, setSkip] = useState(0);
  useEffect(() => { setSkip(0); }, [search, take]);
  const { data, error, denied, loading } = useReport<V1StockResp>(() => fetchV1StockLevels(search, skip, take, negativeOnly ? "negative" : ""), [search, take, skip, negativeOnly]);
  return (
    <>
      {negativeOnly && <p className="muted small">Items showing below zero on hand — usually a missed goods-in or a mis-scan. Fix the count in Inventory management.</p>}
      <div className="toolbar">
        <label>Search <input placeholder="barcode / name" value={search} onChange={(e) => setSearch(e.target.value)} /></label>
        <label>Show
          <select value={take} onChange={(e) => setTake(Number(e.target.value))}>
            <option value={25}>25</option><option value={50}>50</option><option value={100}>100</option>
          </select>
        </label>
      </div>
      {data && !negativeOnly && <p className="muted small">{data.inStock.toLocaleString()} in stock · {data.matched.toLocaleString()} with a stock record · {data.totalCatalogueItems.toLocaleString()} products in the catalogue</p>}
      {denied ? <Denied /> : loading ? <p className="muted">Loading…</p> : error ? <p className="error">{error}</p> : data && (
        <>
          <table>
            <thead><tr><th>Item</th><th>Name</th><th>Category</th><th>Location</th><th className="num">On hand</th></tr></thead>
            <tbody>
              {data.rows.map((l, i) => (
                <tr key={i}><td className="mono small">{l.itemIdOne}</td><td>{l.name ?? <span className="muted">?</span>}</td><td className="small">{l.category ?? "—"}</td><td>{l.location}</td><td className="num">{l.quantity}</td></tr>
              ))}
              {data.rows.length === 0 && <tr><td colSpan={5} className="muted">{negativeOnly ? "No negative stock — everything's at or above zero." : "No stock rows."}</td></tr>}
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

const rangeButtons = (setFrom: (v: string) => void, setTo: (v: string) => void) => (
  <>
    <button className="ghost small" onClick={() => { setFrom(daysAgo(6)); setTo(today()); }}>7 days</button>
    <button className="ghost small" onClick={() => { setFrom(daysAgo(29)); setTo(today()); }}>30 days</button>
  </>
);

/** WP3.7 category sales (till). */
function CategorySalesView() {
  const [from, setFrom] = useState(daysAgo(29));
  const [to, setTo] = useState(today());
  const { data, error, denied, loading } = useReport<V1CategorySales>(() => fetchV1CategorySales(from, to), [from, to]);
  return (
    <>
      <div className="toolbar">
        {rangeButtons(setFrom, setTo)}
        <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
      </div>
      {denied ? <Denied /> : loading ? <p className="muted">Loading…</p> : error ? <p className="error">{error}</p> : data && (
        <>
          <div className="stat-tiles">
            <div className="stat-tile"><span className="stat-label">Categories</span><span className="stat-value">{data.totals.categories}</span></div>
            <div className="stat-tile"><span className="stat-label">Units</span><span className="stat-value">{data.totals.qty}</span></div>
            <div className="stat-tile"><span className="stat-label">Gross</span><span className="stat-value">{gbp(data.totals.grossPence)}</span></div>
          </div>
          <DataTable<V1CategorySalesRow>
            columns={[
              { key: "category", label: "Category" },
              { key: "qty", label: "Units", numeric: true },
              { key: "grossPence", label: "Gross", numeric: true, render: (r) => gbp(r.grossPence), sort: (r) => r.grossPence },
              { key: "sharePct", label: "Share", numeric: true, render: (r) => `${r.sharePct}%`, sort: (r) => r.sharePct },
            ]}
            rows={data.rows} getKey={(r) => r.category} search={(r) => r.category}
            initialSortKey="grossPence" initialSortDir="desc" emptyText="No sales in this range."
          />
        </>
      )}
    </>
  );
}

/** WP3.8 best sellers (till). */
function BestSellersView() {
  const [from, setFrom] = useState(daysAgo(29));
  const [to, setTo] = useState(today());
  const [by, setBy] = useState<"qty" | "gross">("qty");
  const { data, error, denied, loading } = useReport<{ rows: V1BestSellerRow[] }>(() => fetchV1BestSellers(from, to, by, 50), [from, to, by]);
  return (
    <>
      <div className="toolbar">
        {rangeButtons(setFrom, setTo)}
        <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <label>Rank by <select value={by} onChange={(e) => setBy(e.target.value as "qty" | "gross")}><option value="qty">Units</option><option value="gross">Gross</option></select></label>
      </div>
      {denied ? <Denied /> : loading ? <p className="muted">Loading…</p> : error ? <p className="error">{error}</p> : data && (
        <DataTable<V1BestSellerRow>
          columns={[
            { key: "rank", label: "#", numeric: true },
            { key: "itemName", label: "Item", render: (r) => <><span className="mono small">{r.itemIdOne}</span> {r.itemName}</> },
            { key: "category", label: "Category", render: (r) => r.category ?? "—" },
            { key: "qty", label: "Units", numeric: true },
            { key: "grossPence", label: "Gross", numeric: true, render: (r) => gbp(r.grossPence), sort: (r) => r.grossPence },
            { key: "sharePct", label: "Share", numeric: true, render: (r) => `${r.sharePct}%`, sort: (r) => r.sharePct },
          ]}
          rows={data.rows} getKey={(r) => `${r.itemIdOne}-${r.rank}`} search={(r) => `${r.itemIdOne} ${r.itemName} ${r.category ?? ""}`}
          initialSortKey="rank" emptyText="No sales in this range."
        />
      )}
    </>
  );
}
