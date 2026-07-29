import { useEffect, useState } from "react";
import DataTable from "./DataTable.tsx";
import {
  fetchCategorySales, fetchBestSellers, fetchStockLevels, gbp,
  type CategorySalesRow, type BestSellerRow, type StockLevelRow,
} from "./api.ts";
import { useNav } from "./nav.tsx";

// WP3.7/3.8/3.9 report screens (portal). All use the shared DataTable.

const iso = (d: Date) => d.toISOString().slice(0, 10);
const today = () => iso(new Date());
const daysAgo = (n: number) => iso(new Date(Date.now() - n * 86400_000));

function RangeBar({ from, to, setFrom, setTo }: { from: string; to: string; setFrom: (v: string) => void; setTo: (v: string) => void }) {
  return (
    <div className="toolbar">
      <button className="ghost small" onClick={() => { setFrom(today()); setTo(today()); }}>Today</button>
      <button className="ghost small" onClick={() => { setFrom(daysAgo(6)); setTo(today()); }}>7 days</button>
      <button className="ghost small" onClick={() => { setFrom(daysAgo(29)); setTo(today()); }}>30 days</button>
      <label>From <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
      <label>To <input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
    </div>
  );
}

export function CategorySalesReport() {
  const [from, setFrom] = useState(daysAgo(29));
  const [to, setTo] = useState(today());
  const [rows, setRows] = useState<CategorySalesRow[]>([]);
  const [totals, setTotals] = useState({ grossPence: 0, qty: 0, categories: 0 });
  const [error, setError] = useState("");
  useEffect(() => {
    fetchCategorySales(from, to).then((d) => { setRows(d.rows); setTotals(d.totals); setError(""); }).catch((e) => setError(String(e)));
  }, [from, to]);
  return (
    <section className="panel">
      <h2>Category sales</h2>
      <RangeBar from={from} to={to} setFrom={setFrom} setTo={setTo} />
      {error && <p className="error">{error}</p>}
      <div className="stat-row">
        <div className="stat"><span className="stat-label">Categories</span><span className="stat-value">{totals.categories}</span></div>
        <div className="stat"><span className="stat-label">Units</span><span className="stat-value">{totals.qty}</span></div>
        <div className="stat"><span className="stat-label">Gross</span><span className="stat-value">{gbp(totals.grossPence)}</span></div>
      </div>
      <DataTable
        columns={[
          { key: "category", label: "Category" },
          { key: "qty", label: "Units", numeric: true },
          { key: "grossPence", label: "Gross", numeric: true, render: (r) => gbp(r.grossPence), sort: (r) => r.grossPence },
          { key: "discountPence", label: "Discounts", numeric: true, render: (r) => gbp(r.discountPence), sort: (r) => r.discountPence },
          { key: "sharePct", label: "Share", numeric: true, render: (r) => `${r.sharePct}%`, sort: (r) => r.sharePct },
        ]}
        rows={rows} getKey={(r) => r.category} search={(r) => r.category}
        initialSortKey="grossPence" initialSortDir="desc" emptyText="No sales in this range."
      />
    </section>
  );
}

export function BestSellersReport() {
  const [from, setFrom] = useState(daysAgo(29));
  const [to, setTo] = useState(today());
  const [by, setBy] = useState<"qty" | "gross">("qty");
  const [rows, setRows] = useState<BestSellerRow[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    fetchBestSellers(from, to, by, 50).then((d) => { setRows(d.rows); setError(""); }).catch((e) => setError(String(e)));
  }, [from, to, by]);
  return (
    <section className="panel">
      <h2>Best sellers</h2>
      <RangeBar from={from} to={to} setFrom={setFrom} setTo={setTo} />
      <div className="toolbar">
        <label>Rank by
          <select value={by} onChange={(e) => setBy(e.target.value as "qty" | "gross")}>
            <option value="qty">Units sold</option><option value="gross">Gross sales</option>
          </select>
        </label>
      </div>
      {error && <p className="error">{error}</p>}
      <DataTable
        columns={[
          { key: "rank", label: "#", numeric: true },
          { key: "itemName", label: "Item", render: (r) => <><span className="mono small">{r.itemIdOne}</span> {r.itemName}</> },
          { key: "category", label: "Category", render: (r) => r.category ?? "—" },
          { key: "qty", label: "Units", numeric: true },
          { key: "grossPence", label: "Gross", numeric: true, render: (r) => gbp(r.grossPence), sort: (r) => r.grossPence },
          { key: "sharePct", label: "Share", numeric: true, render: (r) => `${r.sharePct}%`, sort: (r) => r.sharePct },
        ]}
        rows={rows} getKey={(r) => `${r.itemIdOne}-${r.rank}`} search={(r) => `${r.itemIdOne} ${r.itemName} ${r.category ?? ""}`}
        initialSortKey="rank" initialSortDir="asc" emptyText="No sales in this range."
      />
    </section>
  );
}

export function NegativeStockReport() {
  const { go } = useNav();
  const [rows, setRows] = useState<StockLevelRow[]>([]);
  const [total, setTotal] = useState(0);
  const [skip, setSkip] = useState(0);
  const [take, setTake] = useState(25);
  const [search, setSearch] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    fetchStockLevels({ filter: "negative", search: search || undefined, skip, take })
      .then((d) => { setRows(d.rows); setTotal(d.matched); setError(""); }).catch((e) => setError(String(e)));
  }, [skip, take, search]);
  return (
    <section className="panel">
      <h2>Negative stock</h2>
      <p className="muted small">Items showing below zero on hand — usually a missed goods-in or a mis-scan. Open Inventory to correct the count.</p>
      {error && <p className="error">{error}</p>}
      <DataTable
        columns={[
          { key: "itemIdOne", label: "Item", render: (r) => <><span className="mono small">{r.itemIdOne}</span> {r.name ?? ""}</> },
          { key: "category", label: "Category", render: (r) => r.category ?? "—" },
          { key: "location", label: "Location" },
          { key: "quantity", label: "On hand", numeric: true },
        ]}
        rows={rows} getKey={(r) => `${r.stockLocationId}-${r.itemIdOne}`}
        server={{ total, skip, take, search, onSearch: (s) => { setSearch(s); setSkip(0); }, onPage: (sk, tk) => { setSkip(sk); setTake(tk); } }}
        rowActions={() => <button className="ghost small" onClick={() => go("Inventory")}>Edit in Inventory</button>}
        emptyText="No negative stock — everything's at or above zero."
      />
    </section>
  );
}
