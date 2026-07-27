import { useEffect, useState } from "react";
import {
  downloadCsv, fetchItemsSold, fetchReportStaff, fetchStores, gbp,
  type ItemsSold, type StaffRow, type StoreRow,
} from "./api.ts";
import { SortTh, useSort } from "./sortable.tsx";

const iso = (d: Date) => d.toISOString().slice(0, 10);
const today = () => iso(new Date());
const daysAgo = (n: number) => iso(new Date(Date.now() - n * 86400_000));

/** WP11.4 items-sold report (NatApp "Stock Outtake" parity): date sold, item, location, staff,
 *  qty, unit price, discount, line gross — quick ranges + month/quarter/year, and filters by
 *  store AND staff across stores, with CSV export. Same endpoint the POS uses (store-scoped). */
export default function ItemsSoldPage() {
  const [from, setFrom] = useState(daysAgo(6));
  const [to, setTo] = useState(today());
  const [storeId, setStoreId] = useState<number | "">("");
  const [staffId, setStaffId] = useState("");
  const [stores, setStores] = useState<StoreRow[]>([]);
  const [staff, setStaff] = useState<StaffRow[]>([]);
  const [data, setData] = useState<ItemsSold | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const now = new Date();

  useEffect(() => { fetchStores().then(setStores).catch(() => undefined); }, []);
  // Staff list follows the store selection (store-scoped sellers; all sellers when no store).
  useEffect(() => {
    setStaffId("");
    fetchReportStaff(storeId === "" ? undefined : storeId).then(setStaff).catch(() => setStaff([]));
  }, [storeId]);

  useEffect(() => {
    setLoading(true);
    setError("");
    fetchItemsSold(from, to, storeId === "" ? undefined : storeId, staffId || undefined)
      .then(setData)
      .catch((e) => setError(String(e instanceof Error ? e.message : e)))
      .finally(() => setLoading(false));
  }, [from, to, storeId, staffId]);

  const setRange = (f: string, t: string) => { setFrom(f); setTo(t); };
  function pickMonth(v: string) { if (!v) return; const [y, m] = v.split("-").map(Number); setRange(`${v}-01`, iso(new Date(y, m, 0))); }
  function pickQuarter(q: number) { const y = now.getFullYear(); setRange(iso(new Date(y, q * 3, 1)), iso(new Date(y, q * 3 + 3, 0))); }
  function pickYear(y: number) { setRange(`${y}-01-01`, `${y}-12-31`); }

  const csvUrl = `/api/v1/reports/items-sold.csv?from=${from}&to=${to}` +
    (storeId === "" ? "" : `&storeId=${storeId}`) + (staffId ? `&operatorUserId=${staffId}` : "");

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
            <option value="">…</option><option value="0">Q1</option><option value="1">Q2</option><option value="2">Q3</option><option value="3">Q4</option>
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
        <label>Store
          <select value={storeId} onChange={(e) => setStoreId(e.target.value === "" ? "" : Number(e.target.value))}>
            <option value="">All stores</option>
            {stores.map((s) => <option key={s.id} value={s.id}>Store {s.id}{s.city ? ` · ${s.city}` : ""}</option>)}
          </select>
        </label>
        <label>Staff
          <select value={staffId} onChange={(e) => setStaffId(e.target.value)}>
            <option value="">All staff</option>
            {staff.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </label>
        <button className="ghost small" disabled={!data?.rows.length}
          onClick={() => void downloadCsv(csvUrl, `items-sold-${from}-${to}.csv`)}>Export CSV</button>
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
          {data.count >= 2000 && <p className="muted small">Showing the most recent 2,000 lines — narrow the filters or export CSV for the full set.</p>}
          <ItemsSoldTable rows={data.rows} />
        </>
      )}
    </section>
  );
}

function ItemsSoldTable({ rows }: { rows: ItemsSold["rows"] }) {
  const s = useSort(rows, "dateSold", "desc");
  return (
          <table>
            <thead><tr>
              <SortTh label="Date sold" k="dateSold" {...s} />
              <SortTh label="Item" k="itemName" {...s} />
              <SortTh label="Location" k="tillName" {...s} />
              <SortTh label="Staff" k="staffName" {...s} />
              <SortTh label="Qty" k="qty" num {...s} />
              <SortTh label="Unit" k="unitPricePence" num {...s} />
              <SortTh label="Discount" k="discountPence" num {...s} />
              <SortTh label="Line gross" k="lineGrossPence" num {...s} />
            </tr></thead>
            <tbody>
              {s.sorted.map((r, i) => (
                <tr key={i}>
                  <td className="small">{new Date(r.dateSold + "Z").toLocaleString("en-GB")}</td>
                  <td><span className="mono small">{r.itemIdOne}</span> {r.itemName}</td>
                  <td className="small">Store {r.storeId} · {r.tillName}</td>
                  <td className="small">{r.staffName}</td>
                  <td className="num">{r.qty}</td>
                  <td className="num">{gbp(r.unitPricePence)}</td>
                  <td className="num">{r.discountPence ? gbp(r.discountPence) : "—"}</td>
                  <td className="num">{gbp(r.lineGrossPence)}</td>
                </tr>
              ))}
              {rows.length === 0 && <tr><td colSpan={8} className="muted">No items sold for these filters.</td></tr>}
            </tbody>
          </table>
  );
}
